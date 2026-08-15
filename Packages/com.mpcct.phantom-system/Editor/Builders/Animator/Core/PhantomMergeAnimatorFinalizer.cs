using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Finalizes generated Modular Avatar animator merge ordering.</summary>
    internal static class PhantomMergeAnimatorFinalizer
    {
        public static void Apply(BuildContext context, PhantomBuildState state)
        {
            if (state == null || !state.HasWork)
            {
                return;
            }

            var phantomRoot = state.System.RuntimeRoot != null
                ? state.System.RuntimeRoot.transform
                : null;

            var allMergeAnimators = context.AvatarRootObject
                .GetComponentsInChildren<ModularAvatarMergeAnimator>(true);
            var fxPriority = ExternalMaxPriority(
                allMergeAnimators,
                phantomRoot,
                VRCAvatarDescriptor.AnimLayerType.FX);

            foreach (var slot in state.System.Slots)
            {
                if (slot.SourceGestureMergeAnimator != null)
                {
                    slot.SourceGestureMergeAnimator.layerPriority = CheckedIncrement(
                        fxPriority,
                        slot,
                        state.Report);
                    fxPriority = slot.SourceGestureMergeAnimator.layerPriority;
                }
            }

            foreach (var slot in state.System.Slots)
            {
                if (slot.SourceActionMergeAnimator != null)
                {
                    slot.SourceActionMergeAnimator.layerPriority = CheckedIncrement(
                        fxPriority,
                        slot,
                        state.Report);
                    fxPriority = slot.SourceActionMergeAnimator.layerPriority;
                }
            }

            foreach (var slot in state.System.Slots)
            {
                if (slot.SourceFxMergeAnimator != null)
                {
                    slot.SourceFxMergeAnimator.layerPriority = CheckedIncrement(fxPriority, slot, state.Report);
                    fxPriority = slot.SourceFxMergeAnimator.layerPriority;
                }
            }

            foreach (var slot in state.System.Slots)
            {
                if (slot.CoreMergeAnimator != null)
                {
                    slot.CoreMergeAnimator.layerPriority = CheckedIncrement(fxPriority, slot, state.Report);
                    fxPriority = slot.CoreMergeAnimator.layerPriority;
                }
            }

            foreach (var slot in state.System.Slots)
            {
                if (slot.TrackingMergeAnimator != null)
                {
                    slot.TrackingMergeAnimator.layerPriority = CheckedIncrement(fxPriority, slot, state.Report);
                    fxPriority = slot.TrackingMergeAnimator.layerPriority;
                }
            }

            foreach (var slot in state.System.Slots)
            {
                if (slot.PhantomViewMergeAnimator != null)
                {
                    slot.PhantomViewMergeAnimator.layerPriority = CheckedIncrement(fxPriority, slot, state.Report);
                    fxPriority = slot.PhantomViewMergeAnimator.layerPriority;
                }
            }
        }

        private static int ExternalMaxPriority(
            ModularAvatarMergeAnimator[] animators,
            UnityEngine.Transform phantomRoot,
            VRCAvatarDescriptor.AnimLayerType type)
        {
            return animators
                .Where(animator => animator != null
                                   && animator.layerType == type
                                   && (phantomRoot == null
                                       || (animator.transform != phantomRoot
                                           && !animator.transform.IsChildOf(phantomRoot))))
                .Select(animator => animator.layerPriority)
                .DefaultIfEmpty(0)
                .Max();
        }

        private static int CheckedIncrement(int priority, PhantomSlotBuildState slot, PhantomBuildReport report)
        {
            if (priority == int.MaxValue)
            {
                report.Error(
                    $"Slot '{slot.SlotId}' cannot allocate a Merge Animator priority because the base avatar already uses int.MaxValue.",
                    slot.CloneRoot);
                return int.MaxValue;
            }

            return priority + 1;
        }
    }
}
