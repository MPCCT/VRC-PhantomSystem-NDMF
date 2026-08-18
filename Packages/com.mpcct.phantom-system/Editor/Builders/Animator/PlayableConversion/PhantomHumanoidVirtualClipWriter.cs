using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>
    /// Reconstructs a converted Humanoid motion directly as a VirtualClip when pose sampling data
    /// is already cached. A curve-free physical clip is retained only as the ObjectRegistry identity.
    /// </summary>
    internal static class PhantomHumanoidVirtualClipWriter
    {
        internal static PhantomVirtualClipImport WriteCached(
            BuildContext context,
            AnimationClip currentSource,
            VirtualClip sourceMotion,
            PhantomHumanoidClipBakePreparation preparation,
            IReadOnlyDictionary<string, string> cloneToAnimationDriverPaths,
            Func<string, string> toAvatarRelative,
            string outputName)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (currentSource == null)
            {
                throw new ArgumentNullException(nameof(currentSource));
            }
            if (sourceMotion == null)
            {
                throw new ArgumentNullException(nameof(sourceMotion));
            }
            if (preparation == null || !preparation.IsCacheHit)
            {
                throw new ArgumentException(
                    "A prepared Humanoid cache hit is required.",
                    nameof(preparation));
            }

            cloneToAnimationDriverPaths ??=
                new Dictionary<string, string>(StringComparer.Ordinal);
            toAvatarRelative ??= path => path;

            AnimationClip identityClip = null;
            try
            {
                identityClip = PhantomHumanoidCurveWriter.CreateOutputClip(
                    currentSource,
                    preparation.SampleRate);
                identityClip.name = outputName;

                var imported = PhantomVirtualClipAdapter.ImportConverted(
                    context,
                    identityClip,
                    sourceMotion,
                    null);
                var output = imported.Clip;
                output.Name = outputName;

                CopyCurrentNonHumanoidCurves(
                    currentSource,
                    output,
                    preparation.Options,
                    cloneToAnimationDriverPaths,
                    toAvatarRelative);
                PhantomHumanoidCurveWriter.WritePoseCurves(
                    output,
                    preparation.CachedPoseData,
                    toAvatarRelative);
                PhantomHumanoidCurveWriter.WriteMissingNeutralRotationCurves(
                    output,
                    preparation.Analysis.ExplicitlyAnimatedBones,
                    preparation.Options.NeutralRotationCompletionBones,
                    preparation.Options.OutputBonePaths,
                    preparation.Options.NeutralBoneRotations,
                    toAvatarRelative);
                return imported;
            }
            finally
            {
                if (identityClip != null)
                {
                    Object.DestroyImmediate(identityClip);
                }
            }
        }

        private static void CopyCurrentNonHumanoidCurves(
            AnimationClip source,
            VirtualClip output,
            PhantomHumanoidClipBakeOptions options,
            IReadOnlyDictionary<string, string> cloneToAnimationDriverPaths,
            Func<string, string> toAvatarRelative)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (!ShouldCopyFloatBinding(binding, options))
                {
                    continue;
                }

                output.SetFloatCurve(
                    MapBinding(binding, cloneToAnimationDriverPaths, toAvatarRelative),
                    AnimationUtility.GetEditorCurve(source, binding));
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                output.SetObjectCurve(
                    MapBinding(binding, cloneToAnimationDriverPaths, toAvatarRelative),
                    AnimationUtility.GetObjectReferenceCurve(source, binding));
            }
        }

        private static bool ShouldCopyFloatBinding(
            EditorCurveBinding binding,
            PhantomHumanoidClipBakeOptions options)
        {
            if (binding.type == typeof(Animator)
                && PhantomAnimationBindingClassifier.Classify(
                    binding,
                    options.AnimatorParameterNames) != PhantomAnimationBindingKind.AnimatorParameter)
            {
                return false;
            }

            return !options.LocalizeRootMotionToHips
                   || binding.type != typeof(Transform)
                   || !string.IsNullOrEmpty(binding.path)
                   || (!PhantomHumanoidBindingUtility.IsRootPositionOrRotationBinding(binding)
                       && !PhantomHumanoidBindingUtility.IsRootScaleBinding(binding));
        }

        private static EditorCurveBinding MapBinding(
            EditorCurveBinding binding,
            IReadOnlyDictionary<string, string> cloneToAnimationDriverPaths,
            Func<string, string> toAvatarRelative)
        {
            var mapped = binding;
            var path = binding.path ?? string.Empty;
            if (binding.type == typeof(Transform)
                && cloneToAnimationDriverPaths.TryGetValue(path, out var driverPath))
            {
                path = driverPath;
            }

            mapped.path = binding.type == typeof(Animator) && string.IsNullOrEmpty(path)
                ? string.Empty
                : toAvatarRelative(path);
            return mapped;
        }
    }
}
