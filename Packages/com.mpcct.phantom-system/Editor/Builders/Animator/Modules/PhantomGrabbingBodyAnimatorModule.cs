using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorClipUtility;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorGraphUtility;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>
    /// Stages ownership of non-Hips humanoid bones between base-avatar following
    /// and the Phantom Grabbing body PhysBone proxy without enabling both directions.
    /// </summary>
    internal static class PhantomGrabbingBodyAnimatorModule
    {
        public static void Build(PhantomAnimatorBuildContext context)
        {
            if (context.PhantomGrabbingBodyPhysBonePaths.Count == 0
                || context.PhantomGrabbingBodySyncConstraintPaths.Count == 0
                || context.PhantomGrabbingBodyOutputConstraintPaths.Count == 0)
            {
                context.Report.InternalError(
                    $"Slot '{context.Slot.SlotId}' could not resolve the generated "
                    + "body proxy skeleton required by Phantom Grabbing.",
                    context.ErrorContext);
                return;
            }

            var clips = CreateClips(context);
            ApplyCurves(context, clips);
            BuildLayer(context, clips);
        }

        private static BodyGrabbingClips CreateClips(PhantomAnimatorBuildContext context)
        {
            return new BodyGrabbingClips(
                context.CreateClip("PhantomGrabbingBodyFollowBase"),
                context.CreateClip("PhantomGrabbingBodyReturnToBase"),
                context.CreateClip("PhantomGrabbingBodyEnterFrozen"));
        }

        private static void ApplyCurves(
            PhantomAnimatorBuildContext context,
            BodyGrabbingClips clips)
        {
            SetPose(context, clips.FollowBase, true, false, true, false, 0f);

            // Return disconnects proxy input/output and PhysBone while restoring
            // base follow for a frame before proxy synchronization resumes.
            SetPose(context, clips.ReturnToBase, false, false, true, false, FrameDuration);

            ApplyFreezeHandoff(context, clips.EnterFrozen);

        }

        private static void ApplyFreezeHandoff(
            PhantomAnimatorBuildContext context,
            AnimationClip clip)
        {
            var handoffDuration = 2f * FrameDuration;
            SetPose(context, clip, true, true, true, false, handoffDuration);

            // Explicit sampling order:
            // frame 0: segment PhysBones enabled while base follow and proxy sync stay active
            // frame 1: hold the exact same initialization pose for another frame
            // frame 2: proxy sync and base follow turn off as proxy output takes ownership

            var syncCurve = Stepped(
                new Keyframe(0f, 1f),
                new Keyframe(FrameDuration, 1f),
                new Keyframe(handoffDuration, 0f));
            foreach (var pair in context.PhantomGrabbingBodySyncConstraintPaths)
            {
                SetFloat(
                    clip,
                    pair.Value,
                    typeof(VRCParentConstraint),
                    IsActive,
                    syncCurve);
            }

            var baseFollowCurve = Stepped(
                new Keyframe(0f, 1f),
                new Keyframe(FrameDuration, 1f),
                new Keyframe(handoffDuration, 0f));
            foreach (var pair in context.Slot.PhantomGrabbingBodyProxyBones)
            {
                var bone = pair.Key;
                if (bone == HumanBodyBones.Hips
                    || !context.Slot.CloneBoneAvatarPaths.TryGetValue(bone, out var bonePath)
                    || !context.Slot.CloneBoneConstraintTypes.TryGetValue(bone, out var constraintType))
                {
                    continue;
                }

                SetFloat(clip, bonePath, constraintType, IsActive, baseFollowCurve);
            }

            var outputCurve = Stepped(
                new Keyframe(0f, 0f),
                new Keyframe(FrameDuration, 0f),
                new Keyframe(handoffDuration, 1f));
            foreach (var pair in context.PhantomGrabbingBodyOutputConstraintPaths)
            {
                SetFloat(
                    clip,
                    pair.Value,
                    typeof(VRCRotationConstraint),
                    IsActive,
                    outputCurve);
            }
        }

        private static void SetPose(
            PhantomAnimatorBuildContext context,
            AnimationClip clip,
            bool syncActive,
            bool physBoneActive,
            bool baseFollowActive,
            bool outputActive,
            float duration)
        {
            foreach (var pair in context.PhantomGrabbingBodyPhysBonePaths)
            {
                SetFloat(
                    clip,
                    pair.Value,
                    typeof(VRCPhysBone),
                    "m_Enabled",
                    Constant(duration, physBoneActive ? 1f : 0f));
            }

            foreach (var pair in context.PhantomGrabbingBodySyncConstraintPaths)
            {
                SetFloat(
                    clip,
                    pair.Value,
                    typeof(VRCParentConstraint),
                    IsActive,
                    Constant(duration, syncActive ? 1f : 0f));
            }

            foreach (var pair in context.PhantomGrabbingBodyOutputConstraintPaths)
            {
                SetFloat(
                    clip,
                    pair.Value,
                    typeof(VRCRotationConstraint),
                    IsActive,
                    Constant(duration, outputActive ? 1f : 0f));
            }

            foreach (var pair in context.Slot.PhantomGrabbingBodyProxyBones)
            {
                var bone = pair.Key;
                if (bone == HumanBodyBones.Hips
                    || !context.Slot.CloneBoneAvatarPaths.TryGetValue(bone, out var bonePath)
                    || !context.Slot.CloneBoneConstraintTypes.TryGetValue(bone, out var constraintType))
                {
                    continue;
                }

                SetFloat(
                    clip,
                    bonePath,
                    constraintType,
                    IsActive,
                    Constant(duration, baseFollowActive ? 1f : 0f));
            }
        }

        private static void BuildLayer(
            PhantomAnimatorBuildContext context,
            BodyGrabbingClips clips)
        {
            var layer = AddLayer(context.Controller, "PhantomGrabbingBody");
            var machine = layer.stateMachine;
            var baseFollow = AddState(machine, clips.FollowBase);
            var frozen = AddState(machine, clips.EnterFrozen);
            frozen.name = "PhantomGrabbingBodyFrozen";
            var returnToBase = AddState(machine, clips.ReturnToBase);
            machine.defaultState = baseFollow;

            var slot = context.Slot.Slot;
            var activate = PhantomParameterNames.Activate(slot);
            var freeze = PhantomParameterNames.Freeze(slot);

            AddTransition(
                baseFollow,
                frozen,
                BoolCondition(activate, true),
                BoolCondition(freeze, true));

            AddTransition(frozen, returnToBase, BoolCondition(activate, false));
            AddTransition(frozen, returnToBase, BoolCondition(freeze, false));

            // Always complete the return handoff before another Freeze entry. This
            // prevents a quick reactivation from leaving proxy input/output ownership
            // in the partially returned pose.
            AddExitTransition(returnToBase, baseFollow, 1.1f);
        }

        private readonly struct BodyGrabbingClips
        {
            public readonly AnimationClip FollowBase;
            public readonly AnimationClip ReturnToBase;
            public readonly AnimationClip EnterFrozen;

            public BodyGrabbingClips(
                AnimationClip followBase,
                AnimationClip returnToBase,
                AnimationClip enterFrozen)
            {
                FollowBase = followBase;
                ReturnToBase = returnToBase;
                EnterFrozen = enterFrozen;
            }
        }
    }
}
