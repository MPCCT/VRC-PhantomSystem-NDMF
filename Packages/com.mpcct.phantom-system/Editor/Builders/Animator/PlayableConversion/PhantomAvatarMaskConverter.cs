using System;
using System.Collections.Generic;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Converts humanoid masks into explicit paths for the generic phantom bone curves.</summary>
    internal static class PhantomAvatarMaskConverter
    {
        public static AvatarMask Convert(
            PhantomSlotBuildState slot,
            AvatarMask descriptorMask,
            AvatarMask layerMask,
            string name)
        {
            if (slot?.CloneRoot == null || descriptorMask == null && layerMask == null)
            {
                return null;
            }

            var result = new AvatarMask { name = name };
            for (var part = AvatarMaskBodyPart.Root;
                 part < AvatarMaskBodyPart.LastBodyPart;
                 part++)
            {
                result.SetHumanoidBodyPartActive(part, false);
            }

            var transforms = slot.CloneRoot.GetComponentsInChildren<Transform>(true);
            result.transformCount = transforms.Length;
            var boneParts = BuildBonePartMap(slot);
            for (var index = 0; index < transforms.Length; index++)
            {
                var transform = transforms[index];
                var path = TransformPathUtility.GetRelativePath(
                    transform,
                    slot.CloneRoot.transform) ?? string.Empty;
                var sourcePath = slot.AnimationDriverToClonePaths.TryGetValue(
                    path,
                    out var clonePath)
                    ? clonePath
                    : path;
                var part = FindNearestBodyPart(transform, slot.CloneRoot.transform, boneParts);
                var active = IsActive(descriptorMask, sourcePath, part)
                             && IsActive(layerMask, sourcePath, part);
                result.SetTransformPath(index, path);
                result.SetTransformActive(index, active);
            }

            return result;
        }

        private static Dictionary<Transform, AvatarMaskBodyPart> BuildBonePartMap(
            PhantomSlotBuildState slot)
        {
            var result = new Dictionary<Transform, AvatarMaskBodyPart>();
            foreach (var pair in slot.CloneBones)
            {
                if (pair.Value != null && TryGetBodyPart(pair.Key, out var part))
                {
                    result[pair.Value] = part;
                }
            }

            foreach (var pair in slot.AnimationDriverBones)
            {
                if (pair.Value != null && TryGetBodyPart(pair.Key, out var part))
                {
                    result[pair.Value] = part;
                }
            }

            return result;
        }

        private static AvatarMaskBodyPart? FindNearestBodyPart(
            Transform transform,
            Transform root,
            IReadOnlyDictionary<Transform, AvatarMaskBodyPart> boneParts)
        {
            for (var current = transform;
                 current != null && current != root;
                 current = current.parent)
            {
                if (boneParts.TryGetValue(current, out var part))
                {
                    return part;
                }
            }

            return null;
        }

        private static bool IsActive(
            AvatarMask mask,
            string path,
            AvatarMaskBodyPart? bodyPart)
        {
            if (mask == null)
            {
                return true;
            }

            var transformActive = ResolveTransformActive(mask, path);
            return transformActive
                   && (!bodyPart.HasValue
                       || mask.GetHumanoidBodyPartActive(bodyPart.Value));
        }

        private static bool ResolveTransformActive(AvatarMask mask, string path)
        {
            if (mask.transformCount == 0)
            {
                return true;
            }

            var bestLength = -1;
            var active = true;
            for (var index = 0; index < mask.transformCount; index++)
            {
                var candidate = mask.GetTransformPath(index) ?? string.Empty;
                if (!IsSameOrParent(candidate, path) || candidate.Length < bestLength)
                {
                    continue;
                }

                bestLength = candidate.Length;
                active = mask.GetTransformActive(index);
            }

            return active;
        }

        private static bool IsSameOrParent(string candidate, string path)
        {
            return string.IsNullOrEmpty(candidate)
                   || string.Equals(candidate, path, StringComparison.Ordinal)
                   || path.StartsWith(candidate + "/", StringComparison.Ordinal);
        }

        private static bool TryGetBodyPart(
            HumanBodyBones bone,
            out AvatarMaskBodyPart part)
        {
            switch (bone)
            {
                case HumanBodyBones.Hips:
                    part = AvatarMaskBodyPart.Body;
                    return true;
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest:
                case HumanBodyBones.UpperChest:
                    part = AvatarMaskBodyPart.Body;
                    return true;
                case HumanBodyBones.Neck:
                case HumanBodyBones.Head:
                case HumanBodyBones.LeftEye:
                case HumanBodyBones.RightEye:
                case HumanBodyBones.Jaw:
                    part = AvatarMaskBodyPart.Head;
                    return true;
                case HumanBodyBones.LeftShoulder:
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.LeftHand:
                    part = AvatarMaskBodyPart.LeftArm;
                    return true;
                case HumanBodyBones.RightShoulder:
                case HumanBodyBones.RightUpperArm:
                case HumanBodyBones.RightLowerArm:
                case HumanBodyBones.RightHand:
                    part = AvatarMaskBodyPart.RightArm;
                    return true;
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.LeftToes:
                    part = AvatarMaskBodyPart.LeftLeg;
                    return true;
                case HumanBodyBones.RightUpperLeg:
                case HumanBodyBones.RightLowerLeg:
                case HumanBodyBones.RightFoot:
                case HumanBodyBones.RightToes:
                    part = AvatarMaskBodyPart.RightLeg;
                    return true;
            }

            if (bone >= HumanBodyBones.LeftThumbProximal
                && bone <= HumanBodyBones.LeftLittleDistal)
            {
                part = AvatarMaskBodyPart.LeftFingers;
                return true;
            }

            if (bone >= HumanBodyBones.RightThumbProximal
                && bone <= HumanBodyBones.RightLittleDistal)
            {
                part = AvatarMaskBodyPart.RightFingers;
                return true;
            }

            part = AvatarMaskBodyPart.LastBodyPart;
            return false;
        }
    }
}
