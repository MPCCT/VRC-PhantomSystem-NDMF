using UnityEditor.Animations;
using UnityEngine;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorClipUtility;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorGraphUtility;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Controls local Phantom View visibility, stereo separation, and mask size.</summary>
    internal static class PhantomViewAnimatorModule
    {
        private const string IsLocalParameter = "IsLocal";
        private const string VrModeParameter = "VRMode";

        public const float MaximumStereoStrength = 0.1f;
        public const float DefaultStereoStrengthParameter = 0.64f;
        public const float MaximumMaskSizeDegrees = 40f;
        public const float DefaultMaskSizeParameter = 1f;

        public static void BuildVisibility(PhantomAnimatorBuildContext context)
        {
            if (!ValidatePaths(context))
            {
                return;
            }

            var slot = context.Slot.Slot;
            var enabledParameter = PhantomParameterNames.PhantomViewEnabled(slot);
            AddBoolParameter(context.Controller, enabledParameter, false);
            AddBoolParameter(context.Controller, IsLocalParameter, true);

            BuildVisibilityLayer(context, enabledParameter);
        }

        public static void BuildControls(PhantomAnimatorBuildContext context)
        {
            if (!ValidatePaths(context))
            {
                return;
            }

            var slot = context.Slot.Slot;
            var stereoStrengthParameter = PhantomParameterNames.PhantomViewStereoStrength(slot);
            var maskSizeParameter = PhantomParameterNames.PhantomViewMaskSize(slot);
            var directWeightParameter = PhantomParameterNames.PhantomViewDirectWeight(slot);
            AddFloatParameter(
                context.Controller,
                stereoStrengthParameter,
                DefaultStereoStrengthParameter);
            AddFloatParameter(
                context.Controller,
                maskSizeParameter,
                DefaultMaskSizeParameter);
            AddFloatParameter(context.Controller, directWeightParameter, 1f);
            if (slot.enableScaleControl)
            {
                // Phantom View controls are merged through a standalone controller.
                // Declare the Core scale parameter here too so pre-merge controller
                // inspection does not report an unresolved BlendTree parameter.
                AddFloatParameter(
                    context.Controller,
                    PhantomParameterNames.Scale(slot),
                    ScaleControlAnimatorModule.DefaultScaleParameter);
            }

            var directTree = CreateDirectTree(context, "PhantomViewControlsDirect");
            AddDirectChild(
                directTree,
                CreateStereoStrengthMotion(context, stereoStrengthParameter),
                directWeightParameter);
            AddDirectChild(
                directTree,
                CreateMaskSizeTree(context, maskSizeParameter),
                directWeightParameter);

            var layer = AddLayer(context, "PhantomViewControls");
            var state = layer.stateMachine.AddState("PhantomViewControls");
            state.motion = directTree;
            state.writeDefaultValues = true;
            layer.stateMachine.defaultState = state;

            BuildCameraTypeFilter(context);
        }

        private static bool ValidatePaths(PhantomAnimatorBuildContext context)
        {
            if (context.Slot.PhantomViewLeftCamera != null
                && context.Slot.PhantomViewRightCamera != null
                && context.Slot.PhantomViewDisplayHost != null
                && context.PhantomViewLeftCameraPath != null
                && context.PhantomViewRightCameraPath != null
                && context.PhantomViewDisplayPath != null)
            {
                return true;
            }

            context.Report.InternalError(
                $"Slot '{context.Slot.SlotId}' could not resolve the generated Phantom View paths.",
                context.ErrorContext);
            return false;
        }

        private static void BuildVisibilityLayer(
            PhantomAnimatorBuildContext context,
            string enabledParameter)
        {
            var disabledClip = context.CreateClip("PhantomViewDisabled");
            var enabledClip = context.CreateClip("PhantomViewEnabled");
            ApplyVisibility(context, disabledClip, false);
            ApplyVisibility(context, enabledClip, true);

            var layer = AddLayer(context, "PhantomViewVisibility");
            var machine = layer.stateMachine;
            var disabled = AddState(machine, disabledClip);
            var enabled = AddState(machine, enabledClip);
            machine.defaultState = disabled;

            // Entering one local View turns off every other View parameter. Each
            // slot owns a separate Animator layer, so those layers will then
            // transition to their disabled states without overlapping displays.
            foreach (var otherSlot in context.System.Slots)
            {
                if (otherSlot == null
                    || otherSlot == context.Slot
                    || otherSlot.Slot == null
                    || !otherSlot.Slot.enablePhantomView)
                {
                    continue;
                }

                AddSetBoolParameterDriver(
                    context,
                    enabled,
                    PhantomParameterNames.PhantomViewEnabled(otherSlot.Slot),
                    false,
                    true);
            }

            AddTransition(
                disabled,
                enabled,
                BoolCondition(enabledParameter, true),
                BoolCondition(IsLocalParameter, true));
            AddTransition(
                enabled,
                disabled,
                BoolCondition(enabledParameter, false));
            AddTransition(
                enabled,
                disabled,
                BoolCondition(IsLocalParameter, false));
        }

        private static void ApplyVisibility(
            PhantomAnimatorBuildContext context,
            AnimationClip clip,
            bool visible)
        {
            SetFloat(
                clip,
                context.PhantomViewLeftCameraPath,
                typeof(Camera),
                "m_Enabled",
                visible);
            SetFloat(
                clip,
                context.PhantomViewRightCameraPath,
                typeof(Camera),
                "m_Enabled",
                visible);
            SetFloat(
                clip,
                context.PhantomViewDisplayPath,
                typeof(MeshRenderer),
                "m_Enabled",
                visible);
        }

        private static Motion CreateStereoStrengthMotion(
            PhantomAnimatorBuildContext context,
            string stereoStrengthParameter)
        {
            Motion motion;
            if (context.Slot.Slot.enableScaleControl)
            {
                var minimumScaleTree = CreateStereoStrengthTree(
                    context,
                    "PhantomViewStereoStrengthMinimumScaleTree",
                    stereoStrengthParameter,
                    ScaleControlAnimatorModule.MinimumScale);
                var maximumScaleTree = CreateStereoStrengthTree(
                    context,
                    "PhantomViewStereoStrengthMaximumScaleTree",
                    stereoStrengthParameter,
                    ScaleControlAnimatorModule.MaximumScale);
                var scaleTree = context.CreateBlendTree(
                    "PhantomViewStereoStrengthScaleTree",
                    PhantomParameterNames.Scale(context.Slot.Slot));
                scaleTree.AddChild(minimumScaleTree, 0f);
                scaleTree.AddChild(maximumScaleTree, 1f);
                motion = scaleTree;
            }
            else
            {
                motion = CreateStereoStrengthTree(
                    context,
                    "PhantomViewStereoStrengthTree",
                    stereoStrengthParameter,
                    1f);
            }

            return motion;
        }

        private static BlendTree CreateMaskSizeTree(
            PhantomAnimatorBuildContext context,
            string maskSizeParameter)
        {
            var minimumClip = CreateMaskSizeClip(
                context,
                "PhantomViewMaskSizeMinimum",
                0f);
            var maximumClip = CreateMaskSizeClip(
                context,
                "PhantomViewMaskSizeMaximum",
                MaximumMaskSizeDegrees);
            var tree = context.CreateBlendTree(
                "PhantomViewMaskSizeTree",
                maskSizeParameter);
            tree.AddChild(minimumClip, 0f);
            tree.AddChild(maximumClip, 1f);
            return tree;
        }

        private static void BuildCameraTypeFilter(
            PhantomAnimatorBuildContext context)
        {
            AddIntParameter(context.Controller, VrModeParameter, 0);

            var desktopClip = context.CreateClip("PhantomViewDesktopCameraFilter");
            var vrClip = context.CreateClip("PhantomViewVRCameraFilter");
            SetFloat(
                desktopClip,
                context.PhantomViewDisplayPath,
                typeof(MeshRenderer),
                "material._RequireStereoCamera",
                0f);
            SetFloat(
                vrClip,
                context.PhantomViewDisplayPath,
                typeof(MeshRenderer),
                "material._RequireStereoCamera",
                1f);

            var layer = AddLayer(context, "PhantomViewCameraFilter");
            var machine = layer.stateMachine;
            var desktop = AddState(machine, desktopClip);
            var vr = AddState(machine, vrClip);
            desktop.writeDefaultValues = true;
            vr.writeDefaultValues = true;
            machine.defaultState = desktop;

            AddTransition(
                desktop,
                vr,
                IntEqualsCondition(VrModeParameter, 1));
            AddTransition(
                vr,
                desktop,
                IntEqualsCondition(VrModeParameter, 0));
        }

        private static BlendTree CreateDirectTree(
            PhantomAnimatorBuildContext context,
            string name)
        {
            var direct = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Direct,
                useAutomaticThresholds = false
            };
            context.RegisterBlendTree(direct);
            return direct;
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

        private static AnimationClip CreateMaskSizeClip(
            PhantomAnimatorBuildContext context,
            string name,
            float maskSizeDegrees)
        {
            var clip = context.CreateClip(name);
            SetFloat(
                clip,
                context.PhantomViewDisplayPath,
                typeof(MeshRenderer),
                "material._MaskSizeAngleDegrees",
                maskSizeDegrees);
            return clip;
        }

        private static BlendTree CreateStereoStrengthTree(
            PhantomAnimatorBuildContext context,
            string treeName,
            string stereoStrengthParameter,
            float scaleMultiplier)
        {
            var minimumClip = CreateStereoStrengthClip(
                context,
                $"{treeName}Minimum",
                0f);
            var maximumClip = CreateStereoStrengthClip(
                context,
                $"{treeName}Maximum",
                MaximumStereoStrength * scaleMultiplier);
            var tree = context.CreateBlendTree(
                treeName,
                stereoStrengthParameter);
            tree.AddChild(minimumClip, 0f);
            tree.AddChild(maximumClip, 1f);
            return tree;
        }

        private static AnimationClip CreateStereoStrengthClip(
            PhantomAnimatorBuildContext context,
            string name,
            float stereoStrength)
        {
            var clip = context.CreateClip(name);
            var halfStrength = stereoStrength * 0.5f;
            SetFloat(
                clip,
                context.PhantomViewLeftCameraPath,
                typeof(Transform),
                "m_LocalPosition.x",
                -halfStrength);
            SetFloat(
                clip,
                context.PhantomViewRightCameraPath,
                typeof(Transform),
                "m_LocalPosition.x",
                halfStrength);
            return clip;
        }
    }
}
