using UnityEditor.Animations;
using UnityEngine;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorClipUtility;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorGraphUtility;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Builds per-slot armature scale and X-axis mirror controls.</summary>
    internal static class ScaleControlAnimatorModule
    {
        private const float MinimumScale = 0.2f;
        private const float MaximumScale = 1.8f;
        public const float DefaultScaleParameter = 0.5f;

        public static void Build(PhantomAnimatorBuildContext context)
        {
            var slot = context.Slot.Slot;
            var scaleParameter = PhantomParameterNames.Scale(slot);
            var mirrorParameter = PhantomParameterNames.Mirror(slot);
            var resetParameter = PhantomParameterNames.ScaleReset(slot);
            var weightParameter = PhantomParameterNames.ScaleControlWeight(slot);
            AddFloatParameter(context.Controller, scaleParameter, DefaultScaleParameter);
            AddFloatParameter(context.Controller, mirrorParameter, 0f);
            AddBoolParameter(context.Controller, resetParameter, false);
            AddFloatParameter(context.Controller, weightParameter, 1f);

            if (context.ArmaturePath == null || context.Slot.CloneArmature == null)
            {
                context.Report.Error(
                    $"Slot '{context.Slot.SlotId}' could not resolve the Scale Control armature path.",
                    context.ErrorContext);
                return;
            }

            var scaleTree = CreateScaleTree(context, scaleParameter);
            var mirrorTree = CreateMirrorTree(context, mirrorParameter);
            var directTree = CreateDirectTree(context, scaleTree, mirrorTree, weightParameter);
            var layer = AddLayer(context.Controller, "PhantomScaleControl");
            var machine = layer.stateMachine;
            var control = machine.AddState("PhantomScaleControl");
            control.motion = directTree;
            machine.defaultState = control;
            BuildResetLayer(context, scaleParameter, resetParameter);
        }

        private static void BuildResetLayer(
            PhantomAnimatorBuildContext context,
            string scaleParameter,
            string resetParameter)
        {
            var idleClip = context.CreateClip("PhantomScaleResetIdle");
            var resetClip = context.CreateClip("PhantomScaleReset");
            var layer = AddLayer(context.Controller, "PhantomScaleReset");
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
            var small = CreateScaleClip(context, "PhantomScaleSmall", MinimumScale);
            var big = CreateScaleClip(context, "PhantomScaleBig", MaximumScale);
            var tree = context.CreateBlendTree("PhantomScaleTree", scaleParameter);
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
            var tree = context.CreateBlendTree("PhantomMirrorTree", mirrorParameter);
            tree.AddChild(normal, 0f);
            tree.AddChild(mirrored, 1f);
            return tree;
        }

        private static BlendTree CreateDirectTree(
            PhantomAnimatorBuildContext context,
            BlendTree scaleTree,
            BlendTree mirrorTree,
            string weightParameter)
        {
            var tree = context.CreateBlendTree("PhantomScaleControlDirectTree", weightParameter);
            tree.blendType = BlendTreeType.Direct;
            tree.AddChild(scaleTree);
            tree.AddChild(mirrorTree);

            var children = tree.children;
            for (var index = 0; index < children.Length; index++)
            {
                children[index].directBlendParameter = weightParameter;
            }
            tree.children = children;
            return tree;
        }

        private static AnimationClip CreateScaleClip(
            PhantomAnimatorBuildContext context,
            string name,
            float multiplier)
        {
            var clip = context.CreateClip(name);
            var baseScale = context.Slot.CloneArmature.localScale;
            SetFloat(clip, context.ArmaturePath, typeof(Transform), "m_LocalScale.x", baseScale.x * multiplier);
            SetFloat(clip, context.ArmaturePath, typeof(Transform), "m_LocalScale.y", baseScale.y * multiplier);
            SetFloat(clip, context.ArmaturePath, typeof(Transform), "m_LocalScale.z", baseScale.z * multiplier);
            return clip;
        }

        private static AnimationClip CreateMirrorClip(
            PhantomAnimatorBuildContext context,
            string name,
            bool mirrored)
        {
            var clip = context.CreateClip(name);
            var baseScale = context.Slot.CloneRoot.transform.localScale;
            SetFloat(
                clip,
                context.RootPath,
                typeof(Transform),
                "m_LocalScale.x",
                baseScale.x * (mirrored ? -1f : 1f));
            SetFloat(clip, context.RootPath, typeof(Transform), "m_LocalScale.y", baseScale.y);
            SetFloat(clip, context.RootPath, typeof(Transform), "m_LocalScale.z", baseScale.z);
            return clip;
        }
    }
}
