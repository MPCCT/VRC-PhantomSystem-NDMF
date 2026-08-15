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
            var sourceFxBonePaths = new HashSet<string>(StringComparer.Ordinal);
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

                var cloneRootPath = TransformPathUtility.GetRelativePath(
                    slot.CloneRoot?.transform,
                    context.AvatarRootTransform);
                if (cloneRootPath != null)
                {
                    foreach (var relativePath in PhantomFxBoneAnimationFilter.CollectBonePaths(slot))
                    {
                        sourceFxBonePaths.Add(string.IsNullOrEmpty(relativePath)
                            ? cloneRootPath
                            : $"{cloneRootPath}/{relativePath}");
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
                        sourceFxBonePaths,
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
            ISet<string> sourceFxBonePaths,
            ISet<string> animatorParameterNames,
            ISet<string> reported)
        {
            // Strict PhantomSystem diagnostics are meaningful only for clips whose
            // conversion provenance we recorded. Other NDMF passes can create final
            // clips with intentionally unresolved sentinel bindings.
            if (!TryGetConvertedPlayableClipMetadata(
                    context.ObjectRegistry,
                    state,
                    clip,
                    out var metadata))
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


                if (string.Equals(
                        metadata.Playable,
                        VRCAvatarDescriptor.AnimLayerType.FX.ToString(),
                        StringComparison.Ordinal)
                    && binding.type == typeof(Transform)
                    && sourceFxBonePaths.Contains(binding.path ?? string.Empty)
                    && IsPositionRotationOrScale(binding.propertyName))
                {
                    var key = $"source-fx-bone|{clip.GetInstanceID()}|{binding.path}|{binding.propertyName}";
                    if (reported.Add(key))
                    {
                        state.Report.InternalError(
                            $"Converted Source FX clip '{clip.name}' still animates phantom skeleton transform "
                            + $"'{binding.path}' through '{binding.propertyName}'. Source FX bone animation "
                            + "must be removed before controller merging.",
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
            return TryGetConvertedPlayableClipMetadata(
                objectRegistry,
                state,
                clip,
                out _);
        }

        private static bool TryGetConvertedPlayableClipMetadata(
            IObjectRegistry objectRegistry,
            PhantomBuildState state,
            AnimationClip clip,
            out PhantomConvertedClipMetadata metadata)
        {
            metadata = null;
            if (objectRegistry == null || clip == null || state?.System?.Slots == null)
            {
                return false;
            }

            var reference = objectRegistry.GetReference(clip, false);
            if (reference == null)
            {
                return false;
            }

            foreach (var slot in state.System.Slots)
            {
                if (slot.ConvertedClipReferences.TryGetValue(reference, out metadata))
                {
                    return true;
                }
            }

            metadata = null;
            return false;
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
                    VRCAvatarDescriptor.AnimLayerType.FX,
                    out var fxController))
            {
                state.Report.InternalError(
                    "Final FX controller is missing while Converted Action layers are present.");
                return;
            }

            var layers = fxController.layers;
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
                        $"Final FX controller does not contain exactly one Converted Action layer "
                        + $"named '{actionLayer.LayerName}'.",
                        fxController);
                    continue;
                }

                var index = matches[0].Index;
                if (index == 0)
                {
                    state.Report.InternalError(
                        $"Converted Action layer '{actionLayer.LayerName}' became FX layer 0.",
                        fxController);
                }
                if (!Mathf.Approximately(layers[index].defaultWeight, 0f))
                {
                    state.Report.InternalError(
                        $"Converted Action layer '{actionLayer.LayerName}' has final default weight "
                        + $"{layers[index].defaultWeight:0.###} instead of 0.",
                        fxController);
                }
            }
        }

        private static bool IsPositionRotationOrScale(string propertyName)
        {
            return PhantomFxBoneAnimationFilter.IsPositionRotationOrScale(propertyName);
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
