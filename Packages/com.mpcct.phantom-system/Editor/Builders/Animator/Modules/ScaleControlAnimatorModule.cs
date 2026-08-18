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
            var directWeightParameter = PhantomParameterNames.ScaleDirectWeight(slot);
            var resetParameter = PhantomParameterNames.ScaleReset(slot);
            AddCoreParameter(context.Controller, slot, scaleParameter);
            AddCoreParameter(context.Controller, slot, mirrorParameter);
            AddCoreParameter(context.Controller, slot, directWeightParameter);
            AddCoreParameter(context.Controller, slot, resetParameter);

            if (context.SlotPath == null
                || context.MirrorPath == null
                || context.Slot.SlotRoot == null
                || context.Slot.MirrorRoot == null)
            {
                context.Report.Error(
                    $"Slot '{context.Slot.SlotId}' could not resolve its ScaleRoot or MirrorRoot path.",
                    context.ErrorContext);
                return;
            }

            var scaleTree = CreateScaleTree(context, scaleParameter);
            var mirrorTree = CreateMirrorTree(context, mirrorParameter);
            var directTree = new BlendTree
            {
                name = "PhantomScaleControlDirect",
                blendType = BlendTreeType.Direct,
                useAutomaticThresholds = false
            };
            context.RegisterBlendTree(directTree);
            AddDirectChild(directTree, scaleTree, directWeightParameter);
            AddDirectChild(directTree, mirrorTree, directWeightParameter);

            var layer = AddLayer(context, "PhantomScaleControl");
            var machine = layer.stateMachine;
            var state = machine.AddState("PhantomScaleControl");
            state.motion = directTree;
            state.writeDefaultValues = true;
            machine.defaultState = state;
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
            string scaleParameter)
        {
            var small = CreateScaleClip(
                context,
                "PhantomScaleSmall",
                MinimumScale);
            var big = CreateScaleClip(
                context,
                "PhantomScaleBig",
                MaximumScale);
            var tree = context.CreateBlendTree(
                "PhantomScaleTree",
                scaleParameter);
            tree.AddChild(small, 0f);
            tree.AddChild(big, 1f);
            return tree;
        }

        private static BlendTree CreateMirrorTree(
            PhantomAnimatorBuildContext context,
            string mirrorParameter)
        {
            var normal = CreateMirrorClip(context, "PhantomMirrorOff", false);
            var mirrored = CreateMirrorClip(context, "PhantomMirrorOn", true);
            var tree = context.CreateBlendTree(
                "PhantomMirrorTree",
                mirrorParameter);
            tree.AddChild(normal, 0f);
            tree.AddChild(mirrored, 1f);
            return tree;
        }

        private static AnimationClip CreateScaleClip(
            PhantomAnimatorBuildContext context,
            string name,
            float multiplier)
        {
            var clip = context.CreateClip(name);
            var baseScale = context.Slot.SlotRoot.transform.localScale;
            SetFloat(clip, context.SlotPath, typeof(Transform), "m_LocalScale.x", baseScale.x * multiplier);
            SetFloat(clip, context.SlotPath, typeof(Transform), "m_LocalScale.y", baseScale.y * multiplier);
            SetFloat(clip, context.SlotPath, typeof(Transform), "m_LocalScale.z", baseScale.z * multiplier);
            return clip;
        }

        private static AnimationClip CreateMirrorClip(
            PhantomAnimatorBuildContext context,
            string name,
            bool mirrored)
        {
            var clip = context.CreateClip(name);
            var baseScale = context.Slot.MirrorRoot.transform.localScale;
            SetFloat(
                clip,
                context.MirrorPath,
                typeof(Transform),
                "m_LocalScale.x",
                baseScale.x * (mirrored ? -1f : 1f));
            SetFloat(clip, context.MirrorPath, typeof(Transform), "m_LocalScale.y", baseScale.y);
            SetFloat(clip, context.MirrorPath, typeof(Transform), "m_LocalScale.z", baseScale.z);
            return clip;
        }

        private static void AddDirectChild(
            BlendTree direct,
            Motion motion,
            string directWeightParameter)
        {
            direct.AddChild(motion, 1f);
            var children = direct.children;
            children[children.Length - 1].directBlendParameter = directWeightParameter;
            direct.children = children;
        }
    }
}
