using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Replaces humanoid motions with generic phantom transform motions.</summary>
    internal sealed class PhantomPlayableMotionConverter
    {
        private readonly BuildContext context;
        private readonly PhantomSlotBuildState slot;
        private readonly PhantomSystemProjectSettingsSnapshot projectSettings;
        private readonly PhantomBuildReport report;
        private readonly VRCAvatarDescriptor.AnimLayerType playable;
        private readonly List<Dictionary<AnimationClip, AnimationClip>> overrideChain;
        private readonly Dictionary<AnimationClip, AnimationClip>[] clipCaches =
        {
            new Dictionary<AnimationClip, AnimationClip>(),
            new Dictionary<AnimationClip, AnimationClip>()
        };
        private readonly Dictionary<BlendTree, BlendTree>[] treeCaches =
        {
            new Dictionary<BlendTree, BlendTree>(),
            new Dictionary<BlendTree, BlendTree>()
        };

        private PhantomPlayableMotionConverter(
            BuildContext context,
            PhantomSlotBuildState slot,
            PhantomSystemProjectSettingsSnapshot projectSettings,
            PhantomBuildReport report,
            VRCAvatarDescriptor.AnimLayerType playable,
            RuntimeAnimatorController runtimeController)
        {
            this.context = context;
            this.slot = slot;
            this.projectSettings = projectSettings;
            this.report = report;
            this.playable = playable;
            overrideChain = BuildOverrideChain(runtimeController);
        }

        public static void Convert(
            BuildContext context,
            PhantomSlotBuildState slot,
            PhantomSystemProjectSettingsSnapshot projectSettings,
            PhantomBuildReport report,
            VRCAvatarDescriptor.AnimLayerType playable,
            RuntimeAnimatorController runtimeController,
            AnimatorController controller,
            AvatarMask descriptorMask)
        {
            var converter = new PhantomPlayableMotionConverter(
                context,
                slot,
                projectSettings,
                report,
                playable,
                runtimeController);
            converter.ConvertController(controller, descriptorMask);
        }

        private void ConvertController(
            AnimatorController controller,
            AvatarMask descriptorMask)
        {
            var layers = controller.layers;
            for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var layer = layers[layerIndex];
                var convertedMask = PhantomAvatarMaskConverter.Convert(
                    slot,
                    descriptorMask,
                    layer.avatarMask,
                    $"PhantomSystem_{slot.SlotId}_{playable}_{layer.name}_Mask");
                if (convertedMask != null)
                {
                    context.AssetSaver.SaveAsset(convertedMask);
                    layer.avatarMask = convertedMask;
                    layers[layerIndex] = layer;
                }
            }
            controller.layers = layers;

            var states = layers
                .Where(layer => layer.stateMachine != null)
                .SelectMany(layer => EnumerateStates(layer.stateMachine))
                .Distinct()
                .ToArray();
            var stateMirrors = states.ToDictionary(state => state, state => state.mirror);
            foreach (var state in states.Where(state => state.mirrorParameterActive))
            {
                report.Warning(
                    $"Slot '{slot.SlotId}' {playable} state '{state.name}' uses parameter-driven Humanoid Mirror "
                    + $"('{state.mirrorParameter}'). PhantomSystem baked the state's default Mirror value "
                    + $"({state.mirror}) and will ignore runtime changes to that Mirror parameter.",
                    state);
            }

            var processedStateMachines = new HashSet<AnimatorStateMachine>();
            layers = controller.layers;
            for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var layer = layers[layerIndex];
                if (layer.syncedLayerIndex >= 0
                    && layer.syncedLayerIndex < layers.Length)
                {
                    foreach (var state in EnumerateStates(
                                 layers[layer.syncedLayerIndex].stateMachine))
                    {
                        var effective = controller.GetStateEffectiveMotion(state, layerIndex);
                        controller.SetStateEffectiveMotion(
                            state,
                            ConvertMotion(effective, stateMirrors[state]),
                            layerIndex);
                    }
                    continue;
                }

                if (layer.stateMachine != null
                    && processedStateMachines.Add(layer.stateMachine))
                {
                    foreach (var state in EnumerateStates(layer.stateMachine))
                    {
                        state.motion = ConvertMotion(state.motion, stateMirrors[state]);
                    }
                }
            }

            foreach (var state in states)
            {
                state.mirror = false;
                state.mirrorParameterActive = false;
                state.mirrorParameter = string.Empty;
            }
        }

        private Motion ConvertMotion(Motion motion, bool inheritedMirror)
        {
            if (motion is AnimationClip clip)
            {
                return ConvertClip(clip, inheritedMirror);
            }

            if (!(motion is BlendTree sourceTree))
            {
                return motion;
            }

            var treeCache = treeCaches[inheritedMirror ? 1 : 0];
            if (treeCache.TryGetValue(sourceTree, out var existingTree))
            {
                return existingTree;
            }

            var sourceChildren = sourceTree.children;
            var convertedMotions = new Motion[sourceChildren.Length];
            var changed = false;
            for (var index = 0; index < sourceChildren.Length; index++)
            {
                convertedMotions[index] = ConvertMotion(
                    sourceChildren[index].motion,
                    CombineMirror(inheritedMirror, sourceChildren[index].mirror));
                changed |= convertedMotions[index] != sourceChildren[index].motion;
            }

            if (!changed)
            {
                treeCache[sourceTree] = sourceTree;
                return sourceTree;
            }

            var tree = CreateConvertedBlendTree(
                sourceTree,
                convertedMotions,
                $"PhantomSystem_{slot.SlotId}_{playable}_{sourceTree.name}"
                + (inheritedMirror ? "_Mirrored" : string.Empty));
            treeCache[sourceTree] = tree;
            context.AssetSaver.SaveAsset(tree);
            return tree;
        }

        internal static BlendTree CreateConvertedBlendTree(
            BlendTree source,
            IReadOnlyList<Motion> convertedMotions,
            string name)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var sourceChildren = source.children;
            if (convertedMotions == null || convertedMotions.Count != sourceChildren.Length)
            {
                throw new ArgumentException(
                    "Converted BlendTree motion count must match the source child count.",
                    nameof(convertedMotions));
            }

            // Do not Object.Instantiate Animator sub-assets here. Unity 2022 can emit a
            // kStrongPPtrMask native assertion while cloning their strong object references.
            // ChildMotion is a value type, so copying the array preserves every per-child
            // option while allowing only the Motion references to be replaced.
            var tree = new BlendTree();
            tree.name = name;
            tree.hideFlags = source.hideFlags;
            tree.blendType = source.blendType;
            tree.blendParameter = source.blendParameter;
            tree.blendParameterY = source.blendParameterY;
            tree.useAutomaticThresholds = false;
            tree.minThreshold = source.minThreshold;
            tree.maxThreshold = source.maxThreshold;

            for (var index = 0; index < sourceChildren.Length; index++)
            {
                sourceChildren[index].motion = convertedMotions[index];
                sourceChildren[index].mirror = false;
            }
            tree.children = sourceChildren;
            tree.useAutomaticThresholds = source.useAutomaticThresholds;
            return tree;
        }

        internal static bool CombineMirror(bool inheritedMirror, bool localMirror)
        {
            return inheritedMirror ^ localMirror;
        }

        private AnimationClip ConvertClip(AnimationClip source, bool inheritedMirror)
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

            var clip = ResolveOverride(source);
            if (clip != source && clipCache.TryGetValue(clip, out existing))
            {
                clipCache[source] = existing;
                return existing;
            }

            var bindings = AnimationUtility.GetCurveBindings(clip);
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            var requiresBake = clip.humanMotion
                               || bindings.Any(binding => binding.type == typeof(Animator))
                               || bindings.Any(binding => binding.type == typeof(Transform)
                                                          && string.IsNullOrEmpty(binding.path));
            var requiresDriverRedirect = bindings.Any(RequiresDriverRedirect)
                                         || objectBindings.Any(RequiresDriverRedirect);
            if (!requiresBake && !requiresDriverRedirect)
            {
                clipCache[source] = clip;
                clipCache[clip] = clip;
                return clip;
            }

            PhantomHumanoidClipBakeResult result = null;
            AnimationClip converted;
            if (requiresBake)
            {
                result = PhantomHumanoidClipBaker.Bake(
                    clip,
                    slot.CloneRoot,
                    new PhantomHumanoidClipBakeOptions
                    {
                        SamplingMode = PhantomHumanoidSamplingMode.Adaptive,
                        SampleRate = projectSettings.MaximumAdaptiveSampleRate,
                        PositionErrorTolerance = projectSettings.PositionErrorTolerance,
                        RotationErrorToleranceDegrees = projectSettings.RotationErrorToleranceDegrees,
                        LocalizeRootMotionToHips = true,
                        InheritedMirror = inheritedMirror,
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
                converted = Object.Instantiate(clip);
            }

            RedirectBoneBindings(converted);
            converted.name = $"PhantomSystem_{slot.SlotId}_{playable}_{clip.name}"
                             + (inheritedMirror ? "_Mirrored" : string.Empty);
            context.AssetSaver.SaveAsset(converted);
            clipCache[source] = converted;
            clipCache[clip] = converted;
            slot.ConvertedClips[converted] = new PhantomConvertedClipMetadata
            {
                SlotId = slot.SlotId,
                Playable = playable.ToString(),
                SourceClip = clip
            };

            if (result != null && result.MissingBones.Count > 0)
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

                    affectedClips.Add($"{playable}/{clip.name}");
                }
            }

            if (result != null
                && result.SkippedAnimatorBindings.Count > 0
                && slot.WarnedUnsupportedAnimatorClips.Add(clip))
            {
                var propertySummary = string.Join(", ", result.SkippedAnimatorBindings
                    .Select(binding => binding.propertyName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .Take(5));
                report.Warning(
                    $"Slot '{slot.SlotId}' {playable} clip '{clip.name}' skipped "
                    + $"{result.SkippedAnimatorBindings.Count} unsupported Animator binding(s)"
                    + (string.IsNullOrEmpty(propertySummary) ? "." : $": {propertySummary}."),
                    clip);
            }

            if (result != null && result.RootMotionLocalized)
            {
                report.Info(
                    $"Slot '{slot.SlotId}' {playable} clip '{clip.name}' localized Root Motion to its phantom Hips.",
                    clip);
            }

            if (result != null && result.IgnoredRootScaleBindings.Count > 0)
            {
                report.Warning(
                    $"Slot '{slot.SlotId}' {playable} clip '{clip.name}' ignored "
                    + $"{result.IgnoredRootScaleBindings.Count} root scale binding(s).",
                    clip);
            }

            if (result != null && result.HitSampleRateLimit)
            {
                report.Warning(
                    $"Slot '{slot.SlotId}' {playable} clip '{clip.name}' reached the "
                    + $"adaptive sampling limit ({result.SampleRate:0.###} FPS) before all bone errors "
                    + "fell within the configured tolerance.",
                    clip);
            }

            return converted;
        }

        private bool RequiresDriverRedirect(EditorCurveBinding binding)
        {
            return binding.type == typeof(Transform)
                   && slot.CloneToAnimationDriverPaths.ContainsKey(binding.path ?? string.Empty);
        }

        private void RedirectBoneBindings(AnimationClip clip)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!RequiresDriverRedirect(binding))
                {
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var redirected = binding;
                redirected.path = slot.CloneToAnimationDriverPaths[binding.path ?? string.Empty];
                AnimationUtility.SetEditorCurve(clip, redirected, curve);
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (!RequiresDriverRedirect(binding))
                {
                    continue;
                }

                var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                var redirected = binding;
                redirected.path = slot.CloneToAnimationDriverPaths[binding.path ?? string.Empty];
                AnimationUtility.SetObjectReferenceCurve(clip, redirected, curve);
            }
        }

        private AnimationClip ResolveOverride(AnimationClip source)
        {
            var current = source;
            foreach (var overrides in overrideChain)
            {
                if (current != null && overrides.TryGetValue(current, out var currentReplacement))
                {
                    current = currentReplacement ?? current;
                }
                else if (source != null && overrides.TryGetValue(source, out var sourceReplacement))
                {
                    current = sourceReplacement ?? current;
                }
            }
            return current;
        }

        private static List<Dictionary<AnimationClip, AnimationClip>> BuildOverrideChain(
            RuntimeAnimatorController runtimeController)
        {
            var controllers = new List<AnimatorOverrideController>();
            var current = runtimeController;
            var visited = new HashSet<RuntimeAnimatorController>();
            while (current is AnimatorOverrideController overrideController
                   && visited.Add(current))
            {
                controllers.Add(overrideController);
                current = overrideController.runtimeAnimatorController;
            }

            controllers.Reverse();
            var result = new List<Dictionary<AnimationClip, AnimationClip>>();
            foreach (var overrideController in controllers)
            {
                var map = new Dictionary<AnimationClip, AnimationClip>();
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(
                    overrideController.overridesCount);
                overrideController.GetOverrides(pairs);
                foreach (var pair in pairs)
                {
                    if (pair.Key != null)
                    {
                        map[pair.Key] = pair.Value ?? pair.Key;
                    }
                }
                result.Add(map);
            }

            return result;
        }

        private static IEnumerable<AnimatorState> EnumerateStates(
            AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null)
            {
                yield break;
            }

            foreach (var child in stateMachine.states)
            {
                if (child.state != null)
                {
                    yield return child.state;
                }
            }

            foreach (var childMachine in stateMachine.stateMachines)
            {
                foreach (var state in EnumerateStates(childMachine.stateMachine))
                {
                    yield return state;
                }
            }
        }
    }
}
