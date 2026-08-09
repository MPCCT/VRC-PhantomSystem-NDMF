using UnityEngine;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorClipUtility;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorGraphUtility;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Shows the Phantom Grabbing proxy-bone display only while frozen.</summary>
    internal static class PhantomGrabbingBoneDisplayAnimatorModule
    {
        public static void Build(PhantomAnimatorBuildContext context)
        {
            if (context.PhantomGrabbingBoneDisplayPath == null)
            {
                context.Report.InternalError(
                    $"Slot '{context.Slot.SlotId}' could not resolve the generated Phantom Grabbing bone display.",
                    context.ErrorContext);
                return;
            }

            var slot = context.Slot.Slot;
            var activate = PhantomParameterNames.Activate(slot);
            var freeze = PhantomParameterNames.Freeze(slot);
            var showBones = PhantomParameterNames.PhantomGrabbingShowBones(slot);
            AddBoolParameter(context.Controller, showBones, false);

            var hiddenClip = context.CreateClip("PhantomGrabbingBonesHidden");
            var visibleClip = context.CreateClip("PhantomGrabbingBonesVisible");
            SetFloat(
                hiddenClip,
                context.PhantomGrabbingBoneDisplayPath,
                typeof(SkinnedMeshRenderer),
                "m_Enabled",
                false);
            SetFloat(
                visibleClip,
                context.PhantomGrabbingBoneDisplayPath,
                typeof(SkinnedMeshRenderer),
                "m_Enabled",
                true);

            var layer = AddLayer(context.Controller, "PhantomGrabbingBoneDisplay");
            var machine = layer.stateMachine;
            var hidden = AddState(machine, hiddenClip);
            var visible = AddState(machine, visibleClip);
            machine.defaultState = hidden;

            AddTransition(
                hidden,
                visible,
                BoolCondition(activate, true),
                BoolCondition(freeze, true),
                BoolCondition(showBones, true));
            AddTransition(visible, hidden, BoolCondition(activate, false));
            AddTransition(visible, hidden, BoolCondition(freeze, false));
            AddTransition(visible, hidden, BoolCondition(showBones, false));
        }
    }
}
