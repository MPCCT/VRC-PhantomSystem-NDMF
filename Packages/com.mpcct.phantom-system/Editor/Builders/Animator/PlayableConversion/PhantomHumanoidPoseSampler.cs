using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MPCCT.PhantomSystem.Editor
{
    internal sealed class PhantomHumanoidBoneTrack
    {
        internal HumanBodyBones Bone { get; }
        internal string Path { get; }
        internal Vector3 BindPosition { get; }
        internal bool ForcePosition { get; }
        internal IReadOnlyList<PhantomTimeInterval> ConstantIntervals { get; }

        internal PhantomHumanoidBoneTrack(
            HumanBodyBones bone,
            string path,
            Vector3 bindPosition,
            bool forcePosition,
            IReadOnlyList<PhantomTimeInterval> constantIntervals)
        {
            Bone = bone;
            Path = path;
            BindPosition = bindPosition;
            ForcePosition = forcePosition;
            ConstantIntervals = constantIntervals;
        }
    }

    internal sealed class PhantomHumanoidPoseBakeData
    {
        internal IReadOnlyList<PhantomHumanoidBoneTrack> Tracks { get; }
        internal PhantomPoseSamplingResult Sampling { get; }
        internal IReadOnlyList<HumanBodyBones> MissingBones { get; }

        internal PhantomHumanoidPoseBakeData(
            IReadOnlyList<PhantomHumanoidBoneTrack> tracks,
            PhantomPoseSamplingResult sampling,
            IReadOnlyList<HumanBodyBones> missingBones)
        {
            Tracks = tracks;
            Sampling = sampling;
            MissingBones = missingBones;
        }
    }

    internal static class PhantomHumanoidPoseSampler
    {
        internal static PhantomHumanoidPoseBakeData Sample(
            AnimationClip source,
            Avatar avatar,
            Transform sourceRoot,
            float sampleRate,
            ISet<HumanBodyBones> affectedBones,
            ISet<HumanBodyBones> forcePositionBones,
            bool localizeRootMotion,
            PhantomHumanoidClipBakeOptions options,
            float positionTolerance,
            float rotationToleranceDegrees,
            IReadOnlyList<EditorCurveBinding> relevantBindings,
            bool mirrorBindings)
        {
            GameObject sampleRoot = null;
            var startedAnimationMode = false;
            try
            {
                sampleRoot = CreateSamplingHierarchy(sourceRoot, avatar, out var sampleAnimator);
                var recorders = new List<BoneRecorder>();
                var missingBones = new List<HumanBodyBones>();
                var constantIntervalsByBone = options.SamplingMode == PhantomHumanoidSamplingMode.Adaptive
                    ? PhantomHumanoidClipAnalyzer.CollectConstantIntervals(
                        source,
                        relevantBindings,
                        mirrorBindings)
                    : new Dictionary<HumanBodyBones, List<PhantomTimeInterval>>();

                foreach (var bone in affectedBones.OrderBy(value => (int)value))
                {
                    var target = sampleAnimator.GetBoneTransform(bone);
                    if (target == null)
                    {
                        missingBones.Add(bone);
                        continue;
                    }

                    var path = options.OutputBonePaths != null
                               && options.OutputBonePaths.TryGetValue(bone, out var outputPath)
                        ? outputPath
                        : GetRelativePath(target, sampleRoot.transform);
                    if (path == null)
                    {
                        missingBones.Add(bone);
                        continue;
                    }

                    var poseParent = target.parent;
                    if (options.OutputBoneParentPaths != null
                        && options.OutputBoneParentPaths.TryGetValue(bone, out var parentPath))
                    {
                        poseParent = string.IsNullOrEmpty(parentPath)
                            ? sampleRoot.transform
                            : sampleRoot.transform.Find(parentPath);
                    }
                    if (poseParent == null)
                    {
                        missingBones.Add(bone);
                        continue;
                    }

                    var intervals = constantIntervalsByBone.TryGetValue(bone, out var boneIntervals)
                        ? (IReadOnlyList<PhantomTimeInterval>)boneIntervals
                        : Array.Empty<PhantomTimeInterval>();
                    recorders.Add(new BoneRecorder(
                        bone,
                        target,
                        poseParent,
                        path,
                        forcePositionBones.Contains(bone),
                        localizeRootMotion && bone == HumanBodyBones.Hips
                            ? sampleRoot.transform
                            : null,
                        intervals));
                }

                if (!AnimationMode.InAnimationMode())
                {
                    AnimationMode.StartAnimationMode();
                    startedAnimationMode = true;
                }

                PhantomPose[] Evaluate(float time)
                {
                    time = Mathf.Clamp(time, 0f, source.length);
                    AnimationMode.BeginSampling();
                    try
                    {
                        AnimationMode.SampleAnimationClip(sampleRoot, source, time);
                        return recorders.Select(recorder => recorder.ReadPose()).ToArray();
                    }
                    finally
                    {
                        AnimationMode.EndSampling();
                    }
                }

                var sourceTimes = options.SamplingMode == PhantomHumanoidSamplingMode.Adaptive
                    ? PhantomHumanoidClipAnalyzer.CollectSourceCandidateTimes(source, relevantBindings)
                    : EnumerateSampleTimes(source.length, sampleRate).ToArray();
                var sampling = options.SamplingMode == PhantomHumanoidSamplingMode.Adaptive
                    ? PhantomAdaptivePoseSampler.SampleAdaptive(
                        sourceTimes,
                        recorders.Count,
                        Evaluate,
                        recorders
                            .Select(recorder => recorder.Track.ConstantIntervals)
                            .ToArray(),
                        sampleRate,
                        positionTolerance,
                        rotationToleranceDegrees)
                    : PhantomAdaptivePoseSampler.SampleFixed(
                        sourceTimes,
                        recorders.Count,
                        Evaluate);

                return new PhantomHumanoidPoseBakeData(
                    recorders.Select(recorder => recorder.Track).ToArray(),
                    sampling,
                    missingBones);
            }
            finally
            {
                if (startedAnimationMode)
                {
                    AnimationMode.StopAnimationMode();
                }
                if (sampleRoot != null)
                {
                    Object.DestroyImmediate(sampleRoot);
                }
            }
        }

        internal static IReadOnlyDictionary<HumanBodyBones, Quaternion> SampleNeutralBoneRotations(
            GameObject humanoidRoot,
            IReadOnlyDictionary<HumanBodyBones, string> outputBoneParentPaths,
            IEnumerable<HumanBodyBones> bones)
        {
            if (humanoidRoot == null)
            {
                throw new ArgumentNullException(nameof(humanoidRoot));
            }
            if (outputBoneParentPaths == null)
            {
                throw new ArgumentNullException(nameof(outputBoneParentPaths));
            }
            if (bones == null)
            {
                throw new ArgumentNullException(nameof(bones));
            }

            var sourceAnimator = humanoidRoot.GetComponent<Animator>();
            if (sourceAnimator == null || sourceAnimator.avatar == null || !sourceAnimator.isHuman)
            {
                throw new ArgumentException(
                    $"'{humanoidRoot.name}' must have a valid Humanoid Animator on its root.",
                    nameof(humanoidRoot));
            }

            GameObject sampleRoot = null;
            HumanPoseHandler poseHandler = null;
            try
            {
                sampleRoot = CreateSamplingHierarchy(
                    humanoidRoot.transform,
                    sourceAnimator.avatar,
                    out var sampleAnimator);
                poseHandler = new HumanPoseHandler(sampleAnimator.avatar, sampleRoot.transform);

                var neutralPose = new HumanPose();
                poseHandler.GetHumanPose(ref neutralPose);
                if (neutralPose.muscles == null || neutralPose.muscles.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Unity returned no Humanoid muscles while sampling the neutral pose.");
                }

                Array.Clear(neutralPose.muscles, 0, neutralPose.muscles.Length);
                poseHandler.SetHumanPose(ref neutralPose);

                var rotations = new Dictionary<HumanBodyBones, Quaternion>();
                foreach (var bone in bones.Distinct().OrderBy(value => (int)value))
                {
                    var target = sampleAnimator.GetBoneTransform(bone);
                    if (target == null)
                    {
                        throw new InvalidOperationException(
                            $"Unity could not resolve Humanoid bone '{bone}' in the neutral-pose sampling hierarchy.");
                    }
                    if (!outputBoneParentPaths.TryGetValue(bone, out var poseParentPath))
                    {
                        throw new InvalidOperationException(
                            $"Humanoid bone '{bone}' has no sampling pose-parent path.");
                    }

                    var poseParent = string.IsNullOrEmpty(poseParentPath)
                        ? sampleRoot.transform
                        : sampleRoot.transform.Find(poseParentPath);
                    if (poseParent == null)
                    {
                        throw new InvalidOperationException(
                            $"Unity could not resolve sampling pose parent '{poseParentPath}' for Humanoid bone '{bone}'.");
                    }
                    rotations[bone] = ReadRelativeRotation(target, poseParent);
                }
                return rotations;
            }
            finally
            {
                poseHandler?.Dispose();
                if (sampleRoot != null)
                {
                    Object.DestroyImmediate(sampleRoot);
                }
            }
        }

        internal static Quaternion ReadRelativeRotation(Transform target, Transform poseParent)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (poseParent == null)
            {
                throw new ArgumentNullException(nameof(poseParent));
            }
            if (poseParent == target.parent)
            {
                return PhantomAdaptivePoseSampler.NormalizeQuaternion(target.localRotation);
            }

            var relative = poseParent.worldToLocalMatrix * target.localToWorldMatrix;
            return RotationFromMatrix(relative);
        }

        private static GameObject CreateSamplingHierarchy(
            Transform sourceRoot,
            Avatar avatar,
            out Animator animator)
        {
            var root = new GameObject(sourceRoot.name)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            foreach (Transform child in sourceRoot)
            {
                CloneTransformHierarchy(child, root.transform);
            }

            animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            if (!animator.isHuman)
            {
                Object.DestroyImmediate(root);
                throw new InvalidOperationException(
                    $"Unity could not bind humanoid avatar '{avatar.name}' to the temporary sampling hierarchy.");
            }
            return root;
        }

        private static void CloneTransformHierarchy(Transform source, Transform parent)
        {
            var clone = new GameObject(source.name)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            clone.transform.SetParent(parent, false);
            clone.transform.localPosition = source.localPosition;
            clone.transform.localRotation = source.localRotation;
            clone.transform.localScale = source.localScale;
            foreach (Transform child in source)
            {
                CloneTransformHierarchy(child, clone.transform);
            }
        }

        private static IEnumerable<float> EnumerateSampleTimes(float duration, float sampleRate)
        {
            if (duration <= 0f)
            {
                yield return 0f;
                yield break;
            }

            var frameCount = Math.Max(1, Mathf.CeilToInt(duration * sampleRate));
            for (var frame = 0; frame <= frameCount; frame++)
            {
                yield return frame == frameCount
                    ? duration
                    : Math.Min(frame / sampleRate, duration);
            }
        }

        private static string GetRelativePath(Transform target, Transform root)
        {
            if (target == root)
            {
                return string.Empty;
            }

            var parts = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Push(current.name);
                current = current.parent;
            }
            return current == root ? string.Join("/", parts) : null;
        }

        private static Quaternion RotationFromMatrix(Matrix4x4 matrix)
        {
            var forward = (Vector3)matrix.GetColumn(2);
            var up = (Vector3)matrix.GetColumn(1);
            if (forward.sqrMagnitude <= Mathf.Epsilon || up.sqrMagnitude <= Mathf.Epsilon)
            {
                return Quaternion.identity;
            }
            return PhantomAdaptivePoseSampler.NormalizeQuaternion(
                Quaternion.LookRotation(forward.normalized, up.normalized));
        }

        private sealed class BoneRecorder
        {
            private readonly Transform _target;
            private readonly Transform _poseParent;
            private readonly Transform _rootMotionSource;
            private Matrix4x4 _rootReference;
            private bool _hasRootReference;

            internal PhantomHumanoidBoneTrack Track { get; }

            internal BoneRecorder(
                HumanBodyBones bone,
                Transform target,
                Transform poseParent,
                string path,
                bool forcePosition,
                Transform rootMotionSource,
                IReadOnlyList<PhantomTimeInterval> constantIntervals)
            {
                _target = target;
                _poseParent = poseParent;
                _rootMotionSource = rootMotionSource;
                Track = new PhantomHumanoidBoneTrack(
                    bone,
                    path,
                    ReadRelativePose().Position,
                    forcePosition,
                    constantIntervals);
            }

            internal PhantomPose ReadPose()
            {
                var relativePose = ReadRelativePose();
                var position = relativePose.Position;
                var rotation = relativePose.Rotation;
                if (_rootMotionSource != null)
                {
                    var rootMatrix = _rootMotionSource.localToWorldMatrix;
                    if (!_hasRootReference)
                    {
                        _rootReference = rootMatrix;
                        _hasRootReference = true;
                    }

                    var rootInverse = rootMatrix.inverse;
                    var rootDelta = _rootReference.inverse * rootMatrix;
                    var parentRelativeToRoot = rootInverse * _poseParent.localToWorldMatrix;
                    var targetRelativeToRoot = rootInverse * _target.localToWorldMatrix;
                    var localized = parentRelativeToRoot.inverse * rootDelta * targetRelativeToRoot;
                    position = localized.GetColumn(3);
                    rotation = RotationFromMatrix(localized);
                }
                return new PhantomPose(
                    position,
                    PhantomAdaptivePoseSampler.NormalizeQuaternion(rotation));
            }

            private PhantomPose ReadRelativePose()
            {
                if (_poseParent == _target.parent)
                {
                    return new PhantomPose(_target.localPosition, _target.localRotation);
                }

                var relative = _poseParent.worldToLocalMatrix * _target.localToWorldMatrix;
                return new PhantomPose(relative.GetColumn(3), RotationFromMatrix(relative));
            }
        }
    }
}
