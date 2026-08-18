using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    internal static class PhantomHumanoidCurveWriter
    {
        private const float PositionEpsilonSquared = 0.0000000001f;

        internal static AnimationClip CreateOutputClip(AnimationClip source, float sampleRate)
        {
            var output = new AnimationClip
            {
                name = $"{source.name}_PhantomGeneric",
                frameRate = sampleRate,
                legacy = source.legacy,
                wrapMode = source.wrapMode,
                localBounds = source.localBounds
            };

            var outputSettings = AnimationUtility.GetAnimationClipSettings(source);
            outputSettings.mirror = false;
            AnimationUtility.SetAnimationClipSettings(output, outputSettings);
            AnimationUtility.SetAnimationEvents(
                output,
                AnimationUtility.GetAnimationEvents(source));
            return output;
        }

        internal static void CopyNonHumanoidCurves(
            AnimationClip source,
            AnimationClip output,
            bool localizeRootMotion,
            ISet<string> animatorParameterNames)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (binding.type == typeof(Animator)
                    && PhantomAnimationBindingClassifier.Classify(
                        binding,
                        animatorParameterNames) != PhantomAnimationBindingKind.AnimatorParameter)
                {
                    continue;
                }

                if (localizeRootMotion
                    && binding.type == typeof(Transform)
                    && string.IsNullOrEmpty(binding.path)
                    && (PhantomHumanoidBindingUtility.IsRootPositionOrRotationBinding(binding)
                        || PhantomHumanoidBindingUtility.IsRootScaleBinding(binding)))
                {
                    continue;
                }

                AnimationUtility.SetEditorCurve(
                    output,
                    binding,
                    AnimationUtility.GetEditorCurve(source, binding));
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                AnimationUtility.SetObjectReferenceCurve(
                    output,
                    binding,
                    AnimationUtility.GetObjectReferenceCurve(source, binding));
            }
        }

        internal static void WritePoseCurves(
            AnimationClip output,
            PhantomHumanoidPoseBakeData poseData)
        {
            var sampling = poseData.Sampling;
            for (var trackIndex = 0; trackIndex < poseData.Tracks.Count; trackIndex++)
            {
                WriteTrack(
                    output,
                    poseData.Tracks[trackIndex],
                    sampling.Times,
                    sampling.PosesByTrack[trackIndex],
                    sampling.KeptIndicesByTrack[trackIndex]);
            }
        }

        internal static void WriteNeutralPoseRotations(
            AnimationClip output,
            GameObject humanoidRoot,
            IReadOnlyDictionary<HumanBodyBones, string> outputBonePaths,
            IReadOnlyDictionary<HumanBodyBones, string> outputBoneParentPaths)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }
            if (outputBonePaths == null || outputBonePaths.Count == 0)
            {
                throw new ArgumentException(
                    "At least one Humanoid Driver output path is required.",
                    nameof(outputBonePaths));
            }

            var boneRotations = PhantomHumanoidPoseSampler.SampleNeutralBoneRotations(
                humanoidRoot,
                outputBoneParentPaths,
                outputBonePaths.Keys);
            var rotations = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
            foreach (var pair in outputBonePaths.OrderBy(pair => (int)pair.Key))
            {
                if (string.IsNullOrEmpty(pair.Value))
                {
                    throw new InvalidOperationException(
                        $"Humanoid bone '{pair.Key}' has no Driver output path.");
                }
                if (rotations.ContainsKey(pair.Value))
                {
                    throw new InvalidOperationException(
                        $"Multiple Humanoid bones resolve to Driver output path '{pair.Value}'.");
                }
                rotations[pair.Value] = boneRotations[pair.Key];
            }
            WriteNeutralRotationCurves(output, rotations);
        }

        internal static void WriteMissingNeutralRotationCurves(
            AnimationClip output,
            ISet<HumanBodyBones> explicitlyAnimatedBones,
            ISet<HumanBodyBones> completionBones,
            IReadOnlyDictionary<HumanBodyBones, string> outputBonePaths,
            IReadOnlyDictionary<HumanBodyBones, Quaternion> neutralBoneRotations)
        {
            if (output == null
                || explicitlyAnimatedBones == null
                || explicitlyAnimatedBones.Count == 0
                || completionBones == null
                || completionBones.Count == 0
                || outputBonePaths == null
                || neutralBoneRotations == null)
            {
                return;
            }

            var rotations = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
            foreach (var bone in completionBones.OrderBy(value => (int)value))
            {
                if (explicitlyAnimatedBones.Contains(bone))
                {
                    continue;
                }
                if (!outputBonePaths.TryGetValue(bone, out var outputPath)
                    || string.IsNullOrEmpty(outputPath))
                {
                    throw new InvalidOperationException(
                        $"Humanoid bone '{bone}' has no Driver output path.");
                }
                if (!neutralBoneRotations.TryGetValue(bone, out var rotation))
                {
                    throw new InvalidOperationException(
                        $"Humanoid bone '{bone}' has no sampled neutral rotation.");
                }
                if (rotations.ContainsKey(outputPath))
                {
                    throw new InvalidOperationException(
                        $"Multiple Humanoid bones resolve to Driver output path '{outputPath}'.");
                }
                rotations[outputPath] = rotation;
            }
            WriteNeutralRotationCurves(output, rotations);
        }

        internal static void WriteNeutralRotationCurves(
            AnimationClip output,
            IReadOnlyDictionary<string, Quaternion> rotations)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }
            if (rotations == null)
            {
                throw new ArgumentNullException(nameof(rotations));
            }

            foreach (var pair in rotations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(pair.Key))
                {
                    throw new ArgumentException(
                        "Humanoid Driver output paths must not be empty.",
                        nameof(rotations));
                }

                var rotation = PhantomAdaptivePoseSampler.NormalizeQuaternion(pair.Value);
                output.SetCurve(
                    pair.Key,
                    typeof(Transform),
                    "m_LocalRotation.x",
                    PhantomAnimatorClipUtility.Constant(
                        PhantomAnimatorClipUtility.FrameDuration,
                        rotation.x));
                output.SetCurve(
                    pair.Key,
                    typeof(Transform),
                    "m_LocalRotation.y",
                    PhantomAnimatorClipUtility.Constant(
                        PhantomAnimatorClipUtility.FrameDuration,
                        rotation.y));
                output.SetCurve(
                    pair.Key,
                    typeof(Transform),
                    "m_LocalRotation.z",
                    PhantomAnimatorClipUtility.Constant(
                        PhantomAnimatorClipUtility.FrameDuration,
                        rotation.z));
                output.SetCurve(
                    pair.Key,
                    typeof(Transform),
                    "m_LocalRotation.w",
                    PhantomAnimatorClipUtility.Constant(
                        PhantomAnimatorClipUtility.FrameDuration,
                        rotation.w));
            }
            output.EnsureQuaternionContinuity();
        }

        private static void WriteTrack(
            AnimationClip output,
            PhantomHumanoidBoneTrack track,
            IReadOnlyList<float> times,
            IReadOnlyList<PhantomPose> poses,
            IReadOnlyList<int> keptIndices)
        {
            var rotations = new Quaternion[keptIndices.Count];
            for (var index = 0; index < keptIndices.Count; index++)
            {
                var rotation = poses[keptIndices[index]].Rotation;
                if (index > 0)
                {
                    rotation = PhantomAdaptivePoseSampler.MatchQuaternionHemisphere(
                        rotations[index - 1],
                        rotation);
                }
                rotations[index] = rotation;
            }

            SetCurve(
                output,
                track,
                "m_LocalRotation.x",
                times,
                keptIndices,
                index => rotations[index].x);
            SetCurve(
                output,
                track,
                "m_LocalRotation.y",
                times,
                keptIndices,
                index => rotations[index].y);
            SetCurve(
                output,
                track,
                "m_LocalRotation.z",
                times,
                keptIndices,
                index => rotations[index].z);
            SetCurve(
                output,
                track,
                "m_LocalRotation.w",
                times,
                keptIndices,
                index => rotations[index].w);

            if (!track.ForcePosition
                && !poses.Any(pose =>
                    (pose.Position - track.BindPosition).sqrMagnitude > PositionEpsilonSquared))
            {
                return;
            }

            SetCurve(
                output,
                track,
                "m_LocalPosition.x",
                times,
                keptIndices,
                index => poses[keptIndices[index]].Position.x);
            SetCurve(
                output,
                track,
                "m_LocalPosition.y",
                times,
                keptIndices,
                index => poses[keptIndices[index]].Position.y);
            SetCurve(
                output,
                track,
                "m_LocalPosition.z",
                times,
                keptIndices,
                index => poses[keptIndices[index]].Position.z);
        }

        private static void SetCurve(
            AnimationClip output,
            PhantomHumanoidBoneTrack track,
            string propertyName,
            IReadOnlyList<float> times,
            IReadOnlyList<int> keptIndices,
            Func<int, float> getValue)
        {
            var keys = new Keyframe[keptIndices.Count];
            for (var index = 0; index < keys.Length; index++)
            {
                keys[index] = new Keyframe(times[keptIndices[index]], getValue(index));
            }

            var curve = new AnimationCurve(keys);
            for (var index = 0; index < keys.Length; index++)
            {
                AnimationUtility.SetKeyLeftTangentMode(
                    curve,
                    index,
                    AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(
                    curve,
                    index,
                    index + 1 < keys.Length
                    && PhantomAdaptivePoseSampler.IsConstantSegment(
                        keys[index].time,
                        keys[index + 1].time,
                        track.ConstantIntervals)
                        ? AnimationUtility.TangentMode.Constant
                        : AnimationUtility.TangentMode.Linear);
            }

            AnimationUtility.SetEditorCurve(
                output,
                EditorCurveBinding.FloatCurve(
                    track.Path,
                    typeof(Transform),
                    propertyName),
                curve);
        }
    }
}
