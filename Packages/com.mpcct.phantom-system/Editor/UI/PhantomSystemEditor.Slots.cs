using L = MPCCT.PhantomSystem.Editor.PhantomLocalization;
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
            EditorGUILayout.LabelField(L.S("slots.title"), EditorStyles.boldLabel);

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
                    L.S("slots.empty"),
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(L.S("slots.add"), GUILayout.Width(120f)))
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
            var phantomViewNearClipPlane =
                slotProperty.FindPropertyRelative("phantomViewNearClipPlane");
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
                var foldoutRect = new Rect(
                    headerRect.x,
                    headerRect.y,
                    Mathf.Max(0f, upRect.xMin - headerRect.x - 4f),
                    headerRect.height);

                expanded = EditorGUI.Foldout(
                    foldoutRect,
                    expanded,
                    source == null ? slotName : $"{slotName} · {source.name}",
                    true);
                if (GUI.Button(upRect, L.S("common.up"), EditorStyles.miniButtonLeft))
                {
                    action = SlotListAction.MoveUp;
                }

                if (GUI.Button(downRect, L.S("common.down"), EditorStyles.miniButtonMid))
                {
                    action = SlotListAction.MoveDown;
                }

                if (GUI.Button(removeRect, L.S("common.remove"), EditorStyles.miniButtonRight))
                {
                    action = SlotListAction.Remove;
                }

                EditorGUILayout.LabelField(SlotStatus(slotIndex), EditorStyles.miniLabel);
                SetSlotFoldout(slotIndex, expanded);
                if (!expanded)
                {
                    return changed;
                }

                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(idProperty, new GUIContent(L.S("slot.name")));
                    EditorGUILayout.PropertyField(sourceProperty, new GUIContent(L.S("slot.source")));
                    EditorGUILayout.PropertyField(spawnProperty, new GUIContent(L.S("slot.spawn")));
                    using (new EditorGUI.DisabledScope(removeSourceControls.boolValue))
                    {
                        EditorGUILayout.PropertyField(
                            includePhantomMenu,
                            L.G("slot.includeMenu"));
                    }
                    EditorGUILayout.PropertyField(
                        enablePhantomGrabbing,
                        L.G("slot.grabbing"));
                    EditorGUILayout.PropertyField(
                        enableScaleControl,
                        L.G("slot.scale"));
                    EditorGUILayout.PropertyField(
                        enablePhantomView,
                        L.G("slot.view"));

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(L.S("slot.parameters"), EditorStyles.boldLabel);
                    using (new EditorGUI.DisabledScope(removeSourceControls.boolValue))
                    {
                        EditorGUILayout.PropertyField(prefixProperty, new GUIContent(L.S("slot.prefix")));
                        EditorGUILayout.PropertyField(
                            renameProperty,
                            new GUIContent(L.S("slot.namespace")));
                        changed |= DrawParameterSharing(
                            slotIndex,
                            renameProperty,
                            sharedNames);
                    }

                    if (removeSourceControls.boolValue)
                    {
                        EditorGUILayout.LabelField(
                            L.S("slot.sourceExcluded"),
                            EditorStyles.miniLabel);
                    }
                    DrawSlotAdvancedOptions(
                        slotIndex,
                        removeSourceControls,
                        useRotationConstraint,
                        rotationSolveInWorldSpace,
                        overridePhysBoneImmobileType,
                        tryConvertAnimatorTrackingControl,
                        enablePhantomView,
                        phantomViewNearClipPlane);

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
            SerializedProperty tryConvertAnimatorTrackingControl,
            SerializedProperty enablePhantomView,
            SerializedProperty phantomViewNearClipPlane)
        {
            EditorGUILayout.Space();
            var expanded = GetSlotAdvancedFoldout(slotIndex);
            var nextExpanded = EditorGUILayout.Foldout(
                expanded,
                L.S("slot.advanced"),
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
                    L.G("slot.removeControls"));
                EditorGUILayout.PropertyField(
                    useRotationConstraint,
                    L.G("slot.rotation"));
                using (new EditorGUI.DisabledScope(!useRotationConstraint.boolValue))
                {
                    EditorGUILayout.PropertyField(
                        rotationSolveInWorldSpace,
                        L.G("slot.worldRotation"));
                }
                EditorGUILayout.PropertyField(
                    overridePhysBoneImmobileType,
                    L.G("slot.immobile"));
                using (new EditorGUI.DisabledScope(removeSourceControls.boolValue))
                {
                    EditorGUILayout.PropertyField(
                        tryConvertAnimatorTrackingControl,
                        L.G("slot.tracking"));
                }
                using (new EditorGUI.DisabledScope(!enablePhantomView.boolValue))
                {
                    EditorGUI.BeginChangeCheck();
                    var nearClipPlane = EditorGUILayout.FloatField(
                        L.G("slot.nearClip"),
                        PhantomViewBuilder.NormalizeNearClipPlane(
                            phantomViewNearClipPlane.floatValue));
                    if (EditorGUI.EndChangeCheck())
                    {
                        phantomViewNearClipPlane.floatValue =
                            PhantomViewBuilder.NormalizeNearClipPlane(nearClipPlane);
                    }
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
            slotProperty.FindPropertyRelative("overridePhysBoneImmobileType").boolValue = true;
            slotProperty.FindPropertyRelative("tryConvertAnimatorTrackingControl").boolValue = true;
            slotProperty.FindPropertyRelative("enablePhantomGrabbing").boolValue = true;
            slotProperty.FindPropertyRelative("enableScaleControl").boolValue = true;
            slotProperty.FindPropertyRelative("enablePhantomView").boolValue = true;
            slotProperty.FindPropertyRelative("phantomViewNearClipPlane").floatValue =
                PhantomSlot.DefaultPhantomViewNearClipPlane;
            SetSlotFoldout(newIndex, true);
            SetSharedParameterFoldout(newIndex, false);
            SetSlotAdvancedFoldout(newIndex, false);
            SetSlotAlertFoldout(newIndex, true);
        }

        private string NextUniqueSlotName()
        {
            var usedNames = new HashSet<string>(System.StringComparer.Ordinal);
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
