using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorClipUtility;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorGraphUtility;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Builds the required Activate, Freeze, and Position Lock animator behavior.</summary>
    internal static class CoreAnimatorModule
    {
        public static void Build(PhantomAnimatorBuildContext context)
        {
            var slot = context.Slot.Slot;
            AddBoolParameter(context.Controller, PhantomParameterNames.Activate(slot), false);
            AddBoolParameter(context.Controller, PhantomParameterNames.Freeze(slot), false);
            AddBoolParameter(context.Controller, PhantomParameterNames.PositionLock(slot), true);

            BuildActivateLayer(context);
            BuildPositionLockLayer(context);
        }

        private static void BuildActivateLayer(PhantomAnimatorBuildContext context)
        {
            var slot = context.Slot.Slot;
            var activateParameter = PhantomParameterNames.Activate(slot);
            var freezeParameter = PhantomParameterNames.Freeze(slot);
            var positionLockParameter = PhantomParameterNames.PositionLock(slot);

            var offClip = context.CreateClip("PhantomOFF");
            var prepareClip = context.CreateClip("PhantomPrepare");
            var freezeOffClip = context.CreateClip("PhantomFreezeOff");
            var freezeClip = context.CreateClip("PhantomFreeze");
            ApplyActivateCurves(context, offClip, prepareClip, freezeOffClip, freezeClip);

            var layer = AddLayer(context.Controller, "PhantomActivate");
            var machine = layer.stateMachine;
            var off = AddState(machine, offClip);
            var prepare = AddState(machine, prepareClip);
            var freezeOff = AddState(machine, freezeOffClip);
            var freeze = AddState(machine, freezeClip);
            machine.defaultState = off;

            AddTransition(off, prepare, BoolCondition(activateParameter, true));
            AddTransition(
                prepare,
                freeze,
                0.05f,
                BoolCondition(activateParameter, true),
                BoolCondition(freezeParameter, true));
            AddTransition(
                prepare,
                freezeOff,
                0.05f,
                BoolCondition(activateParameter, true),
                BoolCondition(freezeParameter, false));
            AddTransition(freeze, freezeOff, BoolCondition(freezeParameter, false));
            AddTransition(freeze, off, BoolCondition(activateParameter, false));
            AddTransition(freezeOff, freeze, BoolCondition(freezeParameter, true));
            AddTransition(freezeOff, off, BoolCondition(activateParameter, false));

            AddSetBoolParameterDriver(context, prepare, positionLockParameter, true);
            AddSetBoolParameterDriver(context, freeze, positionLockParameter, true);
        }

        private static void ApplyActivateCurves(
            PhantomAnimatorBuildContext context,
            AnimationClip off,
            AnimationClip prepare,
            AnimationClip freezeOff,
            AnimationClip freeze)
        {
            SetGameObjectActive(off, context.RootPath, false);
            SetGameObjectActive(prepare, context.RootPath, true);
            SetGameObjectActive(freezeOff, context.RootPath, true);
            SetGameObjectActive(freeze, context.RootPath, true);

            SetFloat(off, context.RootPath, typeof(VRCParentConstraint), IsActive, false);
            SetFloat(prepare, context.RootPath, typeof(VRCParentConstraint), IsActive, true);
            SetFloat(freezeOff, context.RootPath, typeof(VRCParentConstraint), IsActive, true);
            SetFloat(freeze, context.RootPath, typeof(VRCParentConstraint), IsActive, true);

            SetFloat(off, context.RootPath, typeof(VRCParentConstraint), FreezeToWorld, true);
            SetFloat(prepare, context.RootPath, typeof(VRCParentConstraint), FreezeToWorld, false);
            SetFloat(freezeOff, context.RootPath, typeof(VRCParentConstraint), FreezeToWorld, true);
            SetFloat(freeze, context.RootPath, typeof(VRCParentConstraint), FreezeToWorld, true);

            foreach (var pair in context.Slot.CloneBoneAvatarPaths)
            {
                var bone = pair.Key;
                var path = pair.Value;
                if (context.Slot.Slot.enablePhantomGrabbing
                    && context.Slot.PhantomGrabbingBodyProxyBones.ContainsKey(bone))
                {
                    // Phantom Grabbing owns both the Hips constraint and the body
                    // remaining proxy-backed humanoid constraints.
                    continue;
                }

                if (!context.Slot.CloneBoneConstraintTypes.TryGetValue(bone, out var constraintType))
                {
                    continue;
                }

                SetFloat(off, path, constraintType, IsActive, true);
                SetFloat(prepare, path, constraintType, IsActive, true);
                SetFloat(freezeOff, path, constraintType, IsActive, true);
                SetFloat(freeze, path, constraintType, IsActive, false);
            }
        }

        private static void BuildPositionLockLayer(PhantomAnimatorBuildContext context)
        {
            var onClip = context.CreateClip("PositionLockOn");
            var offClip = context.CreateClip("PositionLockOff");
            var prepareClip = context.CreateClip("PositionLockPrepare");

            if (context.BaseAvatarPositionPath == null || context.ArmaturePath == null)
            {
                context.Report.Error(
                    $"Slot '{context.Slot.SlotId}' could not resolve PositionLock helper paths.",
                    context.ErrorContext);
            }
            else
            {
                ApplyPositionLockCurves(context, onClip, offClip, prepareClip);
            }

            var layer = AddLayer(context.Controller, "PhantomPositionLock");
            var machine = layer.stateMachine;
            var on = AddState(machine, onClip);
            var off = AddState(machine, offClip);
            var prepare = AddState(machine, prepareClip);
            machine.defaultState = on;

            var slot = context.Slot.Slot;
            AddTransition(
                on,
                off,
                BoolCondition(PhantomParameterNames.PositionLock(slot), false),
                BoolCondition(PhantomParameterNames.Activate(slot), true));
            AddTransition(
                off,
                prepare,
                BoolCondition(PhantomParameterNames.PositionLock(slot), true));
            AddTransition(
                off,
                on,
                BoolCondition(PhantomParameterNames.Activate(slot), false));
            AddExitTransition(prepare, on, 1.1f);
        }

        private static void ApplyPositionLockCurves(
            PhantomAnimatorBuildContext context,
            AnimationClip on,
            AnimationClip off,
            AnimationClip prepare)
        {
            SetFloat(on, context.BaseAvatarPositionPath, typeof(VRCParentConstraint), FreezeToWorld, false);
            SetFloat(on, context.ArmaturePath, typeof(VRCParentConstraint), FreezeToWorld, false);
            SetFloat(on, context.RootPath, typeof(VRCParentConstraint), SourceWeight(0), 1f);
            SetFloat(on, context.RootPath, typeof(VRCParentConstraint), SourceWeight(1), 0f);
            SetFloat(on, context.ArmaturePath, typeof(VRCParentConstraint), SolveInLocalSpace, true);

            SetFloat(off, context.BaseAvatarPositionPath, typeof(VRCParentConstraint), FreezeToWorld, true);
            SetFloat(off, context.ArmaturePath, typeof(VRCParentConstraint), FreezeToWorld, false);
            SetFloat(off, context.RootPath, typeof(VRCParentConstraint), SourceWeight(0), 1f);
            SetFloat(off, context.RootPath, typeof(VRCParentConstraint), SourceWeight(1), 0f);
            SetFloat(off, context.ArmaturePath, typeof(VRCParentConstraint), SolveInLocalSpace, true);

            var freezeToWorldCurve = Stepped(
                new Keyframe(0f, 1f),
                new Keyframe(FrameDuration, 0f));
            var solveInLocalSpaceCurve = Stepped(
                new Keyframe(0f, 0f),
                new Keyframe(2f * FrameDuration, 1f));

            SetFloat(
                prepare,
                context.BaseAvatarPositionPath,
                typeof(VRCParentConstraint),
                FreezeToWorld,
                freezeToWorldCurve);
            SetFloat(
                prepare,
                context.RootPath,
                typeof(VRCParentConstraint),
                FreezeToWorld,
                freezeToWorldCurve);
            SetFloat(
                prepare,
                context.ArmaturePath,
                typeof(VRCParentConstraint),
                FreezeToWorld,
                true);
            SetFloat(
                prepare,
                context.RootPath,
                typeof(VRCParentConstraint),
                SourceWeight(0),
                AnimationCurve.Constant(FrameDuration, FrameDuration, 0f));
            SetFloat(
                prepare,
                context.RootPath,
                typeof(VRCParentConstraint),
                SourceWeight(1),
                AnimationCurve.Constant(FrameDuration, FrameDuration, 1f));
            SetFloat(
                prepare,
                context.ArmaturePath,
                typeof(VRCParentConstraint),
                SolveInLocalSpace,
                solveInLocalSpaceCurve);
        }
    }
}
