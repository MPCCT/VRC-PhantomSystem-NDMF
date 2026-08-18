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
        private readonly PhantomHumanoidBakeCacheSession bakeCache;
        private readonly PhantomBuildReport report;
        private readonly VRCAvatarDescriptor.AnimLayerType playable;
        private readonly PhantomVirtualPathMapper pathMapper;
        private readonly HashSet<string> animatorParameterNames;
        private readonly HashSet<string> fxBonePaths;
        private int filteredFxClipCount;
        private int removedFxAnimatorCurveCount;
        private int removedFxTransformCurveCount;
        private IReadOnlyDictionary<HumanBodyBones, Quaternion> neutralBoneRotations;

        private PhantomPlayableMotionConverter(
            BuildContext context,
            PhantomSlotBuildState slot,
            PhantomSystemProjectSettingsSnapshot projectSettings,
            PhantomHumanoidBakeCacheSession bakeCache,
            PhantomBuildReport report,
            VRCAvatarDescriptor.AnimLayerType playable,
            IEnumerable<string> animatorParameterNames)
        {
            this.context = context;
            this.slot = slot;
            this.projectSettings = projectSettings;
            this.bakeCache = bakeCache;
            this.report = report;
            this.playable = playable;
            this.animatorParameterNames = new HashSet<string>(
                animatorParameterNames ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);
            pathMapper = new PhantomVirtualPathMapper(
                context.AvatarRootTransform,
                slot.CloneRoot);
            fxBonePaths = playable == VRCAvatarDescriptor.AnimLayerType.FX
                ? PhantomFxBoneAnimationFilter.CollectBonePaths(slot)
                : new HashSet<string>(StringComparer.Ordinal);
        }

        public static void Convert(
            BuildContext context,
            PhantomSlotBuildState slot,
            PhantomSystemProjectSettingsSnapshot projectSettings,
            PhantomHumanoidBakeCacheSession bakeCache,
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
                bakeCache,
                report,
                playable,
                controller.Parameters
                    .Where(pair => pair.Value.type == AnimatorControllerParameterType.Float)
                    .Select(pair => pair.Key));
            converter.ConvertController(controller, descriptorMask, baseController);
            converter.ReportFxBoneAnimationFiltering();
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
            var sourceLayerMasks = new AvatarMask[layers.Length];
            for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var physicalLayerIndex = layers[layerIndex].OriginalPhysicalLayerIndex
                                         ?? layerIndex;
                var sourceLayerMask = physicalLayerIndex >= 0
                                      && physicalLayerIndex < physicalLayers.Length
                    ? physicalLayers[physicalLayerIndex].avatarMask
                    : null;
                sourceLayerMasks[layerIndex] = sourceLayerMask;
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
                if (playable == VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    report.Warning(
                        $"Slot '{slot.SlotId}' FX state '{state.Name}' uses parameter-driven Humanoid Mirror "
                        + $"('{state.MirrorParameter}'). Source FX bone animation is removed, so PhantomSystem "
                        + "will ignore this Mirror parameter.",
                        slot.CloneRoot);
                }
                else
                {
                    report.Warning(
                        $"Slot '{slot.SlotId}' {playable} state '{state.Name}' uses parameter-driven Humanoid Mirror "
                        + $"('{state.MirrorParameter}'). PhantomSystem baked the state's default Mirror value "
                        + $"({state.Mirror}) and will ignore runtime changes to that Mirror parameter.",
                        slot.CloneRoot);
                }
            }

            var processedStateMachines = new HashSet<VirtualStateMachine>();
            for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var layer = layers[layerIndex];
                var completionEnabled = ShouldCompleteHumanoidRotations(
                    playable,
                    layer.BlendingMode,
                    false);
                var completionBones = completionEnabled
                    ? PhantomAvatarMaskConverter.CollectActiveHumanoidBones(
                        slot,
                        descriptorMask,
                        sourceLayerMasks[layerIndex])
                    : new HashSet<HumanBodyBones>();
                var session = new MotionConversionSession(completionBones);

                if (layer.SyncedLayerIndex >= 0)
                {
                    var overrides = layer.SyncedLayerMotionOverrides.ToBuilder();
                    foreach (var pair in layer.SyncedLayerMotionOverrides)
                    {
                        overrides[pair.Key] = ConvertMotion(
                            pair.Value,
                            stateMirrors.TryGetValue(pair.Key, out var mirror) && mirror,
                            session,
                            completionEnabled);
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
                        stateMirrors.TryGetValue(state, out var mirror) && mirror,
                        session,
                        completionEnabled);
                }
            }

            foreach (var state in states)
            {
                state.Mirror = false;
                state.MirrorParameter = null;
            }
        }

        internal static bool ShouldCompleteHumanoidRotations(
            VRCAvatarDescriptor.AnimLayerType playable,
            AnimatorLayerBlendingMode blendingMode,
            bool insideDirectBlendTree)
        {
            return !insideDirectBlendTree
                   && blendingMode == AnimatorLayerBlendingMode.Override
                   && (playable == VRCAvatarDescriptor.AnimLayerType.Gesture
                       || playable == VRCAvatarDescriptor.AnimLayerType.Action);
        }

        private VirtualMotion ConvertMotion(
            VirtualMotion motion,
            bool inheritedMirror,
            MotionConversionSession session,
            bool completionEnabled)
        {
            if (motion is VirtualClip clip)
            {
                return ConvertClip(clip, inheritedMirror, session, completionEnabled);
            }

            if (!(motion is VirtualBlendTree sourceTree))
            {
                return motion;
            }

            var cacheIndex = MotionConversionSession.CacheIndex(
                inheritedMirror,
                completionEnabled);
            var treeCache = session.TreeCaches[cacheIndex];
            if (treeCache.TryGetValue(sourceTree, out var existingTree))
            {
                return existingTree;
            }

            var childCompletionEnabled = completionEnabled
                                         && sourceTree.BlendType != BlendTreeType.Direct;
            var convertedMotions = sourceTree.Children
                .Select(child => ConvertMotion(
                    child.Motion,
                    CombineMirror(inheritedMirror, child.Mirror),
                    session,
                    childCompletionEnabled))
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
                + (inheritedMirror ? "_Mirrored" : string.Empty)
                + (completionEnabled ? "_NeutralCompleted" : string.Empty));
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

        private VirtualClip ConvertClip(
            VirtualClip source,
            bool inheritedMirror,
            MotionConversionSession session,
            bool completionEnabled)
        {
            if (source == null)
            {
                return null;
            }

            var clipCache = session.ClipCaches[MotionConversionSession.CacheIndex(
                inheritedMirror,
                completionEnabled)];
            if (clipCache.TryGetValue(source, out var existing))
            {
                return existing;
            }

            if (playable == VRCAvatarDescriptor.AnimLayerType.FX)
            {
                return ConvertFxClip(source, inheritedMirror, clipCache);
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
                PhantomVirtualClipImport imported;
                var outputName = $"PhantomSystem_{slot.SlotId}_{playable}_{sourceName}"
                                 + (inheritedMirror ? "_Mirrored" : string.Empty)
                                 + (completionEnabled ? "_NeutralCompleted" : string.Empty);
                if (requiresBake)
                {
                    var options = new PhantomHumanoidClipBakeOptions
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
                        OutputBoneParentPaths = slot.AnimationDriverPoseParentClonePaths,
                        NeutralRotationCompletionBones = completionEnabled
                            ? session.CompletionBones
                            : null,
                        NeutralBoneRotations = completionEnabled
                            && session.CompletionBones.Count > 0
                            ? GetNeutralBoneRotations()
                            : null,
                        CacheSession = bakeCache
                    };
                    var preparation = PhantomHumanoidClipBaker.PrepareBake(
                        localSource,
                        slot.CloneRoot,
                        options);
                    if (preparation.IsCacheHit)
                    {
                        imported = PhantomHumanoidVirtualClipWriter.WriteCached(
                            context,
                            localSource,
                            source,
                            preparation,
                            slot.CloneToAnimationDriverPaths,
                            pathMapper.ToAvatarRelative,
                            outputName);
                        result = PhantomHumanoidClipBaker.CreateResult(
                            preparation,
                            null,
                            preparation.CachedPoseData);
                        bakeCache?.RecordVirtualClipFastPathHit();
                    }
                    else
                    {
                        result = PhantomHumanoidClipBaker.BakePrepared(preparation);
                        converted = result.Clip;
                        RedirectBoneBindings(converted);
                        converted.hideFlags = HideFlags.None;
                        converted.name = outputName;
                        imported = PhantomVirtualClipAdapter.ImportConverted(
                            context,
                            converted,
                            source,
                            pathMapper.ToAvatarRelative);
                    }
                }
                else
                {
                    converted = localSource;
                    localSource = null;
                    RedirectBoneBindings(converted);
                    converted.hideFlags = HideFlags.None;
                    converted.name = outputName;
                    imported = PhantomVirtualClipAdapter.ImportConverted(
                        context,
                        converted,
                        source,
                        pathMapper.ToAvatarRelative);
                }

                clipCache[source] = imported.Clip;
                RegisterConvertedClip(imported, sourceName);

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

        private VirtualClip ConvertFxClip(
            VirtualClip source,
            bool inheritedMirror,
            IDictionary<VirtualClip, VirtualClip> clipCache)
        {
            var requiresFiltering = source.GetFloatCurveBindings().Any(binding =>
            {
                var mapped = binding;
                mapped.path = pathMapper.ToCloneRelative(binding.path);
                return PhantomFxBoneAnimationFilter.ShouldRemove(
                    mapped,
                    fxBonePaths,
                    animatorParameterNames);
            });
            if (!requiresFiltering)
            {
                clipCache[source] = source;
                return source;
            }

            var sourceName = source.Name;
            AnimationClip converted = null;
            try
            {
                converted = PhantomVirtualClipAdapter.Materialize(
                    source,
                    pathMapper.ToCloneRelative);
                var result = PhantomFxBoneAnimationFilter.Filter(
                    converted,
                    fxBonePaths,
                    animatorParameterNames);
                if (!result.Changed)
                {
                    clipCache[source] = source;
                    return source;
                }

                converted.hideFlags = HideFlags.None;
                converted.name = $"PhantomSystem_{slot.SlotId}_{playable}_{sourceName}"
                                 + (inheritedMirror ? "_Mirrored" : string.Empty);
                var imported = PhantomVirtualClipAdapter.ImportConverted(
                    context,
                    converted,
                    source,
                    path => PhantomFxBoneAnimationFilter.IsDummyPath(path)
                        ? path
                        : pathMapper.ToAvatarRelative(path));
                clipCache[source] = imported.Clip;
                RegisterConvertedClip(imported, sourceName);

                filteredFxClipCount++;
                removedFxAnimatorCurveCount += result.RemovedAnimatorCurves;
                removedFxTransformCurveCount += result.RemovedTransformCurves;
                return imported.Clip;
            }
            finally
            {
                if (converted != null)
                {
                    Object.DestroyImmediate(converted);
                }
            }
        }

        private void RegisterConvertedClip(
            PhantomVirtualClipImport imported,
            string sourceName)
        {
            slot.ConvertedClipReferences[imported.Reference] = new PhantomConvertedClipMetadata
            {
                SlotId = slot.SlotId,
                Playable = playable.ToString(),
                SourceClipName = sourceName
            };
        }

        private IReadOnlyDictionary<HumanBodyBones, Quaternion> GetNeutralBoneRotations()
        {
            return neutralBoneRotations ??= PhantomHumanoidPoseSampler.SampleNeutralBoneRotations(
                slot.CloneRoot,
                slot.AnimationDriverPoseParentClonePaths,
                slot.AnimationDriverBones.Keys);
        }

        private void ReportFxBoneAnimationFiltering()
        {
            if (filteredFxClipCount == 0)
            {
                return;
            }

            report.Info(
                $"Slot '{slot.SlotId}' removed {removedFxTransformCurveCount} skeletal Transform "
                + $"curve(s) and {removedFxAnimatorCurveCount} non-parameter Animator curve(s) from "
                + $"{filteredFxClipCount} Source FX clip variant(s). A dummy binding preserves each "
                + "affected clip's duration and prevents empty-clip Write Defaults behavior.",
                slot.CloneRoot);
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

        private sealed class MotionConversionSession
        {
            internal readonly ISet<HumanBodyBones> CompletionBones;
            internal readonly Dictionary<VirtualClip, VirtualClip>[] ClipCaches;
            internal readonly Dictionary<VirtualBlendTree, VirtualBlendTree>[] TreeCaches;

            internal MotionConversionSession(ISet<HumanBodyBones> completionBones)
            {
                CompletionBones = completionBones ?? new HashSet<HumanBodyBones>();
                ClipCaches = CreateCaches<VirtualClip>();
                TreeCaches = CreateCaches<VirtualBlendTree>();
            }

            internal static int CacheIndex(bool inheritedMirror, bool completionEnabled)
            {
                return (inheritedMirror ? 1 : 0) | (completionEnabled ? 2 : 0);
            }

            private static Dictionary<T, T>[] CreateCaches<T>() where T : class
            {
                return new[]
                {
                    new Dictionary<T, T>(),
                    new Dictionary<T, T>(),
                    new Dictionary<T, T>(),
                    new Dictionary<T, T>()
                };
            }
        }
    }
}
