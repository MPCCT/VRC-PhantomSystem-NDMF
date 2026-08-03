using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorClipUtility;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorGraphUtility;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Builds the optional Phantom Grabbing Hips animator behavior.</summary>
    internal static class PhantomGrabbingHipsAnimatorModule
    {
        private const string GestureLeft = "GestureLeft";
        private const string GestureRight = "GestureRight";

        public static void Build(PhantomAnimatorBuildContext context)
        {
            var slot = context.Slot.Slot;
            AddBoolParameter(
                context.Controller,
                PhantomParameterNames.PhantomGrabbingContactLeft(slot),
                false);
            AddBoolParameter(
                context.Controller,
                PhantomParameterNames.PhantomGrabbingContactRight(slot),
                false);
            AddIntParameter(context.Controller, GestureLeft, 0);
            AddIntParameter(context.Controller, GestureRight, 0);

            if (string.IsNullOrEmpty(context.PhantomGrabbingHipsPath)
                || string.IsNullOrEmpty(context.PhantomGrabbingHipsConstraintPath))
            {
                context.Report.Error(
                    $"Slot '{context.Slot.SlotId}' could not resolve the generated "
                    + "constraint paths required by Phantom Grabbing.",
                    context.ErrorContext);
                return;
            }

            var clips = CreateClips(context);
            ApplyCurves(context, clips);
            BuildLayer(context, clips);
        }

        private static HipsGrabbingClips CreateClips(PhantomAnimatorBuildContext context)
        {
            return new HipsGrabbingClips(
                context.CreateClip("PhantomGrabbingHipsDisabled"),
                context.CreateClip("PhantomGrabbingHipsFollowBase"),
                context.CreateClip("PhantomGrabbingHipsEnterFrozen"),
                context.CreateClip("PhantomGrabbingHipsFrozenIdle"),
                context.CreateClip("PhantomGrabbingHipsLeftCapture"),
                context.CreateClip("PhantomGrabbingHipsLeftFollow"),
                context.CreateClip("PhantomGrabbingHipsRightCapture"),
                context.CreateClip("PhantomGrabbingHipsRightFollow"),
                context.CreateClip("PhantomGrabbingHipsWorldLocked"));
        }

        private static void ApplyCurves(
            PhantomAnimatorBuildContext context,
            HipsGrabbingClips clips)
        {
            const float captureDuration = 2f * FrameDuration;

            SetPose(context, clips.Disabled, false, false, false, 1f, 0f, true, 0f);

            // FollowBase replaces both ReturnToBase and the old steady BaseFollow.
            // It first disables the hand constraint, then enables base-Hips follow
            // on the next sample and holds that final pose.
            SetPose(
                context,
                clips.FollowBase,
                false,
                false,
                false,
                1f,
                0f,
                true,
                FrameDuration);
            SetFloat(
                clips.FollowBase,
                context.PhantomGrabbingHipsPath,
                typeof(VRCParentConstraint),
                IsActive,
                Stepped(
                    new Keyframe(0f, 0f),
                    new Keyframe(FrameDuration, 1f)));

            // EnterFrozen gives the local-space base-Hips constraint one complete
            // evaluation interval before FrozenIdle disables it.
            SetPose(
                context,
                clips.EnterFrozen,
                true,
                false,
                false,
                1f,
                0f,
                true,
                FrameDuration);
            SetPose(context, clips.FrozenIdle, false, false, false, 1f, 0f, true, 0f);

            // Verified capture order:
            // 1. component disabled + frozen + inactive
            // 2. component still disabled + frozen + logically active
            // 3. component enabled + unfrozen + active (forces a clean runtime rebake)
            // Capture always completes before entering the steady Follow state;
            // Contact/Gesture releases cannot interrupt the rebake samples.
            SetPose(context, clips.LeftCapture, false, false, false, 1f, 0f, true, captureDuration);
            SetPose(context, clips.LeftFollow, false, true, true, 1f, 0f, false, 0f);
            SetPose(context, clips.RightCapture, false, false, false, 0f, 1f, true, captureDuration);
            SetPose(context, clips.RightFollow, false, true, true, 0f, 1f, false, 0f);
            // Once frozen to the world, the selected hand source no longer affects
            // the target. The next Capture writes its own source weights explicitly.
            SetPose(context, clips.WorldLocked, false, true, true, 1f, 0f, true, 0f);

            var captureActiveCurve = Stepped(
                new Keyframe(0f, 0f),
                new Keyframe(FrameDuration, 1f),
                new Keyframe(captureDuration, 1f));
            var captureFreezeCurve = Stepped(
                new Keyframe(0f, 1f),
                new Keyframe(FrameDuration, 1f),
                new Keyframe(captureDuration, 0f));
            var captureEnabledCurve = Stepped(
                new Keyframe(0f, 0f),
                new Keyframe(FrameDuration, 0f),
                new Keyframe(captureDuration, 1f));

            SetFloat(
                clips.LeftCapture,
                context.PhantomGrabbingHipsConstraintPath,
                typeof(VRCParentConstraint),
                IsActive,
                captureActiveCurve);
            SetFloat(
                clips.RightCapture,
                context.PhantomGrabbingHipsConstraintPath,
                typeof(VRCParentConstraint),
                IsActive,
                captureActiveCurve);
            SetFloat(
                clips.LeftCapture,
                context.PhantomGrabbingHipsConstraintPath,
                typeof(VRCParentConstraint),
                FreezeToWorld,
                captureFreezeCurve);
            SetFloat(
                clips.RightCapture,
                context.PhantomGrabbingHipsConstraintPath,
                typeof(VRCParentConstraint),
                FreezeToWorld,
                captureFreezeCurve);
            SetFloat(
                clips.LeftCapture,
                context.PhantomGrabbingHipsConstraintPath,
                typeof(VRCParentConstraint),
                "m_Enabled",
                captureEnabledCurve);
            SetFloat(
                clips.RightCapture,
                context.PhantomGrabbingHipsConstraintPath,
                typeof(VRCParentConstraint),
                "m_Enabled",
                captureEnabledCurve);

        }

        private static void SetPose(
            PhantomAnimatorBuildContext context,
            AnimationClip clip,
            bool hipsConstraintActive,
            bool grabbingHipsConstraintActive,
            bool grabbingHipsComponentEnabled,
            float leftHandWeight,
            float rightHandWeight,
            bool freezeToWorld,
            float duration)
        {
            SetFloat(
                clip,
                context.PhantomGrabbingHipsPath,
                typeof(VRCParentConstraint),
                IsActive,
                hipsConstraintActive);
            SetFloat(
                clip,
                context.PhantomGrabbingHipsConstraintPath,
                typeof(VRCParentConstraint),
                IsActive,
                grabbingHipsConstraintActive);
            SetFloat(
                clip,
                context.PhantomGrabbingHipsConstraintPath,
                typeof(VRCParentConstraint),
                "m_Enabled",
                grabbingHipsComponentEnabled);
            SetFloat(
                clip,
                context.PhantomGrabbingHipsConstraintPath,
                typeof(VRCParentConstraint),
                SourceWeight(0),
                Constant(duration, leftHandWeight));
            SetFloat(
                clip,
                context.PhantomGrabbingHipsConstraintPath,
                typeof(VRCParentConstraint),
                SourceWeight(1),
                Constant(duration, rightHandWeight));
            SetFloat(
                clip,
                context.PhantomGrabbingHipsConstraintPath,
                typeof(VRCParentConstraint),
                FreezeToWorld,
                Constant(duration, freezeToWorld ? 1f : 0f));
        }

        private static void BuildLayer(
            PhantomAnimatorBuildContext context,
            HipsGrabbingClips clips)
        {
            var layer = AddLayer(context.Controller, "PhantomGrabbingHips");
            var machine = layer.stateMachine;
            var disabled = AddState(machine, clips.Disabled);
            var followBase = AddState(machine, clips.FollowBase);
            var enterFrozen = AddState(machine, clips.EnterFrozen);
            var frozenIdle = AddState(machine, clips.FrozenIdle);
            var leftCapture = AddState(machine, clips.LeftCapture);
            var leftFollow = AddState(machine, clips.LeftFollow);
            var rightCapture = AddState(machine, clips.RightCapture);
            var rightFollow = AddState(machine, clips.RightFollow);
            var worldLocked = AddState(machine, clips.WorldLocked);
            machine.defaultState = disabled;

            var slot = context.Slot.Slot;
            var activate = PhantomParameterNames.Activate(slot);
            var freeze = PhantomParameterNames.Freeze(slot);
            var contactLeft = PhantomParameterNames.PhantomGrabbingContactLeft(slot);
            var contactRight = PhantomParameterNames.PhantomGrabbingContactRight(slot);

            // Global availability transitions are the only Any State edges. They
            // safely interrupt every transient state without restarting frozen
            // setup or capture while their conditions remain true.
            AddAnyStateTransition(
                machine,
                disabled,
                BoolCondition(activate, false));
            AddAnyStateTransition(
                machine,
                followBase,
                BoolCondition(activate, true),
                BoolCondition(freeze, false));

            AddTransition(
                disabled,
                enterFrozen,
                BoolCondition(activate, true),
                BoolCondition(freeze, true));
            AddTransition(
                followBase,
                enterFrozen,
                BoolCondition(activate, true),
                BoolCondition(freeze, true));

            AddExitTransition(enterFrozen, frozenIdle, 1.1f);

            AddStartTransition(frozenIdle, leftCapture, activate, freeze, contactLeft, GestureLeft);
            AddStartTransition(frozenIdle, rightCapture, activate, freeze, contactRight, GestureRight);

            AddExitTransition(leftCapture, leftFollow, 1.1f);
            AddReleaseTransitions(leftFollow, worldLocked, contactLeft, GestureLeft);

            AddExitTransition(rightCapture, rightFollow, 1.1f);
            AddReleaseTransitions(rightFollow, worldLocked, contactRight, GestureRight);

            // Handoffs always pass through WorldLocked. Left is registered first
            // to preserve deterministic left-hand priority.
            AddStartTransition(worldLocked, leftCapture, activate, freeze, contactLeft, GestureLeft);
            AddStartTransition(worldLocked, rightCapture, activate, freeze, contactRight, GestureRight);
        }

        private static void AddStartTransition(
            AnimatorState from,
            AnimatorState capture,
            string activate,
            string freeze,
            string contact,
            string gesture)
        {
            AddTransition(
                from,
                capture,
                BoolCondition(activate, true),
                BoolCondition(freeze, true),
                BoolCondition(contact, true),
                IntEqualsCondition(gesture, 5));
        }

        private static void AddReleaseTransitions(
            AnimatorState from,
            AnimatorState released,
            string contact,
            string gesture)
        {
            AddTransition(from, released, BoolCondition(contact, false));
            AddTransition(from, released, IntNotEqualCondition(gesture, 5));
        }

        private readonly struct HipsGrabbingClips
        {
            public readonly AnimationClip Disabled;
            public readonly AnimationClip FollowBase;
            public readonly AnimationClip EnterFrozen;
            public readonly AnimationClip FrozenIdle;
            public readonly AnimationClip LeftCapture;
            public readonly AnimationClip LeftFollow;
            public readonly AnimationClip RightCapture;
            public readonly AnimationClip RightFollow;
            public readonly AnimationClip WorldLocked;

            public HipsGrabbingClips(
                AnimationClip disabled,
                AnimationClip followBase,
                AnimationClip enterFrozen,
                AnimationClip frozenIdle,
                AnimationClip leftCapture,
                AnimationClip leftFollow,
                AnimationClip rightCapture,
                AnimationClip rightFollow,
                AnimationClip worldLocked)
            {
                Disabled = disabled;
                FollowBase = followBase;
                EnterFrozen = enterFrozen;
                FrozenIdle = frozenIdle;
                LeftCapture = leftCapture;
                LeftFollow = leftFollow;
                RightCapture = rightCapture;
                RightFollow = rightFollow;
                WorldLocked = worldLocked;
            }
        }
    }
}
