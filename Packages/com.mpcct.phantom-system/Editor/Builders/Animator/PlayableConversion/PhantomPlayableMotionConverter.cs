using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Replaces humanoid motions in an NDMF virtual controller with generic phantom transform motions.</summary>
    internal sealed class PhantomPlayableMotionConverter
    {
        private readonly BuildContext context;
        private readonly PhantomSlotBuildState slot;
        private readonly PhantomSystemProjectSettingsSnapshot projectSettings;
        private readonly PhantomBuildReport report;
        private readonly VRCAvatarDescriptor.AnimLayerType playable;
        private readonly PhantomVirtualPathMapper pathMapper;
        private readonly HashSet<string> animatorParameterNames;
        private readonly Dictionary<VirtualClip, VirtualClip>[] clipCaches =
        {
            new Dictionary<VirtualClip, VirtualClip>(),
            new Dictionary<VirtualClip, VirtualClip>()
        };
        private readonly Dictionary<VirtualBlendTree, VirtualBlendTree>[] treeCaches =
        {
            new Dictionary<VirtualBlendTree, VirtualBlendTree>(),
            new Dictionary<VirtualBlendTree, VirtualBlendTree>()
        };

        private PhantomPlayableMotionConverter(
            BuildContext context,
            PhantomSlotBuildState slot,
            PhantomSystemProjectSettingsSnapshot projectSettings,
            PhantomBuildReport report,
            VRCAvatarDescriptor.AnimLayerType playable,
            IEnumerable<string> animatorParameterNames)
        {
            this.context = context;
            this.slot = slot;
            this.projectSettings = projectSettings;
            this.report = report;
            this.playable = playable;
            this.animatorParameterNames = new HashSet<string>(
                animatorParameterNames ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);
            pathMapper = new PhantomVirtualPathMapper(
                context.AvatarRootTransform,
                slot.CloneRoot);
        }

        public static void Convert(
            BuildContext context,
            PhantomSlotBuildState slot,
            PhantomSystemProjectSettingsSnapshot projectSettings,
            PhantomBuildReport report,
            VRCAvatarDescriptor.AnimLayerType playable,
            VirtualAnimatorController controller,
            AvatarMask descriptorMask,
            AnimatorController baseController)
        {
            var converter = new PhantomPlayableMotionConverter(
                context,
                slot,
                projectSettings,
                report,
                playable,
                controller.Parameters
                    .Where(pair => pair.Value.type == AnimatorControllerParameterType.Float)
                    .Select(pair => pair.Key));
            converter.ConvertController(controller, descriptorMask, baseController);
        }

        private void ConvertController(
            VirtualAnimatorController controller,
            AvatarMask descriptorMask,
            AnimatorController baseController)
        {
            var layers = controller.Layers.ToArray();
            var physicalLayers = baseController != null
                ? baseController.layers
                : Array.Empty<AnimatorControllerLayer>();
            var controllerContext = context.Extension<AnimatorServicesContext>().ControllerContext;
            for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var sourceLayerMask = layerIndex < physicalLayers.Length
                    ? physicalLayers[layerIndex].avatarMask
                    : null;
                var convertedMask = PhantomAvatarMaskConverter.Convert(
                    slot,
                    descriptorMask,
                    sourceLayerMask,
                    $"PhantomSystem_{slot.SlotId}_{playable}_{layers[layerIndex].Name}_Mask",
                    context.AvatarRootTransform);
                if (convertedMask == null)
                {
                    continue;
                }

                try
                {
                    layers[layerIndex].AvatarMask =
                        controllerContext.CloneContext.Clone(convertedMask);
                }
                finally
                {
                    Object.DestroyImmediate(convertedMask);
                }
            }

            var states = layers
                .Where(layer => layer.StateMachine != null)
                .SelectMany(layer => layer.StateMachine.AllStates())
                .Distinct()
                .ToArray();
            var stateMirrors = states.ToDictionary(state => state, state => state.Mirror);
            foreach (var state in states.Where(state => state.MirrorParameter != null))
            {
                report.Warning(
                    $"Slot '{slot.SlotId}' {playable} state '{state.Name}' uses parameter-driven Humanoid Mirror "
                    + $"('{state.MirrorParameter}'). PhantomSystem baked the state's default Mirror value "
                    + $"({state.Mirror}) and will ignore runtime changes to that Mirror parameter.",
                    slot.CloneRoot);
            }

            var processedStateMachines = new HashSet<VirtualStateMachine>();
            foreach (var layer in layers)
            {
                if (layer.SyncedLayerIndex >= 0)
                {
                    var overrides = layer.SyncedLayerMotionOverrides.ToBuilder();
                    foreach (var pair in layer.SyncedLayerMotionOverrides)
                    {
                        overrides[pair.Key] = ConvertMotion(
                            pair.Value,
                            stateMirrors.TryGetValue(pair.Key, out var mirror) && mirror);
                    }
                    layer.SyncedLayerMotionOverrides = overrides.ToImmutable();
                    continue;
                }

                if (layer.StateMachine == null
                    || !processedStateMachines.Add(layer.StateMachine))
                {
                    continue;
                }

                foreach (var state in layer.StateMachine.AllStates())
                {
                    state.Motion = ConvertMotion(
                        state.Motion,
                        stateMirrors.TryGetValue(state, out var mirror) && mirror);
                }
            }

            foreach (var state in states)
            {
                state.Mirror = false;
                state.MirrorParameter = null;
            }
        }

        private VirtualMotion ConvertMotion(VirtualMotion motion, bool inheritedMirror)
        {
            if (motion is VirtualClip clip)
            {
                return ConvertClip(clip, inheritedMirror);
            }

            if (!(motion is VirtualBlendTree sourceTree))
            {
                return motion;
            }

            var treeCache = treeCaches[inheritedMirror ? 1 : 0];
            if (treeCache.TryGetValue(sourceTree, out var existingTree))
            {
                return existingTree;
            }

            var convertedMotions = sourceTree.Children
                .Select(child => ConvertMotion(
                    child.Motion,
                    CombineMirror(inheritedMirror, child.Mirror)))
                .ToArray();
            var changed = sourceTree.Children
                .Zip(convertedMotions, (source, converted) =>
                    source.Motion != converted || source.Mirror)
                .Any(value => value);
            if (!changed)
            {
                treeCache[sourceTree] = sourceTree;
                return sourceTree;
            }

            var tree = CreateConvertedBlendTree(
                sourceTree,
                convertedMotions,
                $"PhantomSystem_{slot.SlotId}_{playable}_{sourceTree.Name}"
                + (inheritedMirror ? "_Mirrored" : string.Empty));
            treeCache[sourceTree] = tree;
            return tree;
        }

        internal static VirtualBlendTree CreateConvertedBlendTree(
            VirtualBlendTree source,
            IReadOnlyList<VirtualMotion> convertedMotions,
            string name)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (convertedMotions == null || convertedMotions.Count != source.Children.Count)
            {
                throw new ArgumentException(
                    "Converted BlendTree motion count must match the source child count.",
                    nameof(convertedMotions));
            }

            var tree = VirtualBlendTree.Create(name);
            tree.BlendType = source.BlendType;
            tree.BlendParameter = source.BlendParameter;
            tree.BlendParameterY = source.BlendParameterY;
            tree.UseAutomaticThresholds = false;
            tree.MinThreshold = source.MinThreshold;
            tree.MaxThreshold = source.MaxThreshold;
            tree.NormalizedBlendValues = source.NormalizedBlendValues;
            tree.Children = source.Children
                .Select((child, index) => new VirtualBlendTree.VirtualChildMotion
                {
                    Motion = convertedMotions[index],
                    CycleOffset = child.CycleOffset,
                    DirectBlendParameter = child.DirectBlendParameter,
                    Mirror = false,
                    Threshold = child.Threshold,
                    Position = child.Position,
                    TimeScale = child.TimeScale
                })
                .ToImmutableList();
            tree.UseAutomaticThresholds = source.UseAutomaticThresholds;
            return tree;
        }

        internal static bool CombineMirror(bool inheritedMirror, bool localMirror)
        {
            return inheritedMirror ^ localMirror;
        }

        private VirtualClip ConvertClip(VirtualClip source, bool inheritedMirror)
        {
            if (source == null)
            {
                return null;
            }

            var clipCache = clipCaches[inheritedMirror ? 1 : 0];
            if (clipCache.TryGetValue(source, out var existing))
            {
                return existing;
            }

            var bindings = source.GetFloatCurveBindings().ToArray();
            var objectBindings = source.GetObjectCurveBindings().ToArray();
            var requiresBake = bindings.Any(RequiresHumanoidBake);
            var requiresDriverRedirect = bindings.Any(RequiresDriverRedirect)
                                         || objectBindings.Any(RequiresDriverRedirect);
            if (!requiresBake && !requiresDriverRedirect)
            {
                clipCache[source] = source;
                return source;
            }

            var sourceName = source.Name;
            AnimationClip localSource = null;
            AnimationClip converted = null;
            try
            {
                localSource = PhantomVirtualClipAdapter.Materialize(
                    source,
                    pathMapper.ToCloneRelative);

                PhantomHumanoidClipBakeResult result = null;
                if (requiresBake)
                {
                    result = PhantomHumanoidClipBaker.Bake(
                        localSource,
                        slot.CloneRoot,
                        new PhantomHumanoidClipBakeOptions
                        {
                            SamplingMode = PhantomHumanoidSamplingMode.Adaptive,
                            SampleRate = projectSettings.MaximumAdaptiveSampleRate,
                            PositionErrorTolerance = projectSettings.PositionErrorTolerance,
                            RotationErrorToleranceDegrees = projectSettings.RotationErrorToleranceDegrees,
                            LocalizeRootMotionToHips = true,
                            InheritedMirror = inheritedMirror,
                            AnimatorParameterNames = animatorParameterNames,
                            OutputBonePaths = slot.AnimationDriverBones.ToDictionary(
                                pair => pair.Key,
                                pair => TransformPathUtility.GetRelativePath(
                                    pair.Value,
                                    slot.CloneRoot.transform)),
                            OutputBoneParentPaths = slot.AnimationDriverPoseParentClonePaths
                        });
                    converted = result.Clip;
                }
                else
                {
                    converted = localSource;
                    localSource = null;
                }

                RedirectBoneBindings(converted);
                converted.hideFlags = HideFlags.None;
                converted.name = $"PhantomSystem_{slot.SlotId}_{playable}_{sourceName}"
                                 + (inheritedMirror ? "_Mirrored" : string.Empty);
                var imported = PhantomVirtualClipAdapter.ImportConverted(
                    context,
                    converted,
                    source,
                    pathMapper.ToAvatarRelative);
                clipCache[source] = imported.Clip;
                slot.ConvertedClipReferences[imported.Reference] = new PhantomConvertedClipMetadata
                {
                    SlotId = slot.SlotId,
                    Playable = playable.ToString(),
                    SourceClipName = sourceName
                };

                ReportBakeDiagnostics(source, sourceName, result);
                return imported.Clip;
            }
            finally
            {
                if (converted != null)
                {
                    Object.DestroyImmediate(converted);
                }
                if (localSource != null)
                {
                    Object.DestroyImmediate(localSource);
                }
            }
        }

        private void ReportBakeDiagnostics(
            VirtualClip source,
            string sourceName,
            PhantomHumanoidClipBakeResult result)
        {
            if (result == null)
            {
                return;
            }

            if (result.MissingBones.Count > 0)
            {
                foreach (var missingBone in result.MissingBones)
                {
                    if (!slot.MissingHumanoidBoneClips.TryGetValue(
                            missingBone,
                            out var affectedClips))
                    {
                        affectedClips = new HashSet<string>(StringComparer.Ordinal);
                        slot.MissingHumanoidBoneClips.Add(missingBone, affectedClips);
                    }

                    affectedClips.Add($"{playable}/{sourceName}");
                }
            }

            if (result.SkippedAnimatorBindings.Count > 0
                && slot.WarnedUnsupportedAnimatorClips.Add(source))
            {
                var propertySummary = string.Join(", ", result.SkippedAnimatorBindings
                    .Select(binding => binding.propertyName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .Take(5));
                report.Warning(
                    $"Slot '{slot.SlotId}' {playable} clip '{sourceName}' skipped "
                    + $"{result.SkippedAnimatorBindings.Count} unsupported Animator binding(s)"
                    + (string.IsNullOrEmpty(propertySummary) ? "." : $": {propertySummary}."),
                    slot.CloneRoot);
            }

            if (result.RootMotionLocalized)
            {
                report.Info(
                    $"Slot '{slot.SlotId}' {playable} clip '{sourceName}' localized Root Motion to its phantom Hips.",
                    slot.CloneRoot);
            }

            if (result.IgnoredRootScaleBindings.Count > 0)
            {
                report.Warning(
                    $"Slot '{slot.SlotId}' {playable} clip '{sourceName}' ignored "
                    + $"{result.IgnoredRootScaleBindings.Count} root scale binding(s).",
                    slot.CloneRoot);
            }

            if (result.HitSampleRateLimit)
            {
                report.Warning(
                    $"Slot '{slot.SlotId}' {playable} clip '{sourceName}' reached the "
                    + $"adaptive sampling limit ({result.SampleRate:0.###} FPS) before all bone errors "
                    + "fell within the configured tolerance.",
                    slot.CloneRoot);
            }
        }

        private bool RequiresDriverRedirect(EditorCurveBinding binding)
        {
            return binding.type == typeof(Transform)
                   && slot.CloneToAnimationDriverPaths.ContainsKey(
                       pathMapper.ToCloneRelative(binding.path));
        }

        private bool RequiresHumanoidBake(EditorCurveBinding binding)
        {
            if (binding.type == typeof(Animator))
            {
                return PhantomAnimationBindingClassifier.Classify(
                    binding,
                    animatorParameterNames) != PhantomAnimationBindingKind.AnimatorParameter;
            }

            return binding.type == typeof(Transform)
                   && string.IsNullOrEmpty(pathMapper.ToCloneRelative(binding.path));
        }

        private void RedirectBoneBindings(AnimationClip clip)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform)
                    || !slot.CloneToAnimationDriverPaths.TryGetValue(
                        binding.path ?? string.Empty,
                        out var targetPath))
                {
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var redirected = binding;
                redirected.path = targetPath;
                AnimationUtility.SetEditorCurve(clip, redirected, curve);
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.type != typeof(Transform)
                    || !slot.CloneToAnimationDriverPaths.TryGetValue(
                        binding.path ?? string.Empty,
                        out var targetPath))
                {
                    continue;
                }

                var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                var redirected = binding;
                redirected.path = targetPath;
                AnimationUtility.SetObjectReferenceCurve(clip, redirected, curve);
            }
        }
    }
}
