using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Serializes pure pose data without persisting any Unity object references.</summary>
    internal static class PhantomHumanoidBakeCacheSerializer
    {
        internal const int SchemaVersion = 1;
        private const int Magic = 0x50484243; // PHBC
        private const int MaximumTrackCount = 256;
        private const int MaximumSampleCount = 1000000;
        private const long MaximumPoseCount = 20000000;

        internal static void Write(
            BinaryWriter writer,
            string key,
            PhantomHumanoidPoseBakeData data)
        {
            writer.Write(Magic);
            writer.Write(SchemaVersion);
            writer.Write(key);
            writer.Write(data.Tracks.Count);
            foreach (var track in data.Tracks)
            {
                writer.Write((int)track.Bone);
                writer.Write(track.Path ?? string.Empty);
                WriteVector3(writer, track.BindPosition);
                writer.Write(track.ForcePosition);
                writer.Write(track.ConstantIntervals.Count);
                foreach (var interval in track.ConstantIntervals)
                {
                    writer.Write(interval.Start);
                    writer.Write(interval.End);
                }
            }

            writer.Write(data.MissingBones.Count);
            foreach (var bone in data.MissingBones)
            {
                writer.Write((int)bone);
            }

            var sampling = data.Sampling;
            writer.Write(sampling.SourceCandidateTimeCount);
            writer.Write(sampling.AdaptiveSampleCount);
            writer.Write(sampling.HitSampleRateLimit);
            writer.Write(sampling.Times.Count);
            foreach (var time in sampling.Times)
            {
                writer.Write(time);
            }

            writer.Write(sampling.PosesByTrack.Count);
            foreach (var poses in sampling.PosesByTrack)
            {
                writer.Write(poses.Length);
                foreach (var pose in poses)
                {
                    WriteVector3(writer, pose.Position);
                    WriteQuaternion(writer, pose.Rotation);
                }
            }

            writer.Write(sampling.KeptIndicesByTrack.Count);
            foreach (var indices in sampling.KeptIndicesByTrack)
            {
                writer.Write(indices.Count);
                foreach (var index in indices)
                {
                    writer.Write(index);
                }
            }
        }

        internal static PhantomHumanoidPoseBakeData Read(BinaryReader reader, string expectedKey)
        {
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != SchemaVersion)
            {
                throw new InvalidDataException("The cache header is not supported.");
            }
            if (!string.Equals(reader.ReadString(), expectedKey, System.StringComparison.Ordinal))
            {
                throw new InvalidDataException("The cache key does not match its filename.");
            }

            var trackCount = ReadCount(reader, MaximumTrackCount, "track");
            var tracks = new PhantomHumanoidBoneTrack[trackCount];
            for (var index = 0; index < trackCount; index++)
            {
                var bone = ReadBone(reader);
                var path = reader.ReadString();
                var bindPosition = ReadVector3(reader);
                var forcePosition = reader.ReadBoolean();
                var intervalCount = ReadCount(reader, MaximumSampleCount, "constant interval");
                var intervals = new PhantomTimeInterval[intervalCount];
                for (var intervalIndex = 0; intervalIndex < intervalCount; intervalIndex++)
                {
                    intervals[intervalIndex] = new PhantomTimeInterval(
                        reader.ReadSingle(),
                        reader.ReadSingle());
                }
                tracks[index] = new PhantomHumanoidBoneTrack(
                    bone,
                    path,
                    bindPosition,
                    forcePosition,
                    intervals);
            }

            var missingCount = ReadCount(reader, MaximumTrackCount, "missing bone");
            var missingBones = new HumanBodyBones[missingCount];
            for (var index = 0; index < missingCount; index++)
            {
                missingBones[index] = ReadBone(reader);
            }

            var sourceCandidateCount = reader.ReadInt32();
            var adaptiveSampleCount = reader.ReadInt32();
            var hitSampleRateLimit = reader.ReadBoolean();
            if (sourceCandidateCount < 0 || adaptiveSampleCount < 0)
            {
                throw new InvalidDataException("The cache diagnostic counts are invalid.");
            }

            var timeCount = ReadCount(reader, MaximumSampleCount, "sample time");
            var times = new float[timeCount];
            for (var index = 0; index < timeCount; index++)
            {
                times[index] = reader.ReadSingle();
                if (!IsFinite(times[index]) || index > 0 && times[index] <= times[index - 1])
                {
                    throw new InvalidDataException("The cached sample timeline is invalid.");
                }
            }

            var poseTrackCount = ReadCount(reader, MaximumTrackCount, "pose track");
            if (poseTrackCount != trackCount || (long)poseTrackCount * timeCount > MaximumPoseCount)
            {
                throw new InvalidDataException("The cached pose dimensions are invalid.");
            }
            var posesByTrack = new PhantomPose[poseTrackCount][];
            for (var trackIndex = 0; trackIndex < poseTrackCount; trackIndex++)
            {
                var poseCount = ReadCount(reader, MaximumSampleCount, "pose");
                if (poseCount != timeCount)
                {
                    throw new InvalidDataException("A cached pose track does not match the timeline.");
                }
                var poses = new PhantomPose[poseCount];
                for (var poseIndex = 0; poseIndex < poseCount; poseIndex++)
                {
                    var position = ReadVector3(reader);
                    var rotation = ReadQuaternion(reader);
                    if (!IsFinite(position) || !IsFinite(rotation))
                    {
                        throw new InvalidDataException("A cached pose contains a non-finite value.");
                    }
                    poses[poseIndex] = new PhantomPose(position, rotation);
                }
                posesByTrack[trackIndex] = poses;
            }

            var keptTrackCount = ReadCount(reader, MaximumTrackCount, "kept-index track");
            if (keptTrackCount != trackCount)
            {
                throw new InvalidDataException("The cached reduction data has the wrong track count.");
            }
            var keptIndices = new IReadOnlyList<int>[keptTrackCount];
            for (var trackIndex = 0; trackIndex < keptTrackCount; trackIndex++)
            {
                var keptCount = ReadCount(reader, timeCount, "kept index");
                var indices = new int[keptCount];
                for (var index = 0; index < keptCount; index++)
                {
                    indices[index] = reader.ReadInt32();
                    if (indices[index] < 0
                        || indices[index] >= timeCount
                        || index > 0 && indices[index] <= indices[index - 1])
                    {
                        throw new InvalidDataException("A cached kept-index list is invalid.");
                    }
                }
                keptIndices[trackIndex] = indices;
            }

            var sampling = new PhantomPoseSamplingResult(
                times,
                posesByTrack,
                keptIndices,
                sourceCandidateCount,
                adaptiveSampleCount,
                hitSampleRateLimit);
            return new PhantomHumanoidPoseBakeData(tracks, sampling, missingBones);
        }

        private static int ReadCount(BinaryReader reader, int maximum, string name)
        {
            var value = reader.ReadInt32();
            if (value < 0 || value > maximum)
            {
                throw new InvalidDataException($"The cached {name} count is invalid.");
            }
            return value;
        }

        private static HumanBodyBones ReadBone(BinaryReader reader)
        {
            var value = reader.ReadInt32();
            if (value < 0 || value >= (int)HumanBodyBones.LastBone)
            {
                throw new InvalidDataException("The cache contains an invalid Humanoid bone.");
            }
            return (HumanBodyBones)value;
        }

        private static void WriteVector3(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
            writer.Write(value.w);
        }

        private static Quaternion ReadQuaternion(BinaryReader reader)
        {
            return new Quaternion(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }
    }
}
