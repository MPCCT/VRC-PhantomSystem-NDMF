using nadena.dev.ndmf;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Builds the core positional and humanoid constraint rig for a slot.</summary>
    public static class ConstraintRigBuilder
    {
        public static void Build(
            BuildContext ctx,
            PhantomSlotBuildState slot,
            PhantomBuildReport report)
        {
            if (slot.CloneRoot == null || slot.CloneAnimator == null)
            {
                return;
            }

            var baseAnimator = ctx.AvatarRootObject.GetComponent<Animator>();
            if (baseAnimator == null)
            {
                report.InternalError("Base avatar Animator disappeared before constraint generation.", ctx.AvatarRootObject);
                return;
            }

            var baseArmature = baseAnimator.GetBoneTransform(HumanBodyBones.Hips)?.parent;
            if (baseArmature == null)
            {
                report.InternalError("Base avatar armature could not be resolved from humanoid hips.", baseAnimator);
                return;
            }

            if (slot.BaseAvatarPosition == null)
            {
                var baseAvatarPosition = EnsureChild(slot.SlotRoot.transform, "BaseAvatarPosition");
                baseAvatarPosition.SetPositionAndRotation(ctx.AvatarRootTransform.position, ctx.AvatarRootTransform.rotation);
                var armatureTarget = EnsureChild(baseAvatarPosition, "ArmatureConstraintTarget");
                slot.BaseAvatarPosition = baseAvatarPosition;
                slot.ArmatureConstraintTarget = armatureTarget;

                var baseAvatarPositionConstraint = AddParentConstraint(baseAvatarPosition.gameObject, ctx.AvatarRootTransform, true, false);
                baseAvatarPositionConstraint.FreezeToWorld = false;
                AddParentConstraint(armatureTarget.gameObject, baseArmature, true, false);
            }

            var armatureConstraintTarget = slot.ArmatureConstraintTarget;
            var spawnPosition = EnsureChild(slot.SlotRoot.transform, "PhantomSpawnPosition");
            if (slot.Slot.spawnPositionOverride != null)
            {
                spawnPosition.SetPositionAndRotation(slot.Slot.spawnPositionOverride.position, slot.Slot.spawnPositionOverride.rotation);
            }
            else
            {
                spawnPosition.SetPositionAndRotation(ctx.AvatarRootTransform.position, ctx.AvatarRootTransform.rotation);
            }

            var rootResetRotationOffset = Quaternion.Inverse(slot.CloneArmature.rotation) * slot.CloneRoot.transform.rotation;
            var rootConstraint = slot.CloneRoot.AddComponent<VRCParentConstraint>();
            rootConstraint.Locked = true;
            rootConstraint.IsActive = true;
            rootConstraint.FreezeToWorld = true;
            rootConstraint.Sources = new VRCConstraintSourceKeyableList
            {
                new VRCConstraintSource { SourceTransform = spawnPosition, Weight = 1f },
                new VRCConstraintSource
                {
                    SourceTransform = slot.CloneArmature,
                    Weight = 0f,
                    ParentPositionOffset = Vector3.zero,
                    ParentRotationOffset = rootResetRotationOffset.eulerAngles
                }
            };
            rootConstraint.enabled = true;

            var armatureConstraint = AddParentConstraint(slot.CloneArmature.gameObject, armatureConstraintTarget, true, true);
            armatureConstraint.FreezeToWorld = true;

            foreach (var pair in slot.CloneBones)
            {
                var bone = pair.Key;
                var cloneBone = pair.Value;
                var baseBone = baseAnimator.GetBoneTransform(bone);
                if (baseBone == null)
                {
                    continue;
                }

                slot.AnimationDriverBones.TryGetValue(bone, out var driverBone);

                if (!slot.Slot.useRotationConstraint || bone == HumanBodyBones.Hips)
                {
                    var constraint = AddParentConstraint(
                        cloneBone.gameObject,
                        baseBone,
                        true,
                        true,
                        driverBone);
                    slot.CloneBoneConstraintTypes[bone] = typeof(VRCParentConstraint);
                    if (bone == HumanBodyBones.Hips
                        && slot.Slot.enablePhantomGrabbing)
                    {
                        PhantomGrabbingHipsRigBuilder.Build(
                            slot,
                            cloneBone,
                            constraint,
                            baseAnimator,
                            report);
                    }
                }
                else
                {
                    AddRotationConstraint(
                        cloneBone.gameObject,
                        baseBone,
                        !slot.Slot.rotationSolveInWorldSpace,
                        driverBone);
                    slot.CloneBoneConstraintTypes[bone] = typeof(VRCRotationConstraint);
                }
            }

            if (slot.Slot.enablePhantomGrabbing
                && slot.PhantomGrabbingHipsConstraintHost != null)
            {
                PhantomGrabbingBodyRigBuilder.Build(ctx, slot, report);
            }
        }

        internal static Transform EnsureChild(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                return existing;
            }

            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static VRCParentConstraint AddParentConstraint(
            GameObject target,
            Transform source,
            bool active,
            bool solveInLocalSpace,
            Transform animationDriver = null)
        {
            var constraint = target.AddComponent<VRCParentConstraint>();
            constraint.Locked = true;
            constraint.IsActive = active;
            constraint.SolveInLocalSpace = solveInLocalSpace;
            var sources = new VRCConstraintSourceKeyableList
            {
                new VRCConstraintSource { SourceTransform = source, Weight = 1f }
            };
            if (animationDriver != null)
            {
                sources.Add(new VRCConstraintSource
                {
                    SourceTransform = animationDriver,
                    Weight = 0f
                });
            }
            constraint.Sources = sources;
            constraint.enabled = true;
            return constraint;
        }

        private static VRCRotationConstraint AddRotationConstraint(
            GameObject target,
            Transform source,
            bool solveInLocalSpace,
            Transform animationDriver)
        {
            var constraint = target.AddComponent<VRCRotationConstraint>();
            constraint.Locked = false;
            constraint.IsActive = true;
            constraint.SolveInLocalSpace = solveInLocalSpace;
            var sources = new VRCConstraintSourceKeyableList
            {
                new VRCConstraintSource { SourceTransform = source, Weight = 1f }
            };
            if (animationDriver != null)
            {
                sources.Add(new VRCConstraintSource
                {
                    SourceTransform = animationDriver,
                    Weight = 0f
                });
            }
            constraint.Sources = sources;
            constraint.TryBakeCurrentOffsets(VRCConstraintBase.BakeOptions.BakeOffsets);
            constraint.Locked = true;
            constraint.enabled = true;
            return constraint;
        }
    }
}
