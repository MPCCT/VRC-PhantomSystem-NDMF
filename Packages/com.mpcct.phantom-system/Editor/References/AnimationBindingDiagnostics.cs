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
            ISet<string> reported)
        {
            // Strict PhantomSystem diagnostics are meaningful only for clips whose
            // conversion provenance we recorded. Other NDMF passes can create final
            // clips with intentionally unresolved sentinel bindings.
            if (!IsConvertedPlayableClip(state, clip))
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

                if (binding.type == typeof(Animator))
                {
                    var key = $"muscle|{clip.GetInstanceID()}|{binding.propertyName}";
                    if (reported.Add(key))
                    {
                        state.Report.InternalError(
                            $"Converted {playable} clip '{clip.name}' still contains humanoid Animator binding "
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

        internal static bool IsConvertedPlayableClip(PhantomBuildState state, AnimationClip clip)
        {
            return state?.System?.Slots != null
                   && state.System.Slots.Any(slot => slot.ConvertedClips.ContainsKey(clip));
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
