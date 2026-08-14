using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem.Editor
{
    public static class AnimationBindingDiagnostics
    {
        public static void InspectFinalAvatar(BuildContext context, PhantomBuildState state)
        {
            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null || state?.System?.RuntimeRoot == null)
            {
                return;
            }

            var controllers = CollectControllers(descriptor);
            ValidateConvertedActionLayers(state, controllers);
            ValidateFxPlayableMask(
                context.AvatarRootTransform,
                descriptor,
                state,
                controllers);
            var phantomRootPath = TransformPathUtility.GetRelativePath(
                state.System.RuntimeRoot.transform,
                context.AvatarRootTransform);
            if (string.IsNullOrEmpty(phantomRootPath))
            {
                return;
            }

            var prohibitedRootPaths = new HashSet<string>(StringComparer.Ordinal)
            {
                string.Empty,
                phantomRootPath
            };
            var visibleHumanoidBonePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in state.System.Slots)
            {
                AddPath(prohibitedRootPaths, slot.SlotRoot, context.AvatarRootTransform);
                AddPath(prohibitedRootPaths, slot.CloneRoot, context.AvatarRootTransform);
                if (slot.AnimationDriverRoot != null)
                {
                    AddPath(
                        prohibitedRootPaths,
                        slot.AnimationDriverRoot.gameObject,
                        context.AvatarRootTransform);
                }
                foreach (var bone in slot.CloneBones.Values)
                {
                    if (bone == null)
                    {
                        continue;
                    }

                    var path = TransformPathUtility.GetRelativePath(
                        bone,
                        context.AvatarRootTransform);
                    if (path != null)
                    {
                        visibleHumanoidBonePaths.Add(path);
                    }
                }
            }

            var reported = new HashSet<string>();
            foreach (var pair in controllers)
            {
                var animatorParameterNames = new HashSet<string>(
                    pair.Value.parameters
                        .Where(parameter => parameter.type == AnimatorControllerParameterType.Float)
                        .Select(parameter => parameter.name),
                    StringComparer.Ordinal);
                foreach (var clip in pair.Value.animationClips.Where(clip => clip != null).Distinct())
                {
                    InspectClip(
                        context,
                        state,
                        pair.Key,
                        clip,
                        phantomRootPath,
                        prohibitedRootPaths,
                        visibleHumanoidBonePaths,
                        animatorParameterNames,
                        reported);
                }
            }
        }

        private static Dictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController>
            CollectControllers(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController>();
            AddLayers(descriptor.baseAnimationLayers, result);
            AddLayers(descriptor.specialAnimationLayers, result);
            return result;
        }

        private static void AddLayers(
            VRCAvatarDescriptor.CustomAnimLayer[] layers,
            IDictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController> result)
        {
            foreach (var layer in layers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
            {
                if ((layer.type == VRCAvatarDescriptor.AnimLayerType.FX
                     || layer.type == VRCAvatarDescriptor.AnimLayerType.Gesture)
                    && GetBaseController(layer.animatorController) is AnimatorController controller)
                {
                    result[layer.type] = controller;
                }
            }
        }

        private static AnimatorController GetBaseController(RuntimeAnimatorController runtimeController)
        {
            var current = runtimeController;
            var visited = new HashSet<RuntimeAnimatorController>();
            while (current is AnimatorOverrideController overrideController
                   && visited.Add(current))
            {
                current = overrideController.runtimeAnimatorController;
            }
            return current as AnimatorController;
        }

        private static void InspectClip(
            BuildContext context,
            PhantomBuildState state,
            VRCAvatarDescriptor.AnimLayerType playable,
            AnimationClip clip,
            string phantomRootPath,
            ISet<string> prohibitedRootPaths,
            ISet<string> visibleHumanoidBonePaths,
            ISet<string> animatorParameterNames,
            ISet<string> reported)
        {
            // Strict PhantomSystem diagnostics are meaningful only for clips whose
            // conversion provenance we recorded. Other NDMF passes can create final
            // clips with intentionally unresolved sentinel bindings.
            if (!IsConvertedPlayableClip(context.ObjectRegistry, state, clip))
            {
                return;
            }

            foreach (var binding in AnimationUtility.GetCurveBindings(clip)
                         .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
            {
                if (IsPhantomPath(binding.path, phantomRootPath)
                    && !IsValidBindingTarget(context.AvatarRootTransform, binding))
                {
                    var key = $"invalid|{clip.GetInstanceID()}|{binding.path}|{binding.type?.FullName}|{binding.propertyName}";
                    if (reported.Add(key))
                    {
                        state.Report.Warning(
                            $"Final {playable} clip '{clip.name}' has an invalid phantom binding '{binding.path}' "
                            + $"({binding.type?.Name}.{binding.propertyName}). The missing target may have been left "
                            + "intentionally by another build tool.",
                            clip);
                    }
                }

                if (binding.type == typeof(Transform)
                    && visibleHumanoidBonePaths.Contains(binding.path ?? string.Empty)
                    && IsPositionRotationOrScale(binding.propertyName))
                {
                    var key = $"visible-bone|{clip.GetInstanceID()}|{binding.path}|{binding.propertyName}";
                    if (reported.Add(key))
                    {
                        state.Report.InternalError(
                            $"Converted {playable} clip '{clip.name}' still animates visible phantom humanoid bone "
                            + $"'{binding.path}' through '{binding.propertyName}'. Bone animation must target the "
                            + "Phantom Animation Driver skeleton.",
                            clip);
                    }
                }

                if (binding.type == typeof(Animator)
                    && PhantomAnimationBindingClassifier.Classify(
                        binding,
                        animatorParameterNames) != PhantomAnimationBindingKind.AnimatorParameter)
                {
                    var key = $"muscle|{clip.GetInstanceID()}|{binding.propertyName}";
                    if (reported.Add(key))
                    {
                        state.Report.InternalError(
                            $"Converted {playable} clip '{clip.name}' still contains an unsupported or humanoid Animator binding "
                            + $"'{binding.propertyName}'.",
                            clip);
                    }
                }

                if (binding.type == typeof(Transform)
                    && prohibitedRootPaths.Contains(binding.path ?? string.Empty)
                    && IsPositionRotationOrScale(binding.propertyName))
                {
                    var key = $"root|{clip.GetInstanceID()}|{binding.path}|{binding.propertyName}";
                    if (reported.Add(key))
                    {
                        state.Report.InternalError(
                            $"Converted {playable} clip '{clip.name}' animates protected phantom root "
                            + $"'{binding.path}' through '{binding.propertyName}'. Root Motion must target Hips only.",
                            clip);
                    }
                }
            }
        }

        internal static bool IsConvertedPlayableClip(
            IObjectRegistry objectRegistry,
            PhantomBuildState state,
            AnimationClip clip)
        {
            if (objectRegistry == null || clip == null || state?.System?.Slots == null)
            {
                return false;
            }

            var reference = objectRegistry.GetReference(clip, false);
            return reference != null
                   && state.System.Slots.Any(slot =>
                       slot.ConvertedClipReferences.ContainsKey(reference));
        }

        private static void ValidateConvertedActionLayers(
            PhantomBuildState state,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController> controllers)
        {
            var expected = state.System.Slots
                .SelectMany(slot => slot.ConvertedActionLayers)
                .ToArray();
            if (expected.Length == 0)
            {
                return;
            }

            if (!controllers.TryGetValue(
                    VRCAvatarDescriptor.AnimLayerType.Gesture,
                    out var gestureController))
            {
                state.Report.InternalError(
                    "Final Gesture controller is missing while Converted Action layers are present.");
                return;
            }

            var layers = gestureController.layers;
            var indices = layers
                .Select((layer, index) => new { layer.name, Index = index })
                .GroupBy(value => value.name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            foreach (var actionLayer in expected)
            {
                if (!indices.TryGetValue(actionLayer.LayerName, out var matches)
                    || matches.Length != 1)
                {
                    state.Report.InternalError(
                        $"Final Gesture controller does not contain exactly one Converted Action layer "
                        + $"named '{actionLayer.LayerName}'.",
                        gestureController);
                    continue;
                }

                var index = matches[0].Index;
                if (index == 0)
                {
                    state.Report.InternalError(
                        $"Converted Action layer '{actionLayer.LayerName}' became Gesture layer 0.",
                        gestureController);
                }
                if (!Mathf.Approximately(layers[index].defaultWeight, 0f))
                {
                    state.Report.InternalError(
                        $"Converted Action layer '{actionLayer.LayerName}' has final default weight "
                        + $"{layers[index].defaultWeight:0.###} instead of 0.",
                        gestureController);
                }
            }
        }

        private static void ValidateFxPlayableMask(
            Transform avatarRoot,
            VRCAvatarDescriptor descriptor,
            PhantomBuildState state,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController> controllers)
        {
            var driverRoots = state.System.Slots
                .Where(slot => slot.AnimationDriverRoot != null
                               && PhantomFxPlayableMaskFinalizer.RequiresAnimationDriverIsolation(slot))
                .Select(slot => slot.AnimationDriverRoot)
                .ToArray();
            if (driverRoots.Length == 0)
            {
                return;
            }

            if (!controllers.TryGetValue(
                    VRCAvatarDescriptor.AnimLayerType.FX,
                    out var fxController)
                || fxController.layers.Length == 0)
            {
                state.Report.InternalError(
                    "Final FX controller or its first layer is missing while Animation Driver isolation is required.");
                return;
            }

            var mask = fxController.layers[0].avatarMask;
            if (mask == null)
            {
                state.Report.InternalError(
                    "Final FX layer 0 has no Avatar Mask while Animation Driver isolation is required.",
                    fxController);
                return;
            }

            foreach (var driverRoot in driverRoots)
            {
                var path = TransformPathUtility.GetRelativePath(driverRoot, avatarRoot);
                if (path == null
                    || !PhantomFxPlayableMaskFinalizer.IsTransformExcluded(mask, path))
                {
                    state.Report.InternalError(
                        $"Final FX layer 0 Mask does not exclude Animation Driver "
                        + $"'{path ?? driverRoot.name}'.",
                        mask);
                }
            }

            var descriptorMask = FindDescriptorMask(
                descriptor,
                VRCAvatarDescriptor.AnimLayerType.FX);
            if (descriptorMask != mask)
            {
                state.Report.InternalError(
                    "The final Avatar Descriptor FX Mask does not match the final FX layer 0 Mask.",
                    descriptor);
            }
        }

        private static AvatarMask FindDescriptorMask(
            VRCAvatarDescriptor descriptor,
            VRCAvatarDescriptor.AnimLayerType type)
        {
            foreach (var layer in (descriptor.baseAnimationLayers
                         ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                     .Concat(descriptor.specialAnimationLayers
                         ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()))
            {
                if (layer.type == type)
                {
                    return layer.mask;
                }
            }
            return null;
        }

        private static bool IsPositionRotationOrScale(string propertyName)
        {
            return propertyName != null
                   && (propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal)
                       || propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal)
                       || propertyName.StartsWith("localEulerAnglesRaw.", StringComparison.Ordinal)
                       || propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal));
        }

        private static void AddPath(ISet<string> paths, GameObject value, Transform avatarRoot)
        {
            if (value == null)
            {
                return;
            }
            var path = TransformPathUtility.GetRelativePath(value.transform, avatarRoot);
            if (path != null)
            {
                paths.Add(path);
            }
        }

        private static bool IsPhantomPath(string path, string phantomRootPath)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path == phantomRootPath
                   || path.StartsWith(phantomRootPath + "/", StringComparison.Ordinal);
        }

        private static bool IsValidBindingTarget(Transform avatarRoot, EditorCurveBinding binding)
        {
            var target = string.IsNullOrEmpty(binding.path) ? avatarRoot : avatarRoot.Find(binding.path);
            if (target == null)
            {
                return false;
            }

            if (binding.type == null || binding.type == typeof(GameObject) || binding.type == typeof(Transform))
            {
                return true;
            }

            return !typeof(Component).IsAssignableFrom(binding.type) || target.GetComponent(binding.type) != null;
        }
    }
}
