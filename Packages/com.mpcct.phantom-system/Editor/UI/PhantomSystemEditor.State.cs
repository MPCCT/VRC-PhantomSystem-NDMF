using System.Collections.Generic;
using UnityEditor;

namespace MPCCT.PhantomSystem.Editor
{
    public sealed partial class PhantomSystemEditor
    {
        private readonly Dictionary<int, bool> slotFoldouts = new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> sharedParameterFoldouts =
            new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> slotAdvancedFoldouts =
            new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> slotAlertFoldouts =
            new Dictionary<int, bool>();

        private void ClearFoldoutCaches()
        {
            slotFoldouts.Clear();
            sharedParameterFoldouts.Clear();
            slotAdvancedFoldouts.Clear();
            slotAlertFoldouts.Clear();
        }

        private string SlotFoldoutKey(int slotIndex)
        {
            return $"{slotFoldoutStateKey}.{slotIndex}";
        }

        private bool GetSlotFoldout(int slotIndex)
        {
            return slotFoldouts.TryGetValue(slotIndex, out var expanded)
                ? expanded
                : SessionState.GetBool(SlotFoldoutKey(slotIndex), true);
        }

        private void SetSlotFoldout(int slotIndex, bool expanded)
        {
            slotFoldouts[slotIndex] = expanded;
            SessionState.SetBool(SlotFoldoutKey(slotIndex), expanded);
        }

        private void SwapSlotFoldouts(int firstIndex, int secondIndex)
        {
            var firstExpanded = GetSlotFoldout(firstIndex);
            var secondExpanded = GetSlotFoldout(secondIndex);
            SetSlotFoldout(firstIndex, secondExpanded);
            SetSlotFoldout(secondIndex, firstExpanded);

            var firstSharedExpanded = GetSharedParameterFoldout(firstIndex);
            var secondSharedExpanded = GetSharedParameterFoldout(secondIndex);
            SetSharedParameterFoldout(firstIndex, secondSharedExpanded);
            SetSharedParameterFoldout(secondIndex, firstSharedExpanded);

            var firstAdvancedExpanded = GetSlotAdvancedFoldout(firstIndex);
            var secondAdvancedExpanded = GetSlotAdvancedFoldout(secondIndex);
            SetSlotAdvancedFoldout(firstIndex, secondAdvancedExpanded);
            SetSlotAdvancedFoldout(secondIndex, firstAdvancedExpanded);

            var firstAlertExpanded = GetSlotAlertFoldout(firstIndex);
            var secondAlertExpanded = GetSlotAlertFoldout(secondIndex);
            SetSlotAlertFoldout(firstIndex, secondAlertExpanded);
            SetSlotAlertFoldout(secondIndex, firstAlertExpanded);
        }

        private void RemoveSlotFoldout(int removedIndex)
        {
            for (var index = removedIndex; index < slots.arraySize - 1; index++)
            {
                SetSlotFoldout(index, GetSlotFoldout(index + 1));
                SetSharedParameterFoldout(
                    index,
                    GetSharedParameterFoldout(index + 1));
                SetSlotAdvancedFoldout(
                    index,
                    GetSlotAdvancedFoldout(index + 1));
                SetSlotAlertFoldout(
                    index,
                    GetSlotAlertFoldout(index + 1));
            }

            var finalIndex = slots.arraySize - 1;
            slotFoldouts.Remove(finalIndex);
            SessionState.EraseBool(SlotFoldoutKey(finalIndex));
            sharedParameterFoldouts.Remove(finalIndex);
            SessionState.EraseBool(SharedParameterFoldoutKey(finalIndex));
            slotAdvancedFoldouts.Remove(finalIndex);
            SessionState.EraseBool(SlotAdvancedFoldoutKey(finalIndex));
            slotAlertFoldouts.Remove(finalIndex);
            SessionState.EraseBool(SlotAlertFoldoutKey(finalIndex));
        }

        private string SharedParameterFoldoutKey(int slotIndex)
        {
            return $"{slotFoldoutStateKey}.SharedParameters.{slotIndex}";
        }

        private bool GetSharedParameterFoldout(int slotIndex)
        {
            return sharedParameterFoldouts.TryGetValue(slotIndex, out var expanded)
                ? expanded
                : SessionState.GetBool(SharedParameterFoldoutKey(slotIndex), false);
        }

        private void SetSharedParameterFoldout(int slotIndex, bool expanded)
        {
            sharedParameterFoldouts[slotIndex] = expanded;
            SessionState.SetBool(SharedParameterFoldoutKey(slotIndex), expanded);
        }

        private string SlotAdvancedFoldoutKey(int slotIndex)
        {
            return $"{slotFoldoutStateKey}.Advanced.{slotIndex}";
        }

        private bool GetSlotAdvancedFoldout(int slotIndex)
        {
            return slotAdvancedFoldouts.TryGetValue(slotIndex, out var expanded)
                ? expanded
                : SessionState.GetBool(SlotAdvancedFoldoutKey(slotIndex), false);
        }

        private void SetSlotAdvancedFoldout(int slotIndex, bool expanded)
        {
            slotAdvancedFoldouts[slotIndex] = expanded;
            SessionState.SetBool(SlotAdvancedFoldoutKey(slotIndex), expanded);
        }

        private string SlotAlertFoldoutKey(int slotIndex)
        {
            return $"{slotFoldoutStateKey}.Alerts.{slotIndex}";
        }

        private bool GetSlotAlertFoldout(int slotIndex)
        {
            return slotAlertFoldouts.TryGetValue(slotIndex, out var expanded)
                ? expanded
                : SessionState.GetBool(SlotAlertFoldoutKey(slotIndex), true);
        }

        private void SetSlotAlertFoldout(int slotIndex, bool expanded)
        {
            slotAlertFoldouts[slotIndex] = expanded;
            SessionState.SetBool(SlotAlertFoldoutKey(slotIndex), expanded);
        }
    }
}
