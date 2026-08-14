using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MPCCT.PhantomSystem.Editor
{
    internal enum PhantomAnimationBindingKind
    {
        ResolvedHumanoid,
        RootTransform,
        AnimatorParameter,
        UnsupportedAnimator,
        NonHumanoid
    }

    internal static class PhantomAnimationBindingClassifier
    {
        public static PhantomAnimationBindingKind Classify(
            EditorCurveBinding binding,
            ISet<string> animatorParameterNames = null)
        {
            if (binding.type == typeof(Animator))
            {
                if (PhantomHumanoidClipBaker.IsRootMotionBinding(binding))
                {
                    return PhantomAnimationBindingKind.RootTransform;
                }

                if (PhantomHumanoidClipBaker.TryResolveHumanoidBinding(binding, out _, out _))
                {
                    return PhantomAnimationBindingKind.ResolvedHumanoid;
                }

                return string.IsNullOrEmpty(binding.path)
                       && animatorParameterNames != null
                       && animatorParameterNames.Contains(binding.propertyName)
                    ? PhantomAnimationBindingKind.AnimatorParameter
                    : PhantomAnimationBindingKind.UnsupportedAnimator;
            }

            if (PhantomHumanoidClipBaker.IsRootPositionOrRotationBinding(binding)
                || PhantomHumanoidClipBaker.IsRootScaleBinding(binding))
            {
                return PhantomAnimationBindingKind.RootTransform;
            }

            return PhantomAnimationBindingKind.NonHumanoid;
        }
    }

    /// <summary>
    /// Converts the humanoid portion of one animation clip into ordinary transform curves
    /// targeting a specific humanoid hierarchy. Controller integration is intentionally
    /// outside the scope of this class.
    /// </summary>
    public static class PhantomHumanoidClipBaker
    {
        private const float DefaultSampleRate = 60f;
        private const float PositionEpsilonSquared = 0.0000000001f;
        private const float TimeEpsilon = 0.000001f;

        private static readonly Dictionary<string, int> MuscleIndices =
            HumanTrait.MuscleName
                .Select((name, index) => new KeyValuePair<string, int>(name, index))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        public static PhantomHumanoidClipBakeResult Bake(
            AnimationClip source,
            GameObject humanoidRoot,
            float sampleRate = 0f)
        {
            return Bake(
                source,
                humanoidRoot,
                new PhantomHumanoidClipBakeOptions
                {
                    SamplingMode = PhantomHumanoidSamplingMode.Fixed,
                    SampleRate = sampleRate,
                    LocalizeRootMotionToHips = true
                });
        }

        public static PhantomHumanoidClipBakeResult Bake(
            AnimationClip source,
            GameObject humanoidRoot,
            PhantomHumanoidClipBakeOptions options)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (humanoidRoot == null)
            {
                throw new ArgumentNullException(nameof(humanoidRoot));
            }

            var sourceAnimator = humanoidRoot.GetComponent<Animator>();
            if (sourceAnimator == null || sourceAnimator.avatar == null || !sourceAnimator.isHuman)
            {
                throw new ArgumentException(
                    $"'{humanoidRoot.name}' must have a valid humanoid Animator on its root.",
                    nameof(humanoidRoot));
            }

            options ??= new PhantomHumanoidClipBakeOptions();
            var sampleRate = options.SampleRate;
            if (sampleRate <= 0f)
            {
                sampleRate = source.frameRate > 0f ? source.frameRate : DefaultSampleRate;
            }
            if (float.IsNaN(sampleRate) || float.IsInfinity(sampleRate))
            {
                sampleRate = DefaultSampleRate;
            }
            var positionTolerance = NormalizeTolerance(
                options.PositionErrorTolerance,
                0.0005f);
            var rotationToleranceDegrees = NormalizeTolerance(
                options.RotationErrorToleranceDegrees,
                0.25f);
            var sourceSettings = AnimationUtility.GetAnimationClipSettings(source);
            var effectiveMirror = ResolveEffectiveMirror(
                sourceSettings.mirror,
                options.InheritedMirror);

            var animatorBindings = AnimationUtility.GetCurveBindings(source)
                .Where(binding => binding.type == typeof(Animator))
                .ToArray();
            var rootTransformBindings = AnimationUtility.GetCurveBindings(source)
                .Where(binding => binding.type == typeof(Transform)
                                  && string.IsNullOrEmpty(binding.path))
                .ToArray();
            var hasRootMotion = animatorBindings.Any(IsRootMotionBinding)
                                || rootTransformBindings.Any(IsRootPositionOrRotationBinding);
            var ignoredRootScaleBindings = options.LocalizeRootMotionToHips
                ? rootTransformBindings.Where(IsRootScaleBinding).ToArray()
                : Array.Empty<EditorCurveBinding>();
            var relevantBindings = animatorBindings
                .Where(binding => PhantomAnimationBindingClassifier.Classify(
                    binding,
                    options.AnimatorParameterNames) != PhantomAnimationBindingKind.AnimatorParameter)
                .Concat(rootTransformBindings.Where(binding =>
                    IsRootPositionOrRotationBinding(binding)
                    || IsRootScaleBinding(binding)))
                .ToArray();
            var affectedBones = new HashSet<HumanBodyBones>();
            var forcePositionBones = new HashSet<HumanBodyBones>();
            var skippedAnimatorBindings = new List<EditorCurveBinding>();
            var resolvedHumanoidBindingCount = 0;
            foreach (var binding in animatorBindings)
            {
                var kind = PhantomAnimationBindingClassifier.Classify(
                    binding,
                    options.AnimatorParameterNames);
                if (kind == PhantomAnimationBindingKind.UnsupportedAnimator)
                {
                    skippedAnimatorBindings.Add(binding);
                    continue;
                }

                if (kind == PhantomAnimationBindingKind.ResolvedHumanoid
                    && TryResolveHumanoidBinding(binding, out var bone, out var forcePosition))
                {
                    resolvedHumanoidBindingCount++;
                    if (effectiveMirror)
                    {
                        bone = MirrorHumanoidBone(bone);
                    }
                    affectedBones.Add(bone);
                    if (forcePosition)
                    {
                        forcePositionBones.Add(bone);
                    }
                }
            }

            if (options.LocalizeRootMotionToHips && hasRootMotion)
            {
                affectedBones.Add(HumanBodyBones.Hips);
                forcePositionBones.Add(HumanBodyBones.Hips);
            }

            var output = CreateOutputClip(source, sampleRate);
            CopyNonHumanoidCurves(
                source,
                output,
                options.LocalizeRootMotionToHips,
                options.AnimatorParameterNames);

            var bakedBones = new List<HumanBodyBones>();
            var missingBones = new List<HumanBodyBones>();
            var samplingDiagnostics = default(SamplingDiagnostics);
            AnimationClip mirroredEvaluationClip = null;
            try
            {
                var evaluationClip = source;
                if (options.InheritedMirror)
                {
                    mirroredEvaluationClip = Object.Instantiate(source);
                    mirroredEvaluationClip.name = $"{source.name}_PhantomMirrorEvaluation";
                    var evaluationSettings = AnimationUtility.GetAnimationClipSettings(
                        mirroredEvaluationClip);
                    evaluationSettings.mirror = ResolveEffectiveMirror(
                        evaluationSettings.mirror,
                        true);
                    AnimationUtility.SetAnimationClipSettings(
                        mirroredEvaluationClip,
                        evaluationSettings);
                    evaluationClip = mirroredEvaluationClip;
                }

                if (affectedBones.Count > 0)
                {
                    samplingDiagnostics = BakeHumanoidCurves(
                        evaluationClip,
                        sourceAnimator.avatar,
                        humanoidRoot.transform,
                        output,
                        sampleRate,
                        affectedBones,
                        forcePositionBones,
                        options.LocalizeRootMotionToHips && hasRootMotion,
                        options,
                        positionTolerance,
                        rotationToleranceDegrees,
                        relevantBindings,
                        effectiveMirror,
                        bakedBones,
                        missingBones);
                }
            }
            finally
            {
                if (mirroredEvaluationClip != null)
                {
                    Object.DestroyImmediate(mirroredEvaluationClip);
                }
            }

            output.EnsureQuaternionContinuity();
            return new PhantomHumanoidClipBakeResult(
                output,
                sampleRate,
                resolvedHumanoidBindingCount,
                bakedBones,
                missingBones,
                skippedAnimatorBindings,
                options.LocalizeRootMotionToHips && hasRootMotion,
                ignoredRootScaleBindings,
                options.SamplingMode,
                samplingDiagnostics.SourceCandidateTimeCount,
                samplingDiagnostics.AdaptiveSampleCount,
                samplingDiagnostics.UnsimplifiedPoseKeyCount,
                samplingDiagnostics.OutputPoseKeyCount,
                samplingDiagnostics.HitSampleRateLimit);
        }

        private static AnimationClip CreateOutputClip(
            AnimationClip source,
            float sampleRate)
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
            // The output contains ordinary Transform curves. Humanoid mirroring has
            // already been consumed while sampling and no longer applies to this clip.
            outputSettings.mirror = false;
            AnimationUtility.SetAnimationClipSettings(output, outputSettings);
            AnimationUtility.SetAnimationEvents(
                output,
                AnimationUtility.GetAnimationEvents(source));
            return output;
        }

        internal static bool ResolveEffectiveMirror(
            bool clipMirror,
            bool inheritedMirror)
        {
            return clipMirror ^ inheritedMirror;
        }

        internal static HumanBodyBones MirrorHumanoidBone(HumanBodyBones bone)
        {
            if (bone < 0 || bone >= HumanBodyBones.LastBone)
            {
                return bone;
            }

            var name = bone.ToString();
            string mirroredName;
            if (name.StartsWith("Left", StringComparison.Ordinal))
            {
                mirroredName = "Right" + name.Substring("Left".Length);
            }
            else if (name.StartsWith("Right", StringComparison.Ordinal))
            {
                mirroredName = "Left" + name.Substring("Right".Length);
            }
            else
            {
                return bone;
            }

            return Enum.TryParse(mirroredName, false, out HumanBodyBones mirrored)
                   && mirrored >= 0
                   && mirrored < HumanBodyBones.LastBone
                   && string.Equals(mirrored.ToString(), mirroredName, StringComparison.Ordinal)
                ? mirrored
                : bone;
        }

        private static void CopyNonHumanoidCurves(
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
                    && (IsRootPositionOrRotationBinding(binding)
                        || IsRootScaleBinding(binding)))
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

        private static SamplingDiagnostics BakeHumanoidCurves(
            AnimationClip source,
            Avatar avatar,
            Transform sourceRoot,
            AnimationClip output,
            float sampleRate,
            HashSet<HumanBodyBones> affectedBones,
            HashSet<HumanBodyBones> forcePositionBones,
            bool localizeRootMotion,
            PhantomHumanoidClipBakeOptions options,
            float positionTolerance,
            float rotationToleranceDegrees,
            IReadOnlyList<EditorCurveBinding> relevantBindings,
            bool mirrorBindings,
            List<HumanBodyBones> bakedBones,
            List<HumanBodyBones> missingBones)
        {
            GameObject sampleRoot = null;
            var startedAnimationMode = false;
            try
            {
                sampleRoot = CreateSamplingHierarchy(sourceRoot, avatar, out var sampleAnimator);
                var recorders = new List<BoneRecorder>();
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

                    recorders.Add(new BoneRecorder(
                        bone,
                        target,
                        poseParent,
                        path,
                        forcePositionBones.Contains(bone),
                        localizeRootMotion && bone == HumanBodyBones.Hips
                            ? sampleRoot.transform
                            : null));
                }

                if (!AnimationMode.InAnimationMode())
                {
                    AnimationMode.StartAnimationMode();
                    startedAnimationMode = true;
                }

                var sourceTimes = options.SamplingMode == PhantomHumanoidSamplingMode.Adaptive
                    ? CollectSourceCandidateTimes(source, relevantBindings)
                    : EnumerateSampleTimes(source.length, sampleRate).ToList();
                var constantIntervalsByBone = options.SamplingMode == PhantomHumanoidSamplingMode.Adaptive
                    ? CollectConstantIntervals(source, relevantBindings, mirrorBindings)
                    : new Dictionary<HumanBodyBones, List<TimeInterval>>();
                var samples = new SortedDictionary<float, BonePose[]>();

                void Sample(float time)
                {
                    time = Mathf.Clamp(time, 0f, source.length);
                    if (samples.ContainsKey(time))
                    {
                        return;
                    }

                    AnimationMode.BeginSampling();
                    try
                    {
                        AnimationMode.SampleAnimationClip(sampleRoot, source, time);
                        var poses = new BonePose[recorders.Count];
                        for (var index = 0; index < recorders.Count; index++)
                        {
                            poses[index] = recorders[index].ReadPose();
                        }
                        samples.Add(time, poses);
                    }
                    finally
                    {
                        AnimationMode.EndSampling();
                    }
                }

                foreach (var time in sourceTimes)
                {
                    Sample(time);
                }

                var adaptiveSampleCount = 0;
                var hitSampleRateLimit = false;
                if (options.SamplingMode == PhantomHumanoidSamplingMode.Adaptive)
                {
                    var initialIntervals = sourceTimes
                        .OrderBy(time => time)
                        .Zip(sourceTimes.OrderBy(time => time).Skip(1),
                            (start, end) => new TimeInterval(start, end))
                        .ToArray();
                    foreach (var interval in initialIntervals)
                    {
                        SubdivideAdaptive(
                            interval.Start,
                            interval.End,
                            recorders,
                            constantIntervalsByBone,
                            samples,
                            Sample,
                            sampleRate,
                            positionTolerance,
                            rotationToleranceDegrees,
                            ref adaptiveSampleCount,
                            ref hitSampleRateLimit);
                    }
                }

                var allTimes = samples.Keys.ToList();
                var unsimplifiedPoseKeyCount = allTimes.Count * recorders.Count;
                var outputPoseKeyCount = 0;

                for (var recorderIndex = 0; recorderIndex < recorders.Count; recorderIndex++)
                {
                    var recorder = recorders[recorderIndex];
                    var constantIntervals = constantIntervalsByBone.TryGetValue(
                        recorder.Bone,
                        out var boneIntervals)
                            ? (IReadOnlyList<TimeInterval>)boneIntervals
                            : Array.Empty<TimeInterval>();
                    var poses = allTimes
                        .Select(time => samples[time][recorderIndex])
                        .ToList();
                    var keptIndices = options.SamplingMode == PhantomHumanoidSamplingMode.Adaptive
                        ? SimplifyBoneSamples(
                            allTimes,
                            poses,
                            constantIntervals,
                            positionTolerance,
                            rotationToleranceDegrees)
                        : Enumerable.Range(0, allTimes.Count).ToList();
                    recorder.WriteTo(output, allTimes, poses, keptIndices, constantIntervals);
                    outputPoseKeyCount += keptIndices.Count;
                    bakedBones.Add(recorder.Bone);
                }

                return new SamplingDiagnostics(
                    sourceTimes.Count,
                    adaptiveSampleCount,
                    unsimplifiedPoseKeyCount,
                    outputPoseKeyCount,
                    hitSampleRateLimit);
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

        private static void CloneTransformHierarchy(
            Transform source,
            Transform parent)
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

        private static IEnumerable<float> EnumerateSampleTimes(
            float duration,
            float sampleRate)
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

        private static List<float> CollectSourceCandidateTimes(
            AnimationClip source,
            IReadOnlyList<EditorCurveBinding> relevantBindings)
        {
            var times = new SortedSet<float> { 0f, source.length };
            foreach (var binding in relevantBindings)
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null)
                {
                    continue;
                }

                foreach (var key in curve.keys)
                {
                    times.Add(Mathf.Clamp(key.time, 0f, source.length));
                }
            }

            return times.ToList();
        }

        private static Dictionary<HumanBodyBones, List<TimeInterval>> CollectConstantIntervals(
            AnimationClip source,
            IReadOnlyList<EditorCurveBinding> relevantBindings,
            bool mirrorBindings)
        {
            var intervals = new Dictionary<HumanBodyBones, List<TimeInterval>>();
            foreach (var binding in relevantBindings)
            {
                HumanBodyBones bone;
                if (binding.type == typeof(Animator))
                {
                    if (!TryResolveHumanoidBinding(binding, out bone, out _))
                    {
                        continue;
                    }
                }
                else if (IsRootPositionOrRotationBinding(binding))
                {
                    bone = HumanBodyBones.Hips;
                }
                else
                {
                    continue;
                }

                if (mirrorBindings)
                {
                    bone = MirrorHumanoidBone(bone);
                }

                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null)
                {
                    continue;
                }

                for (var index = 0; index + 1 < curve.length; index++)
                {
                    if (AnimationUtility.GetKeyRightTangentMode(curve, index)
                        != AnimationUtility.TangentMode.Constant)
                    {
                        continue;
                    }

                    if (!intervals.TryGetValue(bone, out var boneIntervals))
                    {
                        boneIntervals = new List<TimeInterval>();
                        intervals.Add(bone, boneIntervals);
                    }

                    boneIntervals.Add(new TimeInterval(
                        Mathf.Clamp(curve.keys[index].time, 0f, source.length),
                        Mathf.Clamp(curve.keys[index + 1].time, 0f, source.length)));
                }
            }

            return intervals;
        }

        private static void SubdivideAdaptive(
            float start,
            float end,
            IReadOnlyList<BoneRecorder> recorders,
            IReadOnlyDictionary<HumanBodyBones, List<TimeInterval>> constantIntervalsByBone,
            SortedDictionary<float, BonePose[]> samples,
            Action<float> sample,
            float maximumSampleRate,
            float positionTolerance,
            float rotationToleranceDegrees,
            ref int adaptiveSampleCount,
            ref bool hitSampleRateLimit)
        {
            if (end - start <= TimeEpsilon)
            {
                return;
            }

            var midpoint = (start + end) * 0.5f;
            var existed = samples.ContainsKey(midpoint);
            sample(midpoint);
            if (!existed)
            {
                adaptiveSampleCount++;
            }

            var startPoses = samples[start];
            var midpointPoses = samples[midpoint];
            var endPoses = samples[end];
            var exceedsTolerance = false;
            for (var index = 0; index < recorders.Count; index++)
            {
                var isConstant = constantIntervalsByBone.TryGetValue(
                                     recorders[index].Bone,
                                     out var intervals)
                                 && IsConstantSegment(start, end, intervals);
                if (PoseErrorRatio(
                        startPoses[index],
                        endPoses[index],
                        midpointPoses[index],
                        0.5f,
                        positionTolerance,
                        rotationToleranceDegrees,
                        isConstant) > 1f)
                {
                    exceedsTolerance = true;
                    break;
                }
            }

            if (!exceedsTolerance)
            {
                return;
            }

            var minimumInterval = 1f / Mathf.Max(1f, maximumSampleRate);
            if (midpoint - start < minimumInterval - TimeEpsilon
                || end - midpoint < minimumInterval - TimeEpsilon)
            {
                hitSampleRateLimit = true;
                if (!existed)
                {
                    samples.Remove(midpoint);
                    adaptiveSampleCount--;
                }
                return;
            }

            SubdivideAdaptive(
                start,
                midpoint,
                recorders,
                constantIntervalsByBone,
                samples,
                sample,
                maximumSampleRate,
                positionTolerance,
                rotationToleranceDegrees,
                ref adaptiveSampleCount,
                ref hitSampleRateLimit);
            SubdivideAdaptive(
                midpoint,
                end,
                recorders,
                constantIntervalsByBone,
                samples,
                sample,
                maximumSampleRate,
                positionTolerance,
                rotationToleranceDegrees,
                ref adaptiveSampleCount,
                ref hitSampleRateLimit);
        }

        private static List<int> SimplifyBoneSamples(
            IReadOnlyList<float> times,
            IReadOnlyList<BonePose> poses,
            IReadOnlyList<TimeInterval> constantIntervals,
            float positionTolerance,
            float rotationToleranceDegrees)
        {
            if (times.Count <= 2)
            {
                return Enumerable.Range(0, times.Count).ToList();
            }

            var protectedIndices = new SortedSet<int> { 0, times.Count - 1 };
            foreach (var interval in constantIntervals)
            {
                AddNearestTimeIndex(times, interval.Start, protectedIndices);
                AddNearestTimeIndex(times, interval.End, protectedIndices);
            }

            var boundaries = protectedIndices.ToArray();
            var kept = new SortedSet<int>(protectedIndices);
            for (var index = 0; index + 1 < boundaries.Length; index++)
            {
                SimplifyRange(
                    boundaries[index],
                    boundaries[index + 1],
                    times,
                    poses,
                    positionTolerance,
                    rotationToleranceDegrees,
                    constantIntervals,
                    kept);
            }

            return kept.ToList();
        }

        private static void SimplifyRange(
            int startIndex,
            int endIndex,
            IReadOnlyList<float> times,
            IReadOnlyList<BonePose> poses,
            float positionTolerance,
            float rotationToleranceDegrees,
            IReadOnlyList<TimeInterval> constantIntervals,
            ISet<int> kept)
        {
            if (endIndex <= startIndex + 1)
            {
                return;
            }

            var duration = times[endIndex] - times[startIndex];
            if (duration <= TimeEpsilon)
            {
                return;
            }

            var worstIndex = -1;
            var worstRatio = 1f;
            for (var index = startIndex + 1; index < endIndex; index++)
            {
                var t = (times[index] - times[startIndex]) / duration;
                var ratio = PoseErrorRatio(
                    poses[startIndex],
                    poses[endIndex],
                    poses[index],
                    t,
                    positionTolerance,
                    rotationToleranceDegrees,
                    IsConstantSegment(
                        times[startIndex],
                        times[endIndex],
                        constantIntervals));
                if (ratio > worstRatio)
                {
                    worstRatio = ratio;
                    worstIndex = index;
                }
            }

            if (worstIndex < 0)
            {
                return;
            }

            kept.Add(worstIndex);
            SimplifyRange(
                startIndex,
                worstIndex,
                times,
                poses,
                positionTolerance,
                rotationToleranceDegrees,
                constantIntervals,
                kept);
            SimplifyRange(
                worstIndex,
                endIndex,
                times,
                poses,
                positionTolerance,
                rotationToleranceDegrees,
                constantIntervals,
                kept);
        }

        private static float PoseErrorRatio(
            BonePose start,
            BonePose end,
            BonePose actual,
            float t,
            float positionTolerance,
            float rotationToleranceDegrees,
            bool constant = false)
        {
            var predictedPosition = constant
                ? start.Position
                : Vector3.LerpUnclamped(start.Position, end.Position, t);
            var predictedRotation = start.Rotation;
            if (!constant)
            {
                var endRotation = MatchQuaternionHemisphere(start.Rotation, end.Rotation);
                predictedRotation = NormalizeQuaternion(new Quaternion(
                    Mathf.LerpUnclamped(start.Rotation.x, endRotation.x, t),
                    Mathf.LerpUnclamped(start.Rotation.y, endRotation.y, t),
                    Mathf.LerpUnclamped(start.Rotation.z, endRotation.z, t),
                    Mathf.LerpUnclamped(start.Rotation.w, endRotation.w, t)));
            }
            return Mathf.Max(
                Vector3.Distance(predictedPosition, actual.Position)
                    / Mathf.Max(positionTolerance, Mathf.Epsilon),
                Quaternion.Angle(predictedRotation, actual.Rotation)
                    / Mathf.Max(rotationToleranceDegrees, Mathf.Epsilon));
        }

        private static Quaternion MatchQuaternionHemisphere(
            Quaternion reference,
            Quaternion value)
        {
            return Quaternion.Dot(reference, value) < 0f
                ? new Quaternion(-value.x, -value.y, -value.z, -value.w)
                : value;
        }

        private static Quaternion NormalizeQuaternion(Quaternion value)
        {
            var magnitude = Mathf.Sqrt(
                value.x * value.x
                + value.y * value.y
                + value.z * value.z
                + value.w * value.w);
            if (magnitude <= Mathf.Epsilon)
            {
                return Quaternion.identity;
            }

            return new Quaternion(
                value.x / magnitude,
                value.y / magnitude,
                value.z / magnitude,
                value.w / magnitude);
        }

        private static float NormalizeTolerance(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value <= 0f
                ? fallback
                : value;
        }

        private static void AddNearestTimeIndex(
            IReadOnlyList<float> times,
            float target,
            ISet<int> indices)
        {
            var nearestIndex = 0;
            var nearestDistance = float.PositiveInfinity;
            for (var index = 0; index < times.Count; index++)
            {
                var distance = Mathf.Abs(times[index] - target);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = index;
                }
            }
            indices.Add(nearestIndex);
        }

        private static bool IsConstantSegment(
            float start,
            float end,
            IReadOnlyList<TimeInterval> intervals)
        {
            return intervals.Any(interval =>
                start >= interval.Start - TimeEpsilon
                && end <= interval.End + TimeEpsilon);
        }

        private static string GetRelativePath(
            Transform target,
            Transform root)
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

        internal static bool TryResolveHumanoidBinding(
            EditorCurveBinding binding,
            out HumanBodyBones bone,
            out bool forcePosition)
        {
            bone = HumanBodyBones.LastBone;
            forcePosition = false;
            if (binding.type != typeof(Animator))
            {
                return false;
            }

            // Finger muscle bindings use Unity's serialized Animator property names
            // (for example, "LeftHand.Index.1 Stretched"), which are different from
            // the display names returned by HumanTrait.MuscleName. Resolve those
            // bindings explicitly before falling back to the HumanTrait lookup.
            if (TryResolveFingerBinding(binding.propertyName, out bone))
            {
                return true;
            }

            if (MuscleIndices.TryGetValue(binding.propertyName, out var muscleIndex))
            {
                var boneIndex = HumanTrait.BoneFromMuscle(muscleIndex);
                if (boneIndex >= 0 && boneIndex < (int)HumanBodyBones.LastBone)
                {
                    bone = (HumanBodyBones)boneIndex;
                    return true;
                }
            }

            // Unity serializes translation degrees of freedom as Animator curves such
            // as "ChestTDOF.z". These curves affect the local position of the named
            // humanoid bone, so they must participate in sampling and force position
            // curves to be written even when the sampled position is otherwise static.
            if (TryResolveTranslationDofBinding(binding.propertyName, out bone))
            {
                forcePosition = true;
                return true;
            }

            if (StartsWithAny(binding.propertyName, "RootT.", "BodyT."))
            {
                bone = HumanBodyBones.Hips;
                forcePosition = true;
                return true;
            }

            if (StartsWithAny(binding.propertyName, "RootQ.", "BodyQ."))
            {
                bone = HumanBodyBones.Hips;
                return true;
            }

            if (StartsWithAny(binding.propertyName, "LeftFootT."))
            {
                bone = HumanBodyBones.LeftFoot;
                forcePosition = true;
                return true;
            }

            if (StartsWithAny(binding.propertyName, "LeftFootQ."))
            {
                bone = HumanBodyBones.LeftFoot;
                return true;
            }

            if (StartsWithAny(binding.propertyName, "RightFootT."))
            {
                bone = HumanBodyBones.RightFoot;
                forcePosition = true;
                return true;
            }

            if (StartsWithAny(binding.propertyName, "RightFootQ."))
            {
                bone = HumanBodyBones.RightFoot;
                return true;
            }

            if (StartsWithAny(binding.propertyName, "LeftHandT."))
            {
                bone = HumanBodyBones.LeftHand;
                forcePosition = true;
                return true;
            }

            if (StartsWithAny(binding.propertyName, "LeftHandQ."))
            {
                bone = HumanBodyBones.LeftHand;
                return true;
            }

            if (StartsWithAny(binding.propertyName, "RightHandT."))
            {
                bone = HumanBodyBones.RightHand;
                forcePosition = true;
                return true;
            }

            if (StartsWithAny(binding.propertyName, "RightHandQ."))
            {
                bone = HumanBodyBones.RightHand;
                return true;
            }

            return false;
        }

        private static bool TryResolveTranslationDofBinding(
            string propertyName,
            out HumanBodyBones bone)
        {
            bone = HumanBodyBones.LastBone;
            if (string.IsNullOrEmpty(propertyName) || propertyName.Length < 3)
            {
                return false;
            }

            var separator = propertyName.LastIndexOf('.');
            if (separator <= 0 || separator != propertyName.Length - 2)
            {
                return false;
            }

            var component = propertyName[propertyName.Length - 1];
            if (component != 'x' && component != 'y' && component != 'z')
            {
                return false;
            }

            const string suffix = "TDOF";
            var serializedBoneName = propertyName.Substring(0, separator);
            if (!serializedBoneName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            var boneName = serializedBoneName.Substring(
                0,
                serializedBoneName.Length - suffix.Length);
            if (!Enum.TryParse(boneName, false, out bone)
                || bone == HumanBodyBones.LastBone
                || !Enum.IsDefined(typeof(HumanBodyBones), bone)
                || !string.Equals(bone.ToString(), boneName, StringComparison.Ordinal))
            {
                bone = HumanBodyBones.LastBone;
                return false;
            }

            return true;
        }

        internal static bool IsRootMotionBinding(EditorCurveBinding binding)
        {
            return binding.type == typeof(Animator)
                   && StartsWithAny(binding.propertyName, "RootT.", "RootQ.");
        }

        internal static bool IsRootPositionOrRotationBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Transform)
                || !string.IsNullOrEmpty(binding.path))
            {
                return false;
            }

            return StartsWithAny(
                binding.propertyName,
                "m_LocalPosition.",
                "m_LocalRotation.",
                "localEulerAnglesRaw.",
                "localEulerAnglesBaked.");
        }

        internal static bool IsRootScaleBinding(EditorCurveBinding binding)
        {
            return binding.type == typeof(Transform)
                   && string.IsNullOrEmpty(binding.path)
                   && StartsWithAny(binding.propertyName, "m_LocalScale.");
        }

        private static bool TryResolveFingerBinding(
            string propertyName,
            out HumanBodyBones bone)
        {
            bone = HumanBodyBones.LastBone;
            if (string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            var parts = propertyName.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            var isLeft = string.Equals(parts[0], "LeftHand", StringComparison.Ordinal);
            var isRight = string.Equals(parts[0], "RightHand", StringComparison.Ordinal);
            if (!isLeft && !isRight)
            {
                return false;
            }

            var segment = parts[2];
            var jointIndex = string.Equals(segment, "1 Stretched", StringComparison.Ordinal)
                || string.Equals(segment, "Spread", StringComparison.Ordinal)
                    ? 0
                    : string.Equals(segment, "2 Stretched", StringComparison.Ordinal)
                        ? 1
                        : string.Equals(segment, "3 Stretched", StringComparison.Ordinal)
                            ? 2
                            : -1;
            if (jointIndex < 0)
            {
                return false;
            }

            return TryResolveFingerBone(isLeft, parts[1], jointIndex, out bone);
        }

        private static bool TryResolveFingerBone(
            bool isLeft,
            string finger,
            int jointIndex,
            out HumanBodyBones bone)
        {
            bone = HumanBodyBones.LastBone;
            switch (finger)
            {
                case "Thumb":
                    bone = isLeft
                        ? jointIndex == 0 ? HumanBodyBones.LeftThumbProximal
                        : jointIndex == 1 ? HumanBodyBones.LeftThumbIntermediate
                        : HumanBodyBones.LeftThumbDistal
                        : jointIndex == 0 ? HumanBodyBones.RightThumbProximal
                        : jointIndex == 1 ? HumanBodyBones.RightThumbIntermediate
                        : HumanBodyBones.RightThumbDistal;
                    return true;

                case "Index":
                    bone = isLeft
                        ? jointIndex == 0 ? HumanBodyBones.LeftIndexProximal
                        : jointIndex == 1 ? HumanBodyBones.LeftIndexIntermediate
                        : HumanBodyBones.LeftIndexDistal
                        : jointIndex == 0 ? HumanBodyBones.RightIndexProximal
                        : jointIndex == 1 ? HumanBodyBones.RightIndexIntermediate
                        : HumanBodyBones.RightIndexDistal;
                    return true;

                case "Middle":
                    bone = isLeft
                        ? jointIndex == 0 ? HumanBodyBones.LeftMiddleProximal
                        : jointIndex == 1 ? HumanBodyBones.LeftMiddleIntermediate
                        : HumanBodyBones.LeftMiddleDistal
                        : jointIndex == 0 ? HumanBodyBones.RightMiddleProximal
                        : jointIndex == 1 ? HumanBodyBones.RightMiddleIntermediate
                        : HumanBodyBones.RightMiddleDistal;
                    return true;

                case "Ring":
                    bone = isLeft
                        ? jointIndex == 0 ? HumanBodyBones.LeftRingProximal
                        : jointIndex == 1 ? HumanBodyBones.LeftRingIntermediate
                        : HumanBodyBones.LeftRingDistal
                        : jointIndex == 0 ? HumanBodyBones.RightRingProximal
                        : jointIndex == 1 ? HumanBodyBones.RightRingIntermediate
                        : HumanBodyBones.RightRingDistal;
                    return true;

                case "Little":
                    bone = isLeft
                        ? jointIndex == 0 ? HumanBodyBones.LeftLittleProximal
                        : jointIndex == 1 ? HumanBodyBones.LeftLittleIntermediate
                        : HumanBodyBones.LeftLittleDistal
                        : jointIndex == 0 ? HumanBodyBones.RightLittleProximal
                        : jointIndex == 1 ? HumanBodyBones.RightLittleIntermediate
                        : HumanBodyBones.RightLittleDistal;
                    return true;

                default:
                    return false;
            }
        }

        private static bool StartsWithAny(
            string value,
            params string[] prefixes)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (var prefix in prefixes)
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct BonePose
        {
            internal Vector3 Position { get; }
            internal Quaternion Rotation { get; }

            internal BonePose(Vector3 position, Quaternion rotation)
            {
                Position = position;
                Rotation = rotation;
            }
        }

        private readonly struct TimeInterval
        {
            internal float Start { get; }
            internal float End { get; }

            internal TimeInterval(float start, float end)
            {
                Start = start;
                End = end;
            }
        }

        private readonly struct SamplingDiagnostics
        {
            internal int SourceCandidateTimeCount { get; }
            internal int AdaptiveSampleCount { get; }
            internal int UnsimplifiedPoseKeyCount { get; }
            internal int OutputPoseKeyCount { get; }
            internal bool HitSampleRateLimit { get; }

            internal SamplingDiagnostics(
                int sourceCandidateTimeCount,
                int adaptiveSampleCount,
                int unsimplifiedPoseKeyCount,
                int outputPoseKeyCount,
                bool hitSampleRateLimit)
            {
                SourceCandidateTimeCount = sourceCandidateTimeCount;
                AdaptiveSampleCount = adaptiveSampleCount;
                UnsimplifiedPoseKeyCount = unsimplifiedPoseKeyCount;
                OutputPoseKeyCount = outputPoseKeyCount;
                HitSampleRateLimit = hitSampleRateLimit;
            }
        }

        private sealed class BoneRecorder
        {
            private readonly Transform _target;
            private readonly Transform _poseParent;
            private readonly string _path;
            private readonly Vector3 _bindPosition;
            private readonly bool _forcePosition;
            private readonly Transform _rootMotionSource;
            private Matrix4x4 _rootReference;
            private bool _hasRootReference;

            public HumanBodyBones Bone { get; }

            public BoneRecorder(
                HumanBodyBones bone,
                Transform target,
                Transform poseParent,
                string path,
                bool forcePosition,
                Transform rootMotionSource)
            {
                Bone = bone;
                _target = target;
                _poseParent = poseParent;
                _path = path;
                _bindPosition = ReadRelativePose().Position;
                _forcePosition = forcePosition;
                _rootMotionSource = rootMotionSource;
            }

            public BonePose ReadPose()
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
                    var localized = parentRelativeToRoot.inverse
                                    * rootDelta
                                    * targetRelativeToRoot;
                    position = localized.GetColumn(3);
                    rotation = RotationFromMatrix(localized);
                }

                return new BonePose(position, NormalizeQuaternion(rotation));
            }

            private BonePose ReadRelativePose()
            {
                if (_poseParent == _target.parent)
                {
                    return new BonePose(_target.localPosition, _target.localRotation);
                }

                var relative = _poseParent.worldToLocalMatrix * _target.localToWorldMatrix;
                return new BonePose(
                    relative.GetColumn(3),
                    RotationFromMatrix(relative));
            }

            private static Quaternion RotationFromMatrix(Matrix4x4 matrix)
            {
                var forward = (Vector3)matrix.GetColumn(2);
                var up = (Vector3)matrix.GetColumn(1);
                if (forward.sqrMagnitude <= Mathf.Epsilon
                    || up.sqrMagnitude <= Mathf.Epsilon)
                {
                    return Quaternion.identity;
                }

                return Quaternion.LookRotation(forward.normalized, up.normalized);
            }

            public void WriteTo(
                AnimationClip output,
                IReadOnlyList<float> times,
                IReadOnlyList<BonePose> poses,
                IReadOnlyList<int> keptIndices,
                IReadOnlyList<TimeInterval> constantIntervals)
            {
                var rotations = new Quaternion[keptIndices.Count];
                for (var index = 0; index < keptIndices.Count; index++)
                {
                    var rotation = poses[keptIndices[index]].Rotation;
                    if (index > 0)
                    {
                        rotation = MatchQuaternionHemisphere(rotations[index - 1], rotation);
                    }
                    rotations[index] = rotation;
                }

                SetCurve(
                    output,
                    "m_LocalRotation.x",
                    times,
                    keptIndices,
                    constantIntervals,
                    index => rotations[index].x);
                SetCurve(
                    output,
                    "m_LocalRotation.y",
                    times,
                    keptIndices,
                    constantIntervals,
                    index => rotations[index].y);
                SetCurve(
                    output,
                    "m_LocalRotation.z",
                    times,
                    keptIndices,
                    constantIntervals,
                    index => rotations[index].z);
                SetCurve(
                    output,
                    "m_LocalRotation.w",
                    times,
                    keptIndices,
                    constantIntervals,
                    index => rotations[index].w);

                if (!_forcePosition
                    && !poses.Any(pose =>
                        (pose.Position - _bindPosition).sqrMagnitude > PositionEpsilonSquared))
                {
                    return;
                }

                SetCurve(
                    output,
                    "m_LocalPosition.x",
                    times,
                    keptIndices,
                    constantIntervals,
                    index => poses[keptIndices[index]].Position.x);
                SetCurve(
                    output,
                    "m_LocalPosition.y",
                    times,
                    keptIndices,
                    constantIntervals,
                    index => poses[keptIndices[index]].Position.y);
                SetCurve(
                    output,
                    "m_LocalPosition.z",
                    times,
                    keptIndices,
                    constantIntervals,
                    index => poses[keptIndices[index]].Position.z);
            }

            private void SetCurve(
                AnimationClip output,
                string propertyName,
                IReadOnlyList<float> times,
                IReadOnlyList<int> keptIndices,
                IReadOnlyList<TimeInterval> constantIntervals,
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
                        && IsConstantSegment(keys[index].time, keys[index + 1].time, constantIntervals)
                            ? AnimationUtility.TangentMode.Constant
                            : AnimationUtility.TangentMode.Linear);
                }

                AnimationUtility.SetEditorCurve(
                    output,
                    EditorCurveBinding.FloatCurve(
                        _path,
                        typeof(Transform),
                        propertyName),
                    curve);
            }
        }
    }

    public sealed class PhantomHumanoidClipBakeResult
    {
        public AnimationClip Clip { get; }
        public float SampleRate { get; }
        public int ResolvedHumanoidBindingCount { get; }
        public IReadOnlyList<HumanBodyBones> BakedBones { get; }
        public IReadOnlyList<HumanBodyBones> MissingBones { get; }
        public IReadOnlyList<EditorCurveBinding> SkippedAnimatorBindings { get; }
        public bool RootMotionLocalized { get; }
        public IReadOnlyList<EditorCurveBinding> IgnoredRootScaleBindings { get; }
        public PhantomHumanoidSamplingMode SamplingMode { get; }
        public int SourceCandidateTimeCount { get; }
        public int AdaptiveSampleCount { get; }
        public int UnsimplifiedPoseKeyCount { get; }
        public int OutputPoseKeyCount { get; }
        public bool HitSampleRateLimit { get; }

        internal PhantomHumanoidClipBakeResult(
            AnimationClip clip,
            float sampleRate,
            int resolvedHumanoidBindingCount,
            IReadOnlyList<HumanBodyBones> bakedBones,
            IReadOnlyList<HumanBodyBones> missingBones,
            IReadOnlyList<EditorCurveBinding> skippedAnimatorBindings,
            bool rootMotionLocalized,
            IReadOnlyList<EditorCurveBinding> ignoredRootScaleBindings,
            PhantomHumanoidSamplingMode samplingMode,
            int sourceCandidateTimeCount,
            int adaptiveSampleCount,
            int unsimplifiedPoseKeyCount,
            int outputPoseKeyCount,
            bool hitSampleRateLimit)
        {
            Clip = clip;
            SampleRate = sampleRate;
            ResolvedHumanoidBindingCount = resolvedHumanoidBindingCount;
            BakedBones = bakedBones;
            MissingBones = missingBones;
            SkippedAnimatorBindings = skippedAnimatorBindings;
            RootMotionLocalized = rootMotionLocalized;
            IgnoredRootScaleBindings = ignoredRootScaleBindings;
            SamplingMode = samplingMode;
            SourceCandidateTimeCount = sourceCandidateTimeCount;
            AdaptiveSampleCount = adaptiveSampleCount;
            UnsimplifiedPoseKeyCount = unsimplifiedPoseKeyCount;
            OutputPoseKeyCount = outputPoseKeyCount;
            HitSampleRateLimit = hitSampleRateLimit;
        }
    }

    public enum PhantomHumanoidSamplingMode
    {
        Fixed,
        Adaptive
    }

    public sealed class PhantomHumanoidClipBakeOptions
    {
        public PhantomHumanoidSamplingMode SamplingMode { get; set; } =
            PhantomHumanoidSamplingMode.Fixed;
        public float SampleRate { get; set; }
        public float PositionErrorTolerance { get; set; } = 0.0005f;
        public float RotationErrorToleranceDegrees { get; set; } = 0.25f;
        public bool LocalizeRootMotionToHips { get; set; } = true;
        public bool InheritedMirror { get; set; }
        public ISet<string> AnimatorParameterNames { get; set; }
        public IReadOnlyDictionary<HumanBodyBones, string> OutputBonePaths { get; set; }
        public IReadOnlyDictionary<HumanBodyBones, string> OutputBoneParentPaths { get; set; }
    }
}
