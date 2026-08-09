using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>
    /// Creates a transform-only humanoid skeleton which receives source playable animation.
    /// Visible phantom bones select this skeleton through their second constraint source.
    /// </summary>
    internal static class PhantomAnimationDriverRigBuilder
    {
        private const string DriverRootName = "PhantomAnimationDriver";

        public static void Build(PhantomSlotBuildState slot)
        {
            if (slot?.CloneRoot == null
                || slot.CloneArmature == null
                || slot.Slot == null
                || slot.Slot.removeSourceControls)
            {
                return;
            }

            slot.AnimationDriverBones.Clear();
            slot.CloneToAnimationDriverPaths.Clear();
            slot.AnimationDriverToClonePaths.Clear();
            slot.AnimationDriverPoseParentClonePaths.Clear();

            var rootObject = new GameObject(UniqueChildName(slot.CloneArmature, DriverRootName));
            var driverRoot = rootObject.transform;
            driverRoot.SetParent(slot.CloneArmature, false);
            slot.AnimationDriverRoot = driverRoot;

            var mappedBones = new Dictionary<Transform, HumanBodyBones>();
            foreach (var pair in slot.CloneBones)
            {
                if (pair.Value != null)
                {
                    mappedBones[pair.Value] = pair.Key;
                }
            }

            foreach (var pair in slot.CloneBones
                         .Where(pair => pair.Value != null)
                         .OrderBy(pair => HierarchyDepth(pair.Value)))
            {
                var sourceBone = pair.Value;
                var driverParent = FindDriverParent(
                    sourceBone.parent,
                    mappedBones,
                    slot.AnimationDriverBones,
                    out var sourcePoseParent) ?? driverRoot;
                sourcePoseParent ??= slot.CloneArmature;
                var driverBone = new GameObject(sourceBone.name).transform;
                driverBone.SetParent(driverParent, false);

                var sourceParentIsMapped = sourceBone.parent != null
                                           && mappedBones.TryGetValue(sourceBone.parent, out var sourceParentBone)
                                           && slot.AnimationDriverBones.TryGetValue(sourceParentBone, out var expectedParent)
                                           && expectedParent == driverParent;
                if (sourceParentIsMapped
                    || sourceBone.parent == slot.CloneArmature && driverParent == driverRoot)
                {
                    driverBone.localPosition = sourceBone.localPosition;
                    driverBone.localRotation = sourceBone.localRotation;
                    driverBone.localScale = sourceBone.localScale;
                }
                else
                {
                    CopyWorldPose(sourceBone, driverBone);
                }

                slot.AnimationDriverBones[pair.Key] = driverBone;

                var sourcePoseParentPath = TransformPathUtility.GetRelativePath(
                    sourcePoseParent,
                    slot.CloneRoot.transform);
                if (sourcePoseParentPath != null)
                {
                    slot.AnimationDriverPoseParentClonePaths[pair.Key] = sourcePoseParentPath;
                }

                var clonePath = TransformPathUtility.GetRelativePath(
                    sourceBone,
                    slot.CloneRoot.transform);
                var driverPath = TransformPathUtility.GetRelativePath(
                    driverBone,
                    slot.CloneRoot.transform);
                if (clonePath == null || driverPath == null)
                {
                    continue;
                }

                slot.CloneToAnimationDriverPaths[clonePath] = driverPath;
                slot.AnimationDriverToClonePaths[driverPath] = clonePath;
            }
        }

        private static Transform FindDriverParent(
            Transform sourceParent,
            IReadOnlyDictionary<Transform, HumanBodyBones> mappedBones,
            IReadOnlyDictionary<HumanBodyBones, Transform> driverBones,
            out Transform sourcePoseParent)
        {
            for (var current = sourceParent; current != null; current = current.parent)
            {
                if (mappedBones.TryGetValue(current, out var bone)
                    && driverBones.TryGetValue(bone, out var driver))
                {
                    sourcePoseParent = current;
                    return driver;
                }
            }

            sourcePoseParent = null;
            return null;
        }

        private static string UniqueChildName(Transform parent, string preferredName)
        {
            if (parent.Find(preferredName) == null)
            {
                return preferredName;
            }

            for (var suffix = 1; ; suffix++)
            {
                var candidate = $"{preferredName}_{suffix}";
                if (parent.Find(candidate) == null)
                {
                    return candidate;
                }
            }
        }

        private static int HierarchyDepth(Transform transform)
        {
            var depth = 0;
            for (var current = transform; current != null; current = current.parent)
            {
                depth++;
            }
            return depth;
        }

        private static void CopyWorldPose(Transform source, Transform target)
        {
            target.SetPositionAndRotation(source.position, source.rotation);
            var parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
            var sourceScale = source.lossyScale;
            target.localScale = new Vector3(
                SafeDivide(sourceScale.x, parentScale.x),
                SafeDivide(sourceScale.y, parentScale.y),
                SafeDivide(sourceScale.z, parentScale.z));
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) > Mathf.Epsilon ? value / divisor : value;
        }
    }
}
