using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>
    /// Converts the humanoid portion of one animation clip into ordinary transform curves
    /// targeting a specific humanoid hierarchy. Controller integration is intentionally
    /// outside the scope of this class.
    /// </summary>
    public static class PhantomHumanoidClipBaker
    {
        private const float DefaultSampleRate = 60f;
        private const float DefaultPositionTolerance = 0.0005f;
        private const float DefaultRotationToleranceDegrees = 0.25f;

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
            var sampleRate = NormalizeSampleRate(options.SampleRate, source.frameRate);
            var positionTolerance = NormalizeTolerance(
                options.PositionErrorTolerance,
                DefaultPositionTolerance);
            var rotationToleranceDegrees = NormalizeTolerance(
                options.RotationErrorToleranceDegrees,
                DefaultRotationToleranceDegrees);
            var sourceSettings = AnimationUtility.GetAnimationClipSettings(source);
            var effectiveMirror = PhantomHumanoidBindingUtility.ResolveEffectiveMirror(
                sourceSettings.mirror,
                options.InheritedMirror);
            var analysis = PhantomHumanoidClipAnalyzer.Analyze(
                source,
                options,
                effectiveMirror);

            var output = PhantomHumanoidCurveWriter.CreateOutputClip(source, sampleRate);
            PhantomHumanoidCurveWriter.CopyNonHumanoidCurves(
                source,
                output,
                options.LocalizeRootMotionToHips,
                options.AnimatorParameterNames);

            PhantomHumanoidPoseBakeData poseData = null;
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
                    evaluationSettings.mirror = PhantomHumanoidBindingUtility.ResolveEffectiveMirror(
                        evaluationSettings.mirror,
                        true);
                    AnimationUtility.SetAnimationClipSettings(
                        mirroredEvaluationClip,
                        evaluationSettings);
                    evaluationClip = mirroredEvaluationClip;
                }

                if (analysis.AffectedBones.Count > 0)
                {
                    poseData = PhantomHumanoidPoseSampler.Sample(
                        evaluationClip,
                        sourceAnimator.avatar,
                        humanoidRoot.transform,
                        sampleRate,
                        analysis.AffectedBones,
                        analysis.ForcePositionBones,
                        options.LocalizeRootMotionToHips && analysis.HasRootMotion,
                        options,
                        positionTolerance,
                        rotationToleranceDegrees,
                        analysis.RelevantBindings,
                        effectiveMirror);
                    PhantomHumanoidCurveWriter.WritePoseCurves(output, poseData);
                    PhantomHumanoidCurveWriter.WriteMissingNeutralRotationCurves(
                        output,
                        analysis.ExplicitlyAnimatedBones,
                        options.NeutralRotationCompletionBones,
                        options.OutputBonePaths,
                        options.NeutralBoneRotations);
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
            var sampling = poseData?.Sampling;
            return new PhantomHumanoidClipBakeResult(
                output,
                sampleRate,
                analysis.ResolvedHumanoidBindingCount,
                poseData == null
                    ? Array.Empty<HumanBodyBones>()
                    : GetBakedBones(poseData),
                poseData?.MissingBones ?? Array.Empty<HumanBodyBones>(),
                analysis.SkippedAnimatorBindings,
                options.LocalizeRootMotionToHips && analysis.HasRootMotion,
                analysis.IgnoredRootScaleBindings,
                options.SamplingMode,
                sampling?.SourceCandidateTimeCount ?? 0,
                sampling?.AdaptiveSampleCount ?? 0,
                sampling?.UnsimplifiedPoseKeyCount ?? 0,
                sampling?.OutputPoseKeyCount ?? 0,
                sampling?.HitSampleRateLimit ?? false);
        }

        private static IReadOnlyList<HumanBodyBones> GetBakedBones(
            PhantomHumanoidPoseBakeData poseData)
        {
            var bones = new HumanBodyBones[poseData.Tracks.Count];
            for (var index = 0; index < poseData.Tracks.Count; index++)
            {
                bones[index] = poseData.Tracks[index].Bone;
            }
            return bones;
        }

        private static float NormalizeSampleRate(float requested, float sourceRate)
        {
            var sampleRate = requested;
            if (sampleRate <= 0f)
            {
                sampleRate = sourceRate > 0f ? sourceRate : DefaultSampleRate;
            }
            return float.IsNaN(sampleRate) || float.IsInfinity(sampleRate)
                ? DefaultSampleRate
                : sampleRate;
        }

        private static float NormalizeTolerance(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value <= 0f
                ? fallback
                : value;
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
        public ISet<HumanBodyBones> NeutralRotationCompletionBones { get; set; }
        public IReadOnlyDictionary<HumanBodyBones, Quaternion> NeutralBoneRotations { get; set; }
    }
}
