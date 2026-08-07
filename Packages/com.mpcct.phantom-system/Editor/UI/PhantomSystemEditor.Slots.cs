using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem.Editor
{
    public sealed partial class PhantomSystemEditor
    {
        private enum SlotListAction
        {
            None,
            MoveUp,
            MoveDown,
            Remove
        }

        private bool DrawSlots()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Slots", EditorStyles.boldLabel);

            var changed = false;
            var action = SlotListAction.None;
            var actionIndex = -1;

            for (var slotIndex = 0; slotIndex < slots.arraySize; slotIndex++)
            {
                var slotProperty = slots.GetArrayElementAtIndex(slotIndex);
                changed |= DrawSlotCard(slotIndex, slotProperty, out action);
                if (action != SlotListAction.None)
                {
                    actionIndex = slotIndex;
                    break;
                }
            }

            if (actionIndex >= 0)
            {
                changed |= ApplySlotListAction(actionIndex, action);
            }

            if (slots.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No slots are configured. Add a slot and assign a humanoid source avatar.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add Slot", GUILayout.Width(120f)))
                {
                    AddSlot();
                    changed = true;
                }
            }

            return changed;
        }

        private bool DrawSlotCard(
            int slotIndex,
            SerializedProperty slotProperty,
            out SlotListAction action)
        {
            action = SlotListAction.None;
            var changed = false;
            var idProperty = slotProperty.FindPropertyRelative("id");
            var sourceProperty = slotProperty.FindPropertyRelative("phantomAvatar");
            var spawnProperty = slotProperty.FindPropertyRelative("spawnPositionOverride");
            var includePhantomMenu =
                slotProperty.FindPropertyRelative("includePhantomMenu");
            var prefixProperty = slotProperty.FindPropertyRelative("parameterPrefix");
            var renameProperty = slotProperty.FindPropertyRelative("renamePhantomParameters");
            var sharedNames = slotProperty.FindPropertyRelative("sharedParameterNames");
            var removeSourceControls = slotProperty.FindPropertyRelative("removeSourceControls");
            var useRotationConstraint = slotProperty.FindPropertyRelative("useRotationConstraint");
            var rotationSolveInWorldSpace =
                slotProperty.FindPropertyRelative("rotationSolveInWorldSpace");
            var overridePhysBoneImmobileType =
                slotProperty.FindPropertyRelative("overridePhysBoneImmobileType");
            var tryConvertAnimatorTrackingControl =
                slotProperty.FindPropertyRelative("tryConvertAnimatorTrackingControl");
            var enablePhantomGrabbing =
                slotProperty.FindPropertyRelative("enablePhantomGrabbing");
            var enableScaleControl =
                slotProperty.FindPropertyRelative("enableScaleControl");
            var enablePhantomView =
                slotProperty.FindPropertyRelative("enablePhantomView");
            var slotName = string.IsNullOrWhiteSpace(idProperty.stringValue)
                ? $"Slot{slotIndex + 1}"
                : idProperty.stringValue.Trim();
            var source = sourceProperty.objectReferenceValue as VRCAvatarDescriptor;

            if (!slotFoldouts.TryGetValue(slotIndex, out var expanded))
            {
                expanded = SessionState.GetBool(SlotFoldoutKey(slotIndex), true);
                slotFoldouts[slotIndex] = expanded;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var headerRect = EditorGUILayout.GetControlRect(
                    false,
                    EditorGUIUtility.singleLineHeight);
                headerRect.xMin += 12f;

                var removeRect = new Rect(
                    headerRect.xMax - 54f,
                    headerRect.y,
                    54f,
                    headerRect.height);
                var downRect = new Rect(
                    removeRect.xMin - 54f,
                    headerRect.y,
                    54f,
                    headerRect.height);
                var upRect = new Rect(
                    downRect.xMin - 54f,
                    headerRect.y,
                    54f,
                    headerRect.height);
                var statusRect = new Rect(
                    upRect.xMin - 80f,
                    headerRect.y,
                    76f,
                    headerRect.height);
                var foldoutRect = new Rect(
                    headerRect.x,
                    headerRect.y,
                    Mathf.Max(0f, statusRect.xMin - headerRect.x - 4f),
                    headerRect.height);

                expanded = EditorGUI.Foldout(
                    foldoutRect,
                    expanded,
                    source == null ? slotName : $"{slotName} · {source.name}",
                    true);
                EditorGUI.LabelField(
                    statusRect,
                    SlotStatus(slotIndex),
                    EditorStyles.miniLabel);
                if (GUI.Button(upRect, "Up", EditorStyles.miniButtonLeft))
                {
                    action = SlotListAction.MoveUp;
                }

                if (GUI.Button(downRect, "Down", EditorStyles.miniButtonMid))
                {
                    action = SlotListAction.MoveDown;
                }

                if (GUI.Button(removeRect, "Remove", EditorStyles.miniButtonRight))
                {
                    action = SlotListAction.Remove;
                }

                SetSlotFoldout(slotIndex, expanded);
                if (!expanded)
                {
                    return changed;
                }

                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(idProperty, new GUIContent("Slot Name"));
                    EditorGUILayout.PropertyField(sourceProperty, new GUIContent("Phantom Avatar"));
                    EditorGUILayout.PropertyField(spawnProperty, new GUIContent("Spawn Override"));
                    using (new EditorGUI.DisabledScope(removeSourceControls.boolValue))
                    {
                        EditorGUILayout.PropertyField(
                            includePhantomMenu,
                            new GUIContent(
                                "Include Phantom Menu",
                                "Include the final Expression Menu produced by this phantom avatar's NDMF prebake."));
                    }
                    EditorGUILayout.PropertyField(
                        enablePhantomGrabbing,
                        new GUIContent(
                            "Enable Phantom Grabbing",
                            "While the phantom is frozen, Rock&Roll gesture lets either hand move its Hips through contact grabbing, while a generated Humanoid PhysBone proxy lets its body react and be posed. Turning Freeze off disables Phantom Grabbing and returns the phantom to base-avatar following."));
                    EditorGUILayout.PropertyField(
                        enableScaleControl,
                        new GUIContent(
                            "Enable Scale Control",
                            "Add per-slot radial scale control, reset, and X-axis mirror controls."));
                    EditorGUILayout.PropertyField(
                        enablePhantomView,
                        new GUIContent(
                            "Enable Phantom View",
                            "Add a local stereo view rendered from the phantom's Humanoid Head."));

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Parameter Settings", EditorStyles.boldLabel);
                    using (new EditorGUI.DisabledScope(removeSourceControls.boolValue))
                    {
                        EditorGUILayout.PropertyField(prefixProperty, new GUIContent("Parameter Prefix"));
                        EditorGUILayout.PropertyField(
                            renameProperty,
                            new GUIContent("Namespace Phantom Parameters"));
                        changed |= DrawParameterSharing(
                            slotIndex,
                            renameProperty,
                            sharedNames);
                    }

                    if (removeSourceControls.boolValue)
                    {
                        EditorGUILayout.LabelField(
                            "Source FX, Action, Gesture, parameters, and menu are excluded.",
                            EditorStyles.miniLabel);
                    }
                    DrawSlotAdvancedOptions(
                        slotIndex,
                        removeSourceControls,
                        useRotationConstraint,
                        rotationSolveInWorldSpace,
                        overridePhysBoneImmobileType,
                        tryConvertAnimatorTrackingControl);

                    DrawValidation(slotIndex, source);
                }
            }

            return changed;
        }

        private void DrawSlotAdvancedOptions(
            int slotIndex,
            SerializedProperty removeSourceControls,
            SerializedProperty useRotationConstraint,
            SerializedProperty rotationSolveInWorldSpace,
            SerializedProperty overridePhysBoneImmobileType,
            SerializedProperty tryConvertAnimatorTrackingControl)
        {
            EditorGUILayout.Space();
            var expanded = GetSlotAdvancedFoldout(slotIndex);
            var nextExpanded = EditorGUILayout.Foldout(
                expanded,
                "Advanced",
                true);
            if (nextExpanded != expanded)
            {
                SetSlotAdvancedFoldout(slotIndex, nextExpanded);
            }

            if (!nextExpanded)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(
                    removeSourceControls,
                    new GUIContent(
                        "Remove Source Controls",
                        "Exclude this phantom's prebaked FX, Action, and Gesture controllers, source parameter definitions, and final source Expression Menu. PhantomSystem Core controls remain installed."));
                EditorGUILayout.PropertyField(
                    useRotationConstraint,
                    new GUIContent(
                        "Use Rotation Constraint",
                        "Use Rotation Constraints instead of Parent Constraints for non-Hips humanoid bones."));
                using (new EditorGUI.DisabledScope(!useRotationConstraint.boolValue))
                {
                    EditorGUILayout.PropertyField(
                        rotationSolveInWorldSpace,
                        new GUIContent(
                            "Rotation Solve In World Space",
                            "Solve generated Rotation Constraints in world space."));
                }
                EditorGUILayout.PropertyField(
                    overridePhysBoneImmobileType,
                    new GUIContent(
                        "Override PhysBone Immobile Type",
                        "Set every PhysBone in this slot to All Motion. This can fix cases where a frozen phantom's PhysBones move along with the base avatar, but it overrides the source settings and may break PhysBone behavior."));
                using (new EditorGUI.DisabledScope(removeSourceControls.boolValue))
                {
                    EditorGUILayout.PropertyField(
                        tryConvertAnimatorTrackingControl,
                        new GUIContent(
                            "Try Convert Animator Tracking Control",
                            "Convert supported Animator Tracking Control behaviors into PhantomSystem bone-group synchronization. Unsupported face simulation is reported as a partial conversion."));
                }
            }
        }

        private bool ApplySlotListAction(int slotIndex, SlotListAction action)
        {
            switch (action)
            {
                case SlotListAction.MoveUp:
                    if (slotIndex <= 0)
                    {
                        return false;
                    }

                    SwapSlotFoldouts(slotIndex, slotIndex - 1);
                    slots.MoveArrayElement(slotIndex, slotIndex - 1);
                    return true;
                case SlotListAction.MoveDown:
                    if (slotIndex >= slots.arraySize - 1)
                    {
                        return false;
                    }

                    SwapSlotFoldouts(slotIndex, slotIndex + 1);
                    slots.MoveArrayElement(slotIndex, slotIndex + 1);
                    return true;
                case SlotListAction.Remove:
                    RemoveSlotFoldout(slotIndex);
                    slots.DeleteArrayElementAtIndex(slotIndex);
                    return true;
                default:
                    return false;
            }
        }

        private void AddSlot()
        {
            var uniqueName = NextUniqueSlotName();
            var newIndex = slots.arraySize;
            slots.arraySize++;
            var slotProperty = slots.GetArrayElementAtIndex(newIndex);
            slotProperty.FindPropertyRelative("id").stringValue = uniqueName;
            slotProperty.FindPropertyRelative("phantomAvatar").objectReferenceValue = null;
            slotProperty.FindPropertyRelative("spawnPositionOverride").objectReferenceValue = null;
            slotProperty.FindPropertyRelative("includePhantomMenu").boolValue = true;
            slotProperty.FindPropertyRelative("parameterPrefix").stringValue = "";
            slotProperty.FindPropertyRelative("renamePhantomParameters").boolValue = true;
            slotProperty.FindPropertyRelative("sharedParameterNames").ClearArray();
            slotProperty.FindPropertyRelative("removeSourceControls").boolValue = false;
            slotProperty.FindPropertyRelative("useRotationConstraint").boolValue = false;
            slotProperty.FindPropertyRelative("rotationSolveInWorldSpace").boolValue = false;
            slotProperty.FindPropertyRelative("overridePhysBoneImmobileType").boolValue = false;
            slotProperty.FindPropertyRelative("tryConvertAnimatorTrackingControl").boolValue = true;
            slotProperty.FindPropertyRelative("enablePhantomGrabbing").boolValue = true;
            slotProperty.FindPropertyRelative("enableScaleControl").boolValue = true;
            SetSlotFoldout(newIndex, true);
            SetSharedParameterFoldout(newIndex, false);
            SetSlotAdvancedFoldout(newIndex, false);
            SetSlotAlertFoldout(newIndex, true);
        }

        private string NextUniqueSlotName()
        {
            var usedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < slots.arraySize; index++)
            {
                var value = slots.GetArrayElementAtIndex(index)
                    .FindPropertyRelative("id")
                    .stringValue;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    usedNames.Add(value.Trim());
                }
            }

            if (slots.arraySize == 0)
            {
                return PhantomSlot.DefaultId;
            }

            var suffix = Mathf.Max(2, slots.arraySize + 1);
            while (usedNames.Contains($"Slot{suffix}"))
            {
                suffix++;
            }

            return $"Slot{suffix}";
        }
    }
}
