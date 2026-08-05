using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;

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

            var priority = context.AvatarRootObject
                .GetComponentsInChildren<ModularAvatarMergeAnimator>(true)
                .Where(animator => animator != null
                                   && (phantomRoot == null
                                       || (animator.transform != phantomRoot
                                           && !animator.transform.IsChildOf(phantomRoot))))
                .Select(animator => animator.layerPriority)
                .DefaultIfEmpty(0)
                .Max();

            foreach (var slot in state.System.Slots)
            {
                if (slot.OriginalMergeAnimator != null)
                {
                    slot.OriginalMergeAnimator.layerPriority = CheckedIncrement(priority, slot, state.Report);
                    priority = slot.OriginalMergeAnimator.layerPriority;
                }
            }

            foreach (var slot in state.System.Slots)
            {
                if (slot.CoreMergeAnimator != null)
                {
                    slot.CoreMergeAnimator.layerPriority = CheckedIncrement(priority, slot, state.Report);
                    priority = slot.CoreMergeAnimator.layerPriority;
                }
            }

            foreach (var slot in state.System.Slots)
            {
                if (slot.TrackingMergeAnimator != null)
                {
                    slot.TrackingMergeAnimator.layerPriority = CheckedIncrement(priority, slot, state.Report);
                    priority = slot.TrackingMergeAnimator.layerPriority;
                }
            }
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
