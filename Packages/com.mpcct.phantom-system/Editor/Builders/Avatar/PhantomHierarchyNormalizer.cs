using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Prepares a cloned avatar hierarchy for rig and animator generation.</summary>
    public static class PhantomHierarchyNormalizer
    {
        public static void Normalize(PhantomSystemBuildState system, PhantomSlotBuildState slot, PhantomBuildReport report)
        {
            if (slot.CloneAnimator == null)
            {
                return;
            }

            slot.CloneArmature = slot.CloneAnimator.GetBoneTransform(HumanBodyBones.Hips)?.parent;

            if (slot.CloneArmature == null)
            {
                var context = (Object)slot.CloneRoot ?? system.AuthoringComponent;
                report.InternalError($"Slot '{slot.SlotId}' could not resolve the prebaked phantom armature from humanoid hips.", context);
                return;
            }

            CacheHumanoidBones(system, slot);
        }

        private static void CacheHumanoidBones(PhantomSystemBuildState system, PhantomSlotBuildState slot)
        {
            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                {
                    continue;
                }

                var clone = slot.CloneAnimator.GetBoneTransform(bone);
                if (clone != null)
                {
                    slot.CloneBones[bone] = clone;
                    var path = TransformPathUtility.GetRelativePath(clone, system.AvatarRoot);
                    if (path != null)
                    {
                        slot.CloneBoneAvatarPaths[bone] = path;
                    }
                }
            }
        }
    }
}
