using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>
    /// Removes source FX curves that conflict with generated rig controls, while
    /// preserving extra bones, visible-bone scale, and non-transform animation.
    /// </summary>
    internal static class PhantomFxBoneAnimationFilter
    {
        internal const string DummyPath = "$PhantomSystemRemovedFxBoneAnimation$";
        internal const string DummyProperty = "m_IsActive";

        [Flags]
        internal enum TransformChannels
        {
            Position = 1,
            Rotation = 2,
            Scale = 4,
            Pose = Position | Rotation,
            All = Pose | Scale
        }

        internal static Dictionary<string, TransformChannels> CollectTransformChannels(
            PhantomSlotBuildState slot)
        {
            var result = new Dictionary<string, TransformChannels>(StringComparer.Ordinal);
            if (slot?.CloneRoot == null)
            {
                return result;
            }

            // Only generated constraints own visible transforms. In particular, being
            // a skinning bone or an intermediate parent does not imply ownership.
            AddTransform(result, slot.CloneArmature, slot, TransformChannels.Pose);
            foreach (var pair in slot.CloneBoneConstraintTypes)
            {
                if (!slot.CloneBones.TryGetValue(pair.Key, out var bone))
                {
                    continue;
                }

                var channels = pair.Value == typeof(VRCRotationConstraint)
                    ? TransformChannels.Rotation
                    : TransformChannels.Pose;
                AddTransform(result, bone, slot, channels);
            }

            // The generated driver skeleton is private to converted Gesture/Action.
            AddTransform(result, slot.AnimationDriverRoot, slot, TransformChannels.All);
            foreach (var bone in slot.AnimationDriverBones.Values)
            {
                AddTransform(result, bone, slot, TransformChannels.All);
            }

            return result;
        }

        internal static bool ShouldRemove(
            EditorCurveBinding binding,
            IReadOnlyDictionary<string, TransformChannels> transformChannels,
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
            if (string.IsNullOrEmpty(path))
            {
                // Source root motion/scale must never move or resize the whole clone.
                return true;
            }

            if (transformChannels == null
                || !transformChannels.TryGetValue(path, out var channels))
            {
                return false;
            }

            var channel = binding.propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal)
                ? TransformChannels.Position
                : binding.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal)
                    ? TransformChannels.Scale
                    : TransformChannels.Rotation;
            return (channels & channel) != 0;
        }

        internal static PhantomFxBoneAnimationFilterResult Filter(
            AnimationClip clip,
            IReadOnlyDictionary<string, TransformChannels> transformChannels,
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
                if (!ShouldRemove(binding, transformChannels, animatorParameterNames))
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

        private static void AddTransform(
            IDictionary<string, TransformChannels> paths,
            Transform transform,
            PhantomSlotBuildState slot,
            TransformChannels channels)
        {
            if (transform == null)
            {
                return;
            }

            var path = TransformPathUtility.GetRelativePath(transform, slot.CloneRoot.transform);
            if (!string.IsNullOrEmpty(path))
            {
                paths.TryGetValue(path, out var existing);
                paths[path] = existing | channels;
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
