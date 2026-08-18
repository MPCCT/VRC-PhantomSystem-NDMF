using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    internal readonly struct PhantomPose
    {
        internal Vector3 Position { get; }
        internal Quaternion Rotation { get; }

        internal PhantomPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    internal readonly struct PhantomTimeInterval
    {
        internal float Start { get; }
        internal float End { get; }

        internal PhantomTimeInterval(float start, float end)
        {
            Start = start;
            End = end;
        }
    }

    internal sealed class PhantomPoseSamplingResult
    {
        internal IReadOnlyList<float> Times { get; }
        internal IReadOnlyList<PhantomPose[]> PosesByTrack { get; }
        internal IReadOnlyList<IReadOnlyList<int>> KeptIndicesByTrack { get; }
        internal int SourceCandidateTimeCount { get; }
        internal int AdaptiveSampleCount { get; }
        internal int UnsimplifiedPoseKeyCount { get; }
        internal int OutputPoseKeyCount { get; }
        internal bool HitSampleRateLimit { get; }

        internal PhantomPoseSamplingResult(
            IReadOnlyList<float> times,
            IReadOnlyList<PhantomPose[]> posesByTrack,
            IReadOnlyList<IReadOnlyList<int>> keptIndicesByTrack,
            int sourceCandidateTimeCount,
            int adaptiveSampleCount,
            bool hitSampleRateLimit)
        {
            Times = times;
            PosesByTrack = posesByTrack;
            KeptIndicesByTrack = keptIndicesByTrack;
            SourceCandidateTimeCount = sourceCandidateTimeCount;
            AdaptiveSampleCount = adaptiveSampleCount;
            UnsimplifiedPoseKeyCount = times.Count * posesByTrack.Count;
            OutputPoseKeyCount = keptIndicesByTrack.Sum(indices => indices.Count);
            HitSampleRateLimit = hitSampleRateLimit;
        }
    }

    /// <summary>
    /// Refines and simplifies already-defined pose tracks. The caller supplies the pose
    /// evaluator, so this class has no dependency on a Unity scene, Animator, or clip.
    /// </summary>
    internal static class PhantomAdaptivePoseSampler
    {
        internal const float TimeEpsilon = 0.000001f;

        internal static PhantomPoseSamplingResult SampleFixed(
            IReadOnlyList<float> sourceTimes,
            int trackCount,
            Func<float, PhantomPose[]> sample)
        {
            ValidateArguments(sourceTimes, trackCount, sample);
            var samples = SampleInitialTimes(sourceTimes, trackCount, sample);
            return CreateResult(
                sourceTimes.Count,
                samples,
                trackCount,
                null,
                0f,
                0f,
                false,
                0,
                false);
        }

        internal static PhantomPoseSamplingResult SampleAdaptive(
            IReadOnlyList<float> sourceTimes,
            int trackCount,
            Func<float, PhantomPose[]> sample,
            IReadOnlyList<IReadOnlyList<PhantomTimeInterval>> constantIntervalsByTrack,
            float maximumSampleRate,
            float positionTolerance,
            float rotationToleranceDegrees)
        {
            ValidateArguments(sourceTimes, trackCount, sample);
            if (constantIntervalsByTrack == null)
            {
                throw new ArgumentNullException(nameof(constantIntervalsByTrack));
            }
            if (constantIntervalsByTrack.Count != trackCount)
            {
                throw new ArgumentException(
                    "Constant interval track count must match the sampled pose track count.",
                    nameof(constantIntervalsByTrack));
            }

            var samples = SampleInitialTimes(sourceTimes, trackCount, sample);
            var adaptiveSampleCount = 0;
            var hitSampleRateLimit = false;
            for (var index = 0; index + 1 < sourceTimes.Count; index++)
            {
                SubdivideAdaptive(
                    sourceTimes[index],
                    sourceTimes[index + 1],
                    trackCount,
                    constantIntervalsByTrack,
                    samples,
                    time => AddSample(samples, time, trackCount, sample),
                    maximumSampleRate,
                    positionTolerance,
                    rotationToleranceDegrees,
                    ref adaptiveSampleCount,
                    ref hitSampleRateLimit);
            }

            return CreateResult(
                sourceTimes.Count,
                samples,
                trackCount,
                constantIntervalsByTrack,
                positionTolerance,
                rotationToleranceDegrees,
                true,
                adaptiveSampleCount,
                hitSampleRateLimit);
        }

        internal static Quaternion MatchQuaternionHemisphere(
            Quaternion reference,
            Quaternion value)
        {
            return Quaternion.Dot(reference, value) < 0f
                ? new Quaternion(-value.x, -value.y, -value.z, -value.w)
                : value;
        }

        internal static Quaternion NormalizeQuaternion(Quaternion value)
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

        internal static bool IsConstantSegment(
            float start,
            float end,
            IReadOnlyList<PhantomTimeInterval> intervals)
        {
            return intervals != null && intervals.Any(interval =>
                start >= interval.Start - TimeEpsilon
                && end <= interval.End + TimeEpsilon);
        }

        private static void ValidateArguments(
            IReadOnlyList<float> sourceTimes,
            int trackCount,
            Func<float, PhantomPose[]> sample)
        {
            if (sourceTimes == null)
            {
                throw new ArgumentNullException(nameof(sourceTimes));
            }
            if (sourceTimes.Count == 0)
            {
                throw new ArgumentException("At least one source sample time is required.", nameof(sourceTimes));
            }
            if (trackCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(trackCount));
            }
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }
            for (var index = 1; index < sourceTimes.Count; index++)
            {
                if (sourceTimes[index] <= sourceTimes[index - 1])
                {
                    throw new ArgumentException(
                        "Source sample times must be strictly increasing.",
                        nameof(sourceTimes));
                }
            }
        }

        private static SortedDictionary<float, PhantomPose[]> SampleInitialTimes(
            IReadOnlyList<float> sourceTimes,
            int trackCount,
            Func<float, PhantomPose[]> sample)
        {
            var samples = new SortedDictionary<float, PhantomPose[]>();
            foreach (var time in sourceTimes)
            {
                AddSample(samples, time, trackCount, sample);
            }
            return samples;
        }

        private static void AddSample(
            IDictionary<float, PhantomPose[]> samples,
            float time,
            int trackCount,
            Func<float, PhantomPose[]> sample)
        {
            if (samples.ContainsKey(time))
            {
                return;
            }

            var poses = sample(time);
            if (poses == null || poses.Length != trackCount)
            {
                throw new InvalidOperationException(
                    $"Pose evaluator returned {poses?.Length ?? 0} tracks; expected {trackCount}.");
            }
            samples.Add(time, poses);
        }

        private static PhantomPoseSamplingResult CreateResult(
            int sourceCandidateTimeCount,
            SortedDictionary<float, PhantomPose[]> samples,
            int trackCount,
            IReadOnlyList<IReadOnlyList<PhantomTimeInterval>> constantIntervalsByTrack,
            float positionTolerance,
            float rotationToleranceDegrees,
            bool simplify,
            int adaptiveSampleCount,
            bool hitSampleRateLimit)
        {
            var times = samples.Keys.ToList();
            var posesByTrack = new List<PhantomPose[]>(trackCount);
            var keptIndicesByTrack = new List<IReadOnlyList<int>>(trackCount);
            for (var trackIndex = 0; trackIndex < trackCount; trackIndex++)
            {
                var poses = times.Select(time => samples[time][trackIndex]).ToArray();
                posesByTrack.Add(poses);
                keptIndicesByTrack.Add(simplify
                    ? SimplifyTrack(
                        times,
                        poses,
                        constantIntervalsByTrack[trackIndex],
                        positionTolerance,
                        rotationToleranceDegrees)
                    : Enumerable.Range(0, times.Count).ToArray());
            }

            return new PhantomPoseSamplingResult(
                times,
                posesByTrack,
                keptIndicesByTrack,
                sourceCandidateTimeCount,
                adaptiveSampleCount,
                hitSampleRateLimit);
        }

        private static void SubdivideAdaptive(
            float start,
            float end,
            int trackCount,
            IReadOnlyList<IReadOnlyList<PhantomTimeInterval>> constantIntervalsByTrack,
            SortedDictionary<float, PhantomPose[]> samples,
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
            for (var trackIndex = 0; trackIndex < trackCount; trackIndex++)
            {
                if (PoseErrorRatio(
                        startPoses[trackIndex],
                        endPoses[trackIndex],
                        midpointPoses[trackIndex],
                        0.5f,
                        positionTolerance,
                        rotationToleranceDegrees,
                        IsConstantSegment(
                            start,
                            end,
                            constantIntervalsByTrack[trackIndex])) > 1f)
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
                trackCount,
                constantIntervalsByTrack,
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
                trackCount,
                constantIntervalsByTrack,
                samples,
                sample,
                maximumSampleRate,
                positionTolerance,
                rotationToleranceDegrees,
                ref adaptiveSampleCount,
                ref hitSampleRateLimit);
        }

        private static IReadOnlyList<int> SimplifyTrack(
            IReadOnlyList<float> times,
            IReadOnlyList<PhantomPose> poses,
            IReadOnlyList<PhantomTimeInterval> constantIntervals,
            float positionTolerance,
            float rotationToleranceDegrees)
        {
            if (times.Count <= 2)
            {
                return Enumerable.Range(0, times.Count).ToArray();
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
            return kept.ToArray();
        }

        private static void SimplifyRange(
            int startIndex,
            int endIndex,
            IReadOnlyList<float> times,
            IReadOnlyList<PhantomPose> poses,
            float positionTolerance,
            float rotationToleranceDegrees,
            IReadOnlyList<PhantomTimeInterval> constantIntervals,
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
                    IsConstantSegment(times[startIndex], times[endIndex], constantIntervals));
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
            PhantomPose start,
            PhantomPose end,
            PhantomPose actual,
            float t,
            float positionTolerance,
            float rotationToleranceDegrees,
            bool constant)
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
    }
}
