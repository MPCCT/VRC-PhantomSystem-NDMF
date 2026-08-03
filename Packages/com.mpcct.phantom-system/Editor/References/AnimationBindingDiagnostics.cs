using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem.Editor
{
    public static class AnimationBindingDiagnostics
    {
        public static void InspectFinalAvatar(BuildContext context, PhantomBuildState state)
        {
            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null
                || descriptor.baseAnimationLayers == null
                || descriptor.baseAnimationLayers.Length <= 4)
            {
                return;
            }

            var controller = descriptor.baseAnimationLayers[4].animatorController;
            if (controller == null)
            {
                return;
            }

            if (state?.System?.RuntimeRoot == null)
            {
                return;
            }

            var phantomRootPath = TransformPathUtility.GetRelativePath(
                state.System.RuntimeRoot.transform,
                context.AvatarRootTransform);
            if (string.IsNullOrEmpty(phantomRootPath))
            {
                return;
            }

            var reported = new HashSet<string>();
            foreach (var clip in controller.animationClips.Where(clip => clip != null).Distinct())
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip)
                             .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                {
                    if (!IsPhantomPath(binding.path, phantomRootPath)
                        || IsValidBindingTarget(context.AvatarRootTransform, binding))
                    {
                        continue;
                    }

                    var key = $"{clip.GetInstanceID()}|{binding.path}|{binding.type?.FullName}|{binding.propertyName}";
                    if (!reported.Add(key))
                    {
                        continue;
                    }

                    state.Report.Warning(
                        $"Final FX clip '{clip.name}' has an invalid phantom binding '{binding.path}' "
                        + $"({binding.type?.Name}.{binding.propertyName}). AAO may remove the affected animation as meaningless.",
                        clip);
                }
            }
        }

        private static bool IsPhantomPath(string path, string phantomRootPath)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path == phantomRootPath
                   || path.StartsWith(phantomRootPath + "/", System.StringComparison.Ordinal);
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

