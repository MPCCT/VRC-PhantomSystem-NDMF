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
        private readonly Dictionary<AnimationClip, AnimationClip> clipCache =
            new Dictionary<AnimationClip, AnimationClip>();
        private readonly Dictionary<BlendTree, BlendTree> treeCache =
            new Dictionary<BlendTree, BlendTree>();

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
                            ConvertMotion(effective),
                            layerIndex);
                    }
                    continue;
                }

                if (layer.stateMachine != null
                    && processedStateMachines.Add(layer.stateMachine))
                {
                    foreach (var state in EnumerateStates(layer.stateMachine))
                    {
                        state.motion = ConvertMotion(state.motion);
                    }
                }
            }
        }

        private Motion ConvertMotion(Motion motion)
        {
            if (motion is AnimationClip clip)
            {
                return ConvertClip(clip);
            }

            if (!(motion is BlendTree sourceTree))
            {
                return motion;
            }

            if (treeCache.TryGetValue(sourceTree, out var existingTree))
            {
                return existingTree;
            }

            var sourceChildren = sourceTree.children;
            var convertedMotions = new Motion[sourceChildren.Length];
            var changed = false;
            for (var index = 0; index < sourceChildren.Length; index++)
            {
                convertedMotions[index] = ConvertMotion(sourceChildren[index].motion);
                changed |= convertedMotions[index] != sourceChildren[index].motion;
            }

            if (!changed)
            {
                treeCache[sourceTree] = sourceTree;
                return sourceTree;
            }

            var tree = Object.Instantiate(sourceTree);
            tree.name = $"PhantomSystem_{slot.SlotId}_{playable}_{sourceTree.name}";
            treeCache[sourceTree] = tree;
            var children = tree.children;
            for (var index = 0; index < children.Length; index++)
            {
                children[index].motion = convertedMotions[index];
            }
            tree.children = children;
            context.AssetSaver.SaveAsset(tree);
            return tree;
        }

        private AnimationClip ConvertClip(AnimationClip source)
        {
            if (source == null)
            {
                return null;
            }

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
            converted.name = $"PhantomSystem_{slot.SlotId}_{playable}_{clip.name}";
            context.AssetSaver.SaveAsset(converted);
            clipCache[source] = converted;
            clipCache[clip] = converted;

            if (result != null && result.MissingBones.Count > 0)
            {
                report.Warning(
                    $"Slot '{slot.SlotId}' {playable} clip '{clip.name}' could not bake "
                    + $"{result.MissingBones.Count} humanoid bone(s): "
                    + string.Join(", ", result.MissingBones),
                    clip);
            }

            if (result != null && result.SkippedAnimatorBindings.Count > 0)
            {
                report.Warning(
                    $"Slot '{slot.SlotId}' {playable} clip '{clip.name}' skipped "
                    + $"{result.SkippedAnimatorBindings.Count} unsupported Animator binding(s).",
                    clip);
            }

            if (result != null && result.RootMotionLocalized)
            {
                report.Warning(
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

            var animatorBindingCount = bindings.Count(binding => binding.type == typeof(Animator));
            if (result != null && animatorBindingCount > 0 && result.BakedBones.Count == 0)
            {
                report.Error(
                    $"Slot '{slot.SlotId}' {playable} clip '{clip.name}' contains humanoid bindings but produced no phantom bone curves.",
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
