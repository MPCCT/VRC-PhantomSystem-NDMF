using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor.Tests
{
    internal sealed class PhantomAdaptivePoseSamplerTests
    {
        [Test]
        public void LinearTrack_EvaluatesMidpointThenSimplifiesIt()
        {
            var result = Sample(
                time => Pose(new Vector3(time, 0f, 0f), Quaternion.identity),
                new[] { 0f, 1f });

            CollectionAssert.AreEqual(new[] { 0f, 0.5f, 1f }, result.Times);
            CollectionAssert.AreEqual(new[] { 0, 2 }, result.KeptIndicesByTrack[0]);
            Assert.AreEqual(1, result.AdaptiveSampleCount);
            Assert.AreEqual(3, result.UnsimplifiedPoseKeyCount);
            Assert.AreEqual(2, result.OutputPoseKeyCount);
            Assert.IsFalse(result.HitSampleRateLimit);
        }

        [Test]
        public void CurvedPosition_AddsAndKeepsRequiredSamples()
        {
            var result = Sample(
                time => Pose(new Vector3(time * time, 0f, 0f), Quaternion.identity),
                new[] { 0f, 1f },
                maximumSampleRate: 64f,
                positionTolerance: 0.01f);

            Assert.Greater(result.AdaptiveSampleCount, 1);
            Assert.Greater(result.KeptIndicesByTrack[0].Count, 2);
            Assert.IsFalse(result.HitSampleRateLimit);
        }

        [Test]
        public void CurvedRotation_AddsSamplesUsingAngularTolerance()
        {
            var result = Sample(
                time => Pose(
                    Vector3.zero,
                    RotationAroundY(90f * time * time)),
                new[] { 0f, 1f },
                maximumSampleRate: 64f,
                rotationTolerance: 0.5f);

            Assert.Greater(result.KeptIndicesByTrack[0].Count, 2);
            Assert.IsFalse(result.HitSampleRateLimit);
        }

        [Test]
        public void QuaternionHemisphereFlip_DoesNotCreateFalseRotationError()
        {
            var result = Sample(
                time => Pose(
                    Vector3.zero,
                    Mathf.Approximately(time, 0.5f)
                        ? new Quaternion(0f, 0f, 0f, -1f)
                        : Quaternion.identity),
                new[] { 0f, 1f });

            CollectionAssert.AreEqual(new[] { 0, 2 }, result.KeptIndicesByTrack[0]);
            Assert.IsFalse(result.HitSampleRateLimit);
        }

        [Test]
        public void ConstantInterval_ProtectsItsBoundaries()
        {
            var times = new[] { 0f, 0.25f, 0.75f, 1f };
            var result = PhantomAdaptivePoseSampler.SampleAdaptive(
                times,
                1,
                time => Pose(new Vector3(time, 0f, 0f), Quaternion.identity),
                new IReadOnlyList<PhantomTimeInterval>[]
                {
                    new[] { new PhantomTimeInterval(0.25f, 0.75f) }
                },
                64f,
                10f,
                180f);

            var keptTimes = result.KeptIndicesByTrack[0]
                .Select(index => result.Times[index])
                .ToArray();
            CollectionAssert.Contains(keptTimes, 0.25f);
            CollectionAssert.Contains(keptTimes, 0.75f);
        }

        [Test]
        public void UnresolvedMidpointError_ReportsSampleRateLimit()
        {
            var result = Sample(
                time => Pose(
                    Mathf.Approximately(time, 0.5f) ? Vector3.one : Vector3.zero,
                    Quaternion.identity),
                new[] { 0f, 1f },
                maximumSampleRate: 1f,
                positionTolerance: 0.001f);

            CollectionAssert.AreEqual(new[] { 0f, 1f }, result.Times);
            Assert.AreEqual(0, result.AdaptiveSampleCount);
            Assert.IsTrue(result.HitSampleRateLimit);
        }

        [Test]
        public void FixedSampling_KeepsAllSourceTimesAndDiagnostics()
        {
            var result = PhantomAdaptivePoseSampler.SampleFixed(
                new[] { 0f, 0.25f, 1f },
                1,
                time => Pose(new Vector3(time, 0f, 0f), Quaternion.identity));

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, result.KeptIndicesByTrack[0]);
            Assert.AreEqual(3, result.SourceCandidateTimeCount);
            Assert.AreEqual(0, result.AdaptiveSampleCount);
            Assert.AreEqual(3, result.UnsimplifiedPoseKeyCount);
            Assert.AreEqual(3, result.OutputPoseKeyCount);
        }

        private static PhantomPoseSamplingResult Sample(
            Func<float, PhantomPose[]> evaluator,
            IReadOnlyList<float> sourceTimes,
            float maximumSampleRate = 30f,
            float positionTolerance = 0.0005f,
            float rotationTolerance = 0.25f)
        {
            return PhantomAdaptivePoseSampler.SampleAdaptive(
                sourceTimes,
                1,
                evaluator,
                new IReadOnlyList<PhantomTimeInterval>[]
                {
                    Array.Empty<PhantomTimeInterval>()
                },
                maximumSampleRate,
                positionTolerance,
                rotationTolerance);
        }

        private static PhantomPose[] Pose(Vector3 position, Quaternion rotation)
        {
            return new[] { new PhantomPose(position, rotation) };
        }

        private static Quaternion RotationAroundY(float degrees)
        {
            var halfRadians = degrees * Mathf.Deg2Rad * 0.5f;
            return new Quaternion(0f, Mathf.Sin(halfRadians), 0f, Mathf.Cos(halfRadians));
        }
    }
}
