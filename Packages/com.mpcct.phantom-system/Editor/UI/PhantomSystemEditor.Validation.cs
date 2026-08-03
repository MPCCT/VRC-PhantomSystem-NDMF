using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem.Editor
{
    public sealed partial class PhantomSystemEditor
    {
        private void DrawValidation(int slotIndex, VRCAvatarDescriptor source)
        {
            EditorGUILayout.Space();

            var result = validationReport != null && slotIndex < validationReport.Slots.Count
                ? validationReport.Slots[slotIndex]
                : null;
            var expanded = GetSlotAlertFoldout(slotIndex);
            var nextExpanded = EditorGUILayout.Foldout(
                expanded,
                result == null
                    ? "Review Any Alerts"
                    : $"Review Any Alerts ({result.Issues.Count})",
                true);
            if (nextExpanded != expanded)
            {
                SetSlotAlertFoldout(slotIndex, nextExpanded);
            }

            if (!nextExpanded)
            {
                return;
            }

            if (result == null)
            {
                EditorGUILayout.LabelField("Checking source avatar...", EditorStyles.miniLabel);
            }
            else
            {
                if (result.Issues.Count == 0)
                {
                    EditorGUILayout.LabelField("No source validation alerts.", EditorStyles.miniLabel);
                }

                foreach (var issue in result.Issues)
                {
                    DrawValidationIssue(issue, source);
                }
            }

            if (result == null
                || result.CompatibilityStatus == PhantomCompatibilityStatus.NotScanned)
            {
                EditorGUILayout.LabelField(
                    "Component Compatibility",
                    "Not scanned",
                    EditorStyles.miniLabel);
                return;
            }

            var compatibilitySummary =
                $"{result.NdmfEditorOnlyComponentCount} NDMF component(s)";
            if (result.UnclassifiedComponentCount > 0)
            {
                compatibilitySummary +=
                    $" · {result.UnclassifiedComponentTypeCount} warning(s) across "
                    + $"{result.UnclassifiedComponentCount} component(s)";
            }
            else
            {
                compatibilitySummary += " · no unclassified script components";
            }

            EditorGUILayout.LabelField(
                "Component Compatibility",
                compatibilitySummary,
                EditorStyles.miniLabel);
        }

        private void DrawValidationIssue(
            PhantomValidationIssue issue,
            VRCAvatarDescriptor source)
        {
            var selectionTargets = (issue.SelectionTargets
                    ?? System.Array.Empty<UnityEngine.Object>())
                .Where(selectionTarget => selectionTarget != null
                                          && selectionTarget != source
                                          && selectionTarget != target)
                .Distinct()
                .ToArray();
            var canSelect = selectionTargets.Length > 0;
            const float selectButtonWidth = 96f;
            const float columnGap = 4f;
            const float minimumRowHeight = 38f;

            var estimatedRowWidth = Mathf.Max(
                160f,
                EditorGUIUtility.currentViewWidth - 52f - EditorGUI.indentLevel * 15f);
            var messageWidth = estimatedRowWidth
                - (canSelect ? selectButtonWidth + columnGap : 0f);
            var messageHeight = EditorStyles.helpBox.CalcHeight(
                new GUIContent(issue.Message),
                Mathf.Max(80f, messageWidth - 32f));
            var rowHeight = Mathf.Max(
                minimumRowHeight,
                messageHeight + 6f);
            var rowRect = EditorGUI.IndentedRect(
                EditorGUILayout.GetControlRect(false, rowHeight));
            var messageRect = rowRect;

            if (canSelect)
            {
                messageRect.width = Mathf.Max(
                    0f,
                    rowRect.width - selectButtonWidth - columnGap);
                var selectRect = new Rect(
                    messageRect.xMax + columnGap,
                    rowRect.y,
                    selectButtonWidth,
                    rowRect.height);
                var selectLabel = selectionTargets.Length > 1
                    ? $"Select ({selectionTargets.Length})"
                    : "Select";
                if (GUI.Button(selectRect, selectLabel))
                {
                    Selection.objects = selectionTargets;
                    EditorGUIUtility.PingObject(selectionTargets[0]);
                }
            }

            EditorGUI.HelpBox(
                messageRect,
                issue.Message,
                MessageTypeFor(issue.Severity));
        }

        private string OverallStatus()
        {
            if (validationReport == null)
            {
                return "Checking...";
            }

            if (validationReport.Slots.Any(slot => slot.HasErrors))
            {
                return "Has Errors";
            }

            if (validationReport.Slots.Any(slot => slot.HasWarnings))
            {
                return "Warnings";
            }

            return slots.arraySize == 0 ? "No Slots" : "Ready";
        }

        private string SlotStatus(int slotIndex)
        {
            var result = validationReport != null && slotIndex < validationReport.Slots.Count
                ? validationReport.Slots[slotIndex]
                : null;
            if (result == null)
            {
                return "Checking...";
            }

            if (result.HasErrors)
            {
                return "Error";
            }

            return parameterAnalysis != null && slotIndex < parameterAnalysis.Slots.Count
                ? $"{parameterAnalysis.Slots[slotIndex].FinalContributionCost} bits"
                : "Ready";
        }

        private static void DrawIndentedHelpBox(string message, MessageType messageType)
        {
            const float minimumHeight = 38f;
            var estimatedWidth = Mathf.Max(
                160f,
                EditorGUIUtility.currentViewWidth - 52f - EditorGUI.indentLevel * 15f);
            var messageHeight = EditorStyles.helpBox.CalcHeight(
                new GUIContent(message),
                Mathf.Max(80f, estimatedWidth - 32f));
            var rect = EditorGUI.IndentedRect(
                EditorGUILayout.GetControlRect(
                    false,
                    Mathf.Max(minimumHeight, messageHeight + 6f)));
            EditorGUI.HelpBox(rect, message, messageType);
        }

        private static MessageType MessageTypeFor(PhantomValidationSeverity severity)
        {
            switch (severity)
            {
                case PhantomValidationSeverity.Error:
                    return MessageType.Error;
                case PhantomValidationSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.None;
            }
        }
    }
}
