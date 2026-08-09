using System.Collections.Generic;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Builds the optional Phantom Grabbing Hips constraint and contact receivers.</summary>
    internal static class PhantomGrabbingHipsRigBuilder
    {
        public static void Build(
            PhantomSlotBuildState slot,
            Transform cloneHips,
            VRCParentConstraint hipsConstraint,
            Animator baseAnimator,
            PhantomBuildReport report)
        {
            var baseLeftHand = baseAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            var baseRightHand = baseAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            if (baseLeftHand == null || baseRightHand == null)
            {
                report.InternalError(
                    $"Slot '{slot.SlotId}' enables Phantom Grabbing, but the base avatar does not expose both Humanoid hand bones.",
                    baseAnimator);
                return;
            }

            // Keep the original local-space base-Hips constraint intact. The hand
            // constraint lives on a separate animation path but targets the same Hips.
            hipsConstraint.GlobalWeight = 1f;
            hipsConstraint.SolveInLocalSpace = true;
            hipsConstraint.FreezeToWorld = false;
            hipsConstraint.RebakeOffsetsWhenUnfrozen = false;

            var grabbingHipsConstraintHost = ConstraintRigBuilder.EnsureChild(
                slot.SlotRoot.transform,
                "PhantomGrabbingHipsConstraint");
            slot.PhantomGrabbingHipsConstraintHost = grabbingHipsConstraintHost;

            var grabbingHipsConstraint =
                grabbingHipsConstraintHost.gameObject.AddComponent<VRCParentConstraint>();
            grabbingHipsConstraint.Locked = false;
            grabbingHipsConstraint.IsActive = false;
            grabbingHipsConstraint.GlobalWeight = 1f;
            grabbingHipsConstraint.TargetTransform = cloneHips;
            grabbingHipsConstraint.SolveInLocalSpace = false;
            grabbingHipsConstraint.FreezeToWorld = true;
            grabbingHipsConstraint.RebakeOffsetsWhenUnfrozen = true;
            grabbingHipsConstraint.Sources = new VRCConstraintSourceKeyableList
            {
                new VRCConstraintSource { SourceTransform = baseLeftHand, Weight = 1f },
                new VRCConstraintSource { SourceTransform = baseRightHand, Weight = 0f }
            };
            // Initialize the per-source parent offsets before locking the generated
            // constraint. RebakeOffsetsWhenUnfrozen relies on this baked source data;
            // omitting the initial bake leaves every Capture with zero offsets.
            grabbingHipsConstraint.TryBakeCurrentOffsets(VRCConstraintBase.BakeOptions.BakeOffsets);
            grabbingHipsConstraint.Locked = true;
            grabbingHipsConstraint.enabled = true;

            AddPhantomGrabbingContactReceiver(
                cloneHips.gameObject,
                "HandL",
                PhantomParameterNames.PhantomGrabbingContactLeft(slot.Slot));
            AddPhantomGrabbingContactReceiver(
                cloneHips.gameObject,
                "HandR",
                PhantomParameterNames.PhantomGrabbingContactRight(slot.Slot));
        }

        private static void AddPhantomGrabbingContactReceiver(
            GameObject host,
            string collisionTag,
            string parameter)
        {
            var receiver = host.AddComponent<VRCContactReceiver>();
            receiver.rootTransform = host.transform;
            receiver.radius = 0.2f;
            receiver.localOnly = false;
            receiver.collisionTags = new List<string> { collisionTag };
            receiver.allowSelf = true;
            receiver.allowOthers = false;
            receiver.receiverType = ContactReceiver.ReceiverType.Constant;
            receiver.parameter = parameter;
            receiver.enabled = true;
        }
    }
}
