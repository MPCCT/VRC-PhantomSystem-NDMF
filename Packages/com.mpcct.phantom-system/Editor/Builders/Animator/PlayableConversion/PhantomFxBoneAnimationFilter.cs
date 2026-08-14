using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>
    /// Removes pose animation from retained source FX clips without touching their
    /// parameters, blend shapes, materials, object references, or component curves.
    /// </summary>
    internal static class PhantomFxBoneAnimationFilter
    {
        internal const string DummyPath = "$PhantomSystemRemovedFxBoneAnimation$";
        internal const string DummyProperty = "m_IsActive";

        internal static HashSet<string> CollectBonePaths(PhantomSlotBuildState slot)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (slot?.CloneRoot == null)
            {
                return result;
            }

            foreach (var path in slot.CloneToAnimationDriverPaths.Keys)
            {
                AddPath(result, path);
            }
            foreach (var path in slot.CloneToAnimationDriverPaths.Values)
            {
                AddPath(result, path);
            }

            foreach (var bone in slot.CloneBones.Values.Where(value => value != null))
            {
                AddBoneAndParents(
                    result,
                    bone,
                    slot.CloneArmature,
                    slot.CloneRoot.transform);
            }
            foreach (var bone in slot.AnimationDriverBones.Values.Where(value => value != null))
            {
                AddBoneAndParents(
                    result,
                    bone,
                    slot.AnimationDriverRoot,
                    slot.CloneRoot.transform);
            }

            foreach (var renderer in slot.CloneRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                AddBoneAndParents(
                    result,
                    renderer.rootBone,
                    slot.CloneArmature,
                    slot.CloneRoot.transform);
                foreach (var bone in renderer.bones ?? Array.Empty<Transform>())
                {
                    AddBoneAndParents(
                        result,
                        bone,
                        slot.CloneArmature,
                        slot.CloneRoot.transform);
                }
            }

            return result;
        }

        internal static bool ShouldRemove(
            EditorCurveBinding binding,
            ISet<string> bonePaths,
            ISet<string> animatorParameterNames)
        {
            if (binding.type == typeof(Animator))
            {
                return PhantomAnimationBindingClassifier.Classify(
                           binding,
                           animatorParameterNames)
                       != PhantomAnimationBindingKind.AnimatorParameter;
            }

            if (binding.type != typeof(Transform)
                || !IsPositionRotationOrScale(binding.propertyName))
            {
                return false;
            }

            var path = binding.path ?? string.Empty;
            return string.IsNullOrEmpty(path)
                   || bonePaths != null && bonePaths.Contains(path);
        }

        internal static PhantomFxBoneAnimationFilterResult Filter(
            AnimationClip clip,
            ISet<string> bonePaths,
            ISet<string> animatorParameterNames)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            var originalLength = clip.length;
            var removedAnimatorCurves = 0;
            var removedTransformCurves = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!ShouldRemove(binding, bonePaths, animatorParameterNames))
                {
                    continue;
                }

                AnimationUtility.SetEditorCurve(clip, binding, null);
                if (binding.type == typeof(Animator))
                {
                    removedAnimatorCurves++;
                }
                else
                {
                    removedTransformCurves++;
                }
            }

            if (removedAnimatorCurves == 0 && removedTransformCurves == 0)
            {
                return default;
            }

            var dummyBinding = EditorCurveBinding.FloatCurve(
                DummyPath,
                typeof(GameObject),
                DummyProperty);
            AnimationUtility.SetEditorCurve(
                clip,
                dummyBinding,
                AnimationCurve.Constant(originalLength, originalLength, 1f));

            return new PhantomFxBoneAnimationFilterResult(
                removedAnimatorCurves,
                removedTransformCurves,
                originalLength);
        }

        internal static bool IsDummyBinding(EditorCurveBinding binding)
        {
            return binding.type == typeof(GameObject)
                   && IsDummyPath(binding.path)
                   && string.Equals(binding.propertyName, DummyProperty, StringComparison.Ordinal);
        }

        internal static bool IsDummyPath(string path)
        {
            return string.Equals(path, DummyPath, StringComparison.Ordinal);
        }

        internal static bool IsPositionRotationOrScale(string propertyName)
        {
            return !string.IsNullOrEmpty(propertyName)
                   && (propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal)
                       || propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal)
                       || propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal)
                       || propertyName.StartsWith("localEulerAngles", StringComparison.Ordinal)
                       || propertyName.StartsWith("m_LocalEulerAngles", StringComparison.Ordinal));
        }

        private static void AddBoneAndParents(
            ISet<string> paths,
            Transform bone,
            Transform stopAfter,
            Transform cloneRoot)
        {
            if (bone == null || cloneRoot == null)
            {
                return;
            }

            for (var current = bone;
                 current != null && current != cloneRoot;
                 current = current.parent)
            {
                var path = TransformPathUtility.GetRelativePath(current, cloneRoot);
                if (path == null)
                {
                    break;
                }

                AddPath(paths, path);
                if (current == stopAfter)
                {
                    break;
                }
            }
        }

        private static void AddPath(ISet<string> paths, string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }
    }

    internal readonly struct PhantomFxBoneAnimationFilterResult
    {
        internal int RemovedAnimatorCurves { get; }
        internal int RemovedTransformCurves { get; }
        internal float OriginalLength { get; }
        internal bool Changed => RemovedAnimatorCurves > 0 || RemovedTransformCurves > 0;

        internal PhantomFxBoneAnimationFilterResult(
            int removedAnimatorCurves,
            int removedTransformCurves,
            float originalLength)
        {
            RemovedAnimatorCurves = removedAnimatorCurves;
            RemovedTransformCurves = removedTransformCurves;
            OriginalLength = originalLength;
        }
    }
}
