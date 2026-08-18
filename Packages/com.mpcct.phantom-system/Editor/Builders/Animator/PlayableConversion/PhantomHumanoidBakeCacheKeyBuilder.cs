using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Builds a content key from every input that can affect sampled Humanoid poses.</summary>
    internal static class PhantomHumanoidBakeCacheKeyBuilder
    {
        internal static string Create(
            AnimationClip source,
            Avatar avatar,
            Transform sourceRoot,
            float sampleRate,
            float positionTolerance,
            float rotationToleranceDegrees,
            PhantomHumanoidClipAnalysis analysis,
            PhantomHumanoidClipBakeOptions options,
            bool effectiveMirror)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(source.length);
                writer.Write(source.legacy);
                writer.Write((int)source.wrapMode);
                writer.Write(sampleRate);
                writer.Write(positionTolerance);
                writer.Write(rotationToleranceDegrees);
                writer.Write(options.LocalizeRootMotionToHips);
                writer.Write(effectiveMirror);

                WriteClipSettings(writer, AnimationUtility.GetAnimationClipSettings(source));
                WriteBindings(writer, source, analysis.RelevantBindings);
                WriteBoneSet(writer, analysis.AffectedBones);
                WriteBoneSet(writer, analysis.ForcePositionBones);
                WriteBoneStringMap(writer, options.OutputBonePaths);
                WriteAvatar(writer, avatar);
                WriteSemanticRig(writer, sourceRoot, avatar, options.OutputBoneParentPaths);
                writer.Flush();

                using (var sha256 = SHA256.Create())
                {
                    return ToHex(sha256.ComputeHash(stream.ToArray()));
                }
            }
        }

        private static void WriteClipSettings(BinaryWriter writer, AnimationClipSettings settings)
        {
            writer.Write(settings.startTime);
            writer.Write(settings.stopTime);
            writer.Write(settings.orientationOffsetY);
            writer.Write(settings.level);
            writer.Write(settings.cycleOffset);
            writer.Write(settings.loopTime);
            writer.Write(settings.loopBlend);
            writer.Write(settings.loopBlendOrientation);
            writer.Write(settings.loopBlendPositionY);
            writer.Write(settings.loopBlendPositionXZ);
            writer.Write(settings.keepOriginalOrientation);
            writer.Write(settings.keepOriginalPositionY);
            writer.Write(settings.keepOriginalPositionXZ);
            writer.Write(settings.heightFromFeet);
        }

        private static void WriteBindings(
            BinaryWriter writer,
            AnimationClip source,
            IEnumerable<EditorCurveBinding> bindings)
        {
            var ordered = (bindings ?? Enumerable.Empty<EditorCurveBinding>())
                .OrderBy(binding => binding.type?.FullName, StringComparer.Ordinal)
                .ThenBy(binding => binding.path, StringComparer.Ordinal)
                .ThenBy(binding => binding.propertyName, StringComparer.Ordinal)
                .ToArray();
            writer.Write(ordered.Length);
            foreach (var binding in ordered)
            {
                writer.Write(binding.type?.AssemblyQualifiedName ?? string.Empty);
                writer.Write(binding.path ?? string.Empty);
                writer.Write(binding.propertyName ?? string.Empty);
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                writer.Write(curve != null);
                if (curve == null)
                {
                    continue;
                }

                writer.Write((int)curve.preWrapMode);
                writer.Write((int)curve.postWrapMode);
                writer.Write(curve.length);
                foreach (var key in curve.keys)
                {
                    writer.Write(key.time);
                    writer.Write(key.value);
                    writer.Write(key.inTangent);
                    writer.Write(key.outTangent);
                    writer.Write(key.inWeight);
                    writer.Write(key.outWeight);
                    writer.Write((int)key.weightedMode);
                }
            }
        }

        private static void WriteBoneSet(BinaryWriter writer, IEnumerable<HumanBodyBones> bones)
        {
            var ordered = (bones ?? Enumerable.Empty<HumanBodyBones>())
                .Distinct()
                .OrderBy(bone => (int)bone)
                .ToArray();
            writer.Write(ordered.Length);
            foreach (var bone in ordered)
            {
                writer.Write((int)bone);
            }
        }

        private static void WriteBoneStringMap(
            BinaryWriter writer,
            IReadOnlyDictionary<HumanBodyBones, string> map)
        {
            if (map == null)
            {
                writer.Write(0);
                return;
            }

            var ordered = map.OrderBy(pair => (int)pair.Key).ToArray();
            writer.Write(ordered.Length);
            foreach (var pair in ordered)
            {
                writer.Write((int)pair.Key);
                writer.Write(pair.Value ?? string.Empty);
            }
        }

        private static void WriteAvatar(BinaryWriter writer, Avatar avatar)
        {
            var description = avatar.humanDescription;
            writer.Write(description.upperArmTwist);
            writer.Write(description.lowerArmTwist);
            writer.Write(description.upperLegTwist);
            writer.Write(description.lowerLegTwist);
            writer.Write(description.armStretch);
            writer.Write(description.legStretch);
            writer.Write(description.feetSpacing);
            writer.Write(description.hasTranslationDoF);

            var human = (description.human ?? Array.Empty<HumanBone>())
                .OrderBy(bone => bone.humanName, StringComparer.Ordinal)
                .ToArray();
            writer.Write(human.Length);
            foreach (var bone in human)
            {
                writer.Write(bone.humanName ?? string.Empty);
                WriteVector3(writer, bone.limit.min);
                WriteVector3(writer, bone.limit.max);
                WriteVector3(writer, bone.limit.center);
                writer.Write(bone.limit.axisLength);
                writer.Write(bone.limit.useDefaultValues);
            }

            var skeleton = description.skeleton ?? Array.Empty<SkeletonBone>();
            writer.Write(skeleton.Length);
            foreach (var bone in skeleton)
            {
                WriteVector3(writer, bone.position);
                WriteQuaternion(writer, bone.rotation);
                WriteVector3(writer, bone.scale);
            }
        }

        private static void WriteSemanticRig(
            BinaryWriter writer,
            Transform sourceRoot,
            Avatar avatar,
            IReadOnlyDictionary<HumanBodyBones, string> poseParentPaths)
        {
            var animator = sourceRoot.GetComponent<Animator>();
            var hasHumanoidAnimator = animator != null
                                      && animator.avatar == avatar
                                      && animator.isHuman;
            writer.Write(hasHumanoidAnimator);
            if (!hasHumanoidAnimator)
            {
                writer.Write(0);
                WritePoseParents(writer, sourceRoot, poseParentPaths);
                return;
            }

            writer.Write(animator.humanScale);
            var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                .Select(value => (HumanBodyBones)value)
                .Select(bone => new { Bone = bone, Transform = animator.GetBoneTransform(bone) })
                .Where(item => item.Transform != null)
                .ToArray();
            writer.Write(bones.Length);
            foreach (var item in bones)
            {
                writer.Write((int)item.Bone);
                WriteTransformChain(writer, item.Transform, sourceRoot);
            }

            WritePoseParents(writer, sourceRoot, poseParentPaths);
        }

        private static void WritePoseParents(
            BinaryWriter writer,
            Transform sourceRoot,
            IReadOnlyDictionary<HumanBodyBones, string> poseParentPaths)
        {
            if (poseParentPaths == null)
            {
                writer.Write(0);
                return;
            }

            var ordered = poseParentPaths.OrderBy(pair => (int)pair.Key).ToArray();
            writer.Write(ordered.Length);
            foreach (var pair in ordered)
            {
                writer.Write((int)pair.Key);
                var poseParent = string.IsNullOrEmpty(pair.Value)
                    ? sourceRoot
                    : sourceRoot.Find(pair.Value);
                writer.Write(poseParent != null);
                if (poseParent != null)
                {
                    WriteTransformChain(writer, poseParent, sourceRoot);
                }
            }
        }

        private static void WriteTransformChain(
            BinaryWriter writer,
            Transform target,
            Transform sourceRoot)
        {
            var chain = new Stack<Transform>();
            for (var current = target; current != null && current != sourceRoot; current = current.parent)
            {
                chain.Push(current);
            }

            var belongsToRoot = target == sourceRoot || chain.Count > 0
                && chain.Peek().parent == sourceRoot;
            writer.Write(belongsToRoot);
            if (!belongsToRoot)
            {
                return;
            }

            writer.Write(chain.Count);
            foreach (var transform in chain)
            {
                WriteVector3(writer, transform.localPosition);
                WriteQuaternion(writer, transform.localRotation);
                WriteVector3(writer, transform.localScale);
            }
        }

        private static void WriteVector3(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
            writer.Write(value.w);
        }

        private static string ToHex(IEnumerable<byte> bytes)
        {
            var builder = new StringBuilder(64);
            foreach (var value in bytes)
            {
                builder.Append(value.ToString("x2"));
            }
            return builder.ToString();
        }
    }
}
