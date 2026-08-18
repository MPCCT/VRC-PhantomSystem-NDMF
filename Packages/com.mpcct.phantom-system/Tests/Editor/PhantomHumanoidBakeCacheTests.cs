using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MPCCT.PhantomSystem.Editor.Tests
{
    internal sealed class PhantomHumanoidBakeCacheTests
    {
        private string cacheRoot;

        [SetUp]
        public void SetUp()
        {
            cacheRoot = Path.Combine(
                Path.GetTempPath(),
                "PhantomSystemHumanoidBakeCacheTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            PhantomHumanoidBakeCacheSession.ClearAll(cacheRoot, out _);
        }

        [Test]
        public void StoredPoseData_LoadsAcrossSessionsWithoutUnityObjects()
        {
            const string key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var source = CreatePoseData();
            var writer = new PhantomHumanoidBakeCacheSession(cacheRoot);

            Assert.IsFalse(writer.TryLoad(key, out _));
            writer.Store(key, source);
            Assert.AreEqual(1, writer.MissCount);
            Assert.AreEqual(0, writer.WriteFailureCount);

            var reader = new PhantomHumanoidBakeCacheSession(cacheRoot);
            Assert.IsTrue(reader.TryLoad(key, out var loaded));
            Assert.AreEqual(1, reader.HitCount);
            AssertPoseDataEqual(source, loaded);

            var statistics = PhantomHumanoidBakeCacheSession.GetStatistics(cacheRoot);
            Assert.AreEqual(1, statistics.EntryCount);
            Assert.Greater(statistics.Bytes, 0L);
        }

        [Test]
        public void DamagedEntry_IsDeletedAndTreatedAsMiss()
        {
            const string key = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            var writer = new PhantomHumanoidBakeCacheSession(cacheRoot);
            writer.Store(key, CreatePoseData());
            var file = Directory.GetFiles(cacheRoot, "*.bin", SearchOption.AllDirectories)[0];
            File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4 });

            var reader = new PhantomHumanoidBakeCacheSession(cacheRoot);
            LogAssert.Expect(
                LogType.Warning,
                new Regex("Ignored a damaged Humanoid bake cache entry"));
            Assert.IsFalse(reader.TryLoad(key, out _));
            Assert.AreEqual(1, reader.MissCount);
            Assert.IsFalse(File.Exists(file));
        }

        [Test]
        public void NewSession_RemovesIncompatibleSchemaAndLegacyVersionDirectories()
        {
            var obsoleteSchema = Path.Combine(cacheRoot, "v0", "OldUnity");
            var obsoleteUnity = Path.Combine(cacheRoot, "v1", "OldUnity");
            Directory.CreateDirectory(obsoleteSchema);
            Directory.CreateDirectory(obsoleteUnity);
            File.WriteAllText(Path.Combine(obsoleteSchema, "old.bin"), "old");
            File.WriteAllText(Path.Combine(obsoleteUnity, "old.bin"), "old");

            _ = new PhantomHumanoidBakeCacheSession(cacheRoot);

            Assert.IsFalse(Directory.Exists(Path.Combine(cacheRoot, "v0")));
            Assert.IsFalse(Directory.Exists(obsoleteUnity));
        }

        [Test]
        public void CacheKey_TracksPoseInputsButIgnoresNonHumanoidCurves()
        {
            var root = new GameObject("Avatar");
            var child = new GameObject("Bone");
            child.transform.SetParent(root.transform, false);
            var avatar = AvatarBuilder.BuildGenericAvatar(root, string.Empty);
            var clip = new AnimationClip { frameRate = 30f };
            clip.SetCurve(
                string.Empty,
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0f, 0f, 1f, 1f));
            try
            {
                var original = CreateKey(clip, avatar, root.transform, 0.0005f, false);

                clip.SetCurve(
                    "Body",
                    typeof(SkinnedMeshRenderer),
                    "blendShape.Test",
                    AnimationCurve.Linear(0f, 0f, 1f, 100f));
                var nonHumanoidChanged = CreateKey(
                    clip,
                    avatar,
                    root.transform,
                    0.0005f,
                    false);
                Assert.AreEqual(original, nonHumanoidChanged);

                clip.frameRate = 60f;
                Assert.AreEqual(
                    original,
                    CreateKey(clip, avatar, root.transform, 0.0005f, false));
                clip.frameRate = 30f;

                clip.SetCurve(
                    string.Empty,
                    typeof(Transform),
                    "m_LocalPosition.x",
                    AnimationCurve.Linear(0f, 0f, 1f, 2f));
                Assert.AreNotEqual(
                    original,
                    CreateKey(clip, avatar, root.transform, 0.0005f, false));

                clip.SetCurve(
                    string.Empty,
                    typeof(Transform),
                    "m_LocalPosition.x",
                    AnimationCurve.Linear(0f, 0f, 1f, 1f));
                root.name = "RenamedAvatarRoot";
                Assert.AreEqual(
                    original,
                    CreateKey(clip, avatar, root.transform, 0.0005f, false));
                root.name = "Avatar";

                Assert.AreNotEqual(
                    original,
                    CreateKey(clip, avatar, root.transform, 0.001f, false));
                Assert.AreNotEqual(
                    original,
                    CreateKey(clip, avatar, root.transform, 0.0005f, true));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static PhantomHumanoidPoseBakeData CreatePoseData()
        {
            var intervals = new[] { new PhantomTimeInterval(0.5f, 1f) };
            var tracks = new[]
            {
                new PhantomHumanoidBoneTrack(
                    HumanBodyBones.Hips,
                    "Armature/PhantomAnimationDriver/Hips",
                    new Vector3(0f, 1f, 0f),
                    true,
                    intervals)
            };
            var poses = new[]
            {
                new PhantomPose(new Vector3(0f, 1f, 0f), Quaternion.identity),
                new PhantomPose(new Vector3(0.1f, 1.1f, 0f), Quaternion.Euler(0f, 10f, 0f)),
                new PhantomPose(new Vector3(0.2f, 1.2f, 0f), Quaternion.Euler(0f, 20f, 0f))
            };
            var sampling = new PhantomPoseSamplingResult(
                new[] { 0f, 0.5f, 1f },
                new[] { poses },
                new IReadOnlyList<int>[] { new[] { 0, 2 } },
                2,
                1,
                false);
            return new PhantomHumanoidPoseBakeData(
                tracks,
                sampling,
                new[] { HumanBodyBones.LeftEye });
        }

        private string CreateKey(
            AnimationClip clip,
            Avatar avatar,
            Transform root,
            float positionTolerance,
            bool inheritedMirror)
        {
            var options = new PhantomHumanoidClipBakeOptions
            {
                SamplingMode = PhantomHumanoidSamplingMode.Adaptive,
                SampleRate = 30f,
                PositionErrorTolerance = positionTolerance,
                RotationErrorToleranceDegrees = 0.25f,
                LocalizeRootMotionToHips = true,
                InheritedMirror = inheritedMirror,
                OutputBonePaths = new Dictionary<HumanBodyBones, string>
                {
                    [HumanBodyBones.Hips] = "Driver/Hips"
                },
                OutputBoneParentPaths = new Dictionary<HumanBodyBones, string>
                {
                    [HumanBodyBones.Hips] = string.Empty
                }
            };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            var effectiveMirror = PhantomHumanoidBindingUtility.ResolveEffectiveMirror(
                settings.mirror,
                inheritedMirror);
            var analysis = PhantomHumanoidClipAnalyzer.Analyze(
                clip,
                options,
                effectiveMirror);
            var session = new PhantomHumanoidBakeCacheSession(cacheRoot);
            return session.TryCreateKey(
                clip,
                avatar,
                root,
                30f,
                positionTolerance,
                0.25f,
                analysis,
                options,
                effectiveMirror);
        }

        private static void AssertPoseDataEqual(
            PhantomHumanoidPoseBakeData expected,
            PhantomHumanoidPoseBakeData actual)
        {
            Assert.AreEqual(expected.Tracks.Count, actual.Tracks.Count);
            Assert.AreEqual(expected.Tracks[0].Bone, actual.Tracks[0].Bone);
            Assert.AreEqual(expected.Tracks[0].Path, actual.Tracks[0].Path);
            Assert.AreEqual(expected.Tracks[0].BindPosition, actual.Tracks[0].BindPosition);
            Assert.AreEqual(expected.Tracks[0].ForcePosition, actual.Tracks[0].ForcePosition);
            Assert.AreEqual(expected.Tracks[0].ConstantIntervals.Count, actual.Tracks[0].ConstantIntervals.Count);
            CollectionAssert.AreEqual(expected.MissingBones, actual.MissingBones);
            CollectionAssert.AreEqual(expected.Sampling.Times, actual.Sampling.Times);
            CollectionAssert.AreEqual(
                expected.Sampling.KeptIndicesByTrack[0],
                actual.Sampling.KeptIndicesByTrack[0]);
            Assert.AreEqual(
                expected.Sampling.PosesByTrack[0].Length,
                actual.Sampling.PosesByTrack[0].Length);
            for (var index = 0; index < expected.Sampling.PosesByTrack[0].Length; index++)
            {
                Assert.AreEqual(
                    expected.Sampling.PosesByTrack[0][index].Position,
                    actual.Sampling.PosesByTrack[0][index].Position);
                Assert.AreEqual(
                    expected.Sampling.PosesByTrack[0][index].Rotation,
                    actual.Sampling.PosesByTrack[0][index].Rotation);
            }
            Assert.AreEqual(
                expected.Sampling.SourceCandidateTimeCount,
                actual.Sampling.SourceCandidateTimeCount);
            Assert.AreEqual(
                expected.Sampling.AdaptiveSampleCount,
                actual.Sampling.AdaptiveSampleCount);
            Assert.AreEqual(
                expected.Sampling.HitSampleRateLimit,
                actual.Sampling.HitSampleRateLimit);
        }
    }
}
