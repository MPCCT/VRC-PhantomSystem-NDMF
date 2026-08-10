using System;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using NdmfObjectReference = nadena.dev.ndmf.ObjectReference;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>
    /// Bridges NDMF virtual clips to Unity's physical AnimationClip-only Humanoid sampling API.
    /// </summary>
    internal static class PhantomVirtualClipAdapter
    {
        public static AnimationClip Materialize(
            VirtualClip source,
            Func<string, string> pathMapper)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            pathMapper ??= path => path;
            var clip = new AnimationClip
            {
                name = source.Name,
                frameRate = source.FrameRate,
                legacy = source.Legacy,
                localBounds = source.LocalBounds,
                wrapMode = source.WrapMode
            };

            var settings = source.Settings;
            settings.additiveReferencePoseClip = null;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            foreach (var binding in source.GetFloatCurveBindings().ToArray())
            {
                var mapped = binding;
                mapped.path = pathMapper(binding.path ?? string.Empty);
                AnimationUtility.SetEditorCurve(clip, mapped, source.GetFloatCurve(binding));
            }

            foreach (var binding in source.GetObjectCurveBindings().ToArray())
            {
                var mapped = binding;
                mapped.path = pathMapper(binding.path ?? string.Empty);
                AnimationUtility.SetObjectReferenceCurve(
                    clip,
                    mapped,
                    source.GetObjectCurve(binding));
            }

            var serialized = new SerializedObject(clip);
            var highQuality = serialized.FindProperty("m_UseHighQualityCurve");
            if (highQuality != null)
            {
                highQuality.boolValue = source.UseHighQualityCurves;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return clip;
        }

        public static PhantomVirtualClipImport ImportConverted(
            BuildContext context,
            AnimationClip converted,
            VirtualClip source,
            Func<string, string> pathMapper)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (converted == null)
            {
                throw new ArgumentNullException(nameof(converted));
            }

            var registry = (IObjectRegistry)context.ObjectRegistry;
            NdmfObjectReference reference;
            VirtualClip virtualClip;
            using (new ObjectRegistryScope(registry))
            {
                reference = registry.GetReference(converted);
                virtualClip = context.Extension<AnimatorServicesContext>()
                    .ControllerContext
                    .Clone(converted);
            }
            if (virtualClip == null)
            {
                throw new InvalidOperationException(
                    $"NDMF failed to virtualize converted clip '{converted.name}'.");
            }

            virtualClip.AdditiveReferencePoseClip = source?.AdditiveReferencePoseClip;
            if (source != null)
            {
                virtualClip.AdditiveReferencePoseTime = source.AdditiveReferencePoseTime;
                virtualClip.UseHighQualityCurves = source.UseHighQualityCurves;
            }
            if (pathMapper != null)
            {
                virtualClip.EditPaths(pathMapper);
            }

            return new PhantomVirtualClipImport(virtualClip, reference);
        }
    }

    internal readonly struct PhantomVirtualClipImport
    {
        public readonly VirtualClip Clip;
        public readonly NdmfObjectReference Reference;

        public PhantomVirtualClipImport(VirtualClip clip, NdmfObjectReference reference)
        {
            Clip = clip;
            Reference = reference;
        }
    }

    internal sealed class PhantomVirtualPathMapper
    {
        private readonly string cloneRootPath;

        public PhantomVirtualPathMapper(Transform avatarRoot, GameObject cloneRoot)
        {
            cloneRootPath = cloneRoot == null
                ? null
                : TransformPathUtility.GetRelativePath(cloneRoot.transform, avatarRoot);
        }

        public string CloneRootPath => cloneRootPath;

        public string ToCloneRelative(string path)
        {
            path ??= string.Empty;
            if (string.IsNullOrEmpty(cloneRootPath))
            {
                return path;
            }
            if (string.Equals(path, cloneRootPath, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var prefix = cloneRootPath + "/";
            return path.StartsWith(prefix, StringComparison.Ordinal)
                ? path.Substring(prefix.Length)
                : path;
        }

        public string ToAvatarRelative(string path)
        {
            path ??= string.Empty;
            if (string.IsNullOrEmpty(cloneRootPath))
            {
                return path;
            }
            return string.IsNullOrEmpty(path)
                ? cloneRootPath
                : cloneRootPath + "/" + path;
        }
    }
}
