using UnityEditor.Animations;
using UnityEngine;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorClipUtility;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorGraphUtility;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Builds per-slot scale and X-axis mirror controls.</summary>
    internal static class ScaleControlAnimatorModule
    {
        public const float MinimumScale = 0.2f;
        public const float MaximumScale = 1.8f;
        public const float DefaultScaleParameter = 0.5f;

        public static void Build(PhantomAnimatorBuildContext context)
        {
            var slot = context.Slot.Slot;
            var scaleParameter = PhantomParameterNames.Scale(slot);
            var mirrorParameter = PhantomParameterNames.Mirror(slot);
            var resetParameter = PhantomParameterNames.ScaleReset(slot);
            AddFloatParameter(context.Controller, scaleParameter, DefaultScaleParameter);
            AddBoolParameter(context.Controller, mirrorParameter, false);
            AddBoolParameter(context.Controller, resetParameter, false);

            if (context.SlotPath == null || context.Slot.SlotRoot == null)
            {
                context.Report.Error(
                    $"Slot '{context.Slot.SlotId}' could not resolve the Scale Control slot path.",
                    context.ErrorContext);
                return;
            }

            var normalTree = CreateScaleTree(context, scaleParameter, false);
            var mirroredTree = CreateScaleTree(context, scaleParameter, true);
            var layer = AddLayer(context, "PhantomScaleControl");
            var machine = layer.stateMachine;
            var normal = machine.AddState("PhantomScaleNormal");
            normal.motion = normalTree;
            var mirrored = machine.AddState("PhantomScaleMirrored");
            mirrored.motion = mirroredTree;
            machine.defaultState = normal;
            AddTransition(normal, mirrored, BoolCondition(mirrorParameter, true));
            AddTransition(mirrored, normal, BoolCondition(mirrorParameter, false));
            BuildResetLayer(context, scaleParameter, resetParameter);
        }

        private static void BuildResetLayer(
            PhantomAnimatorBuildContext context,
            string scaleParameter,
            string resetParameter)
        {
            var idleClip = context.CreateClip("PhantomScaleResetIdle");
            var resetClip = context.CreateClip("PhantomScaleReset");
            var layer = AddLayer(context, "PhantomScaleReset");
            var machine = layer.stateMachine;
            var idle = AddState(machine, idleClip);
            var reset = AddState(machine, resetClip);
            machine.defaultState = idle;
            AddTransition(idle, reset, BoolCondition(resetParameter, true));
            AddTransition(reset, idle, BoolCondition(resetParameter, false));
            AddSetFloatParameterDriver(context, reset, scaleParameter, DefaultScaleParameter);
        }

        private static BlendTree CreateScaleTree(
            PhantomAnimatorBuildContext context,
            string scaleParameter,
            bool mirrored)
        {
            var modeName = mirrored ? "Mirrored" : "Normal";
            var small = CreateScaleClip(
                context,
                $"PhantomScale{modeName}Small",
                MinimumScale,
                mirrored);
            var big = CreateScaleClip(
                context,
                $"PhantomScale{modeName}Big",
                MaximumScale,
                mirrored);
            var tree = context.CreateBlendTree(
                $"PhantomScale{modeName}Tree",
                scaleParameter);
            tree.AddChild(small, 0f);
            tree.AddChild(big, 1f);
            return tree;
        }

        private static AnimationClip CreateScaleClip(
            PhantomAnimatorBuildContext context,
            string name,
            float multiplier,
            bool mirrored)
        {
            var clip = context.CreateClip(name);
            var baseScale = context.Slot.SlotRoot.transform.localScale;
            SetFloat(
                clip,
                context.SlotPath,
                typeof(Transform),
                "m_LocalScale.x",
                baseScale.x * multiplier * (mirrored ? -1f : 1f));
            SetFloat(clip, context.SlotPath, typeof(Transform), "m_LocalScale.y", baseScale.y * multiplier);
            SetFloat(clip, context.SlotPath, typeof(Transform), "m_LocalScale.z", baseScale.z * multiplier);
            return clip;
        }
    }
}
