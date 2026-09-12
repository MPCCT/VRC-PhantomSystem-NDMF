using L = MPCCT.PhantomSystem.Editor.PhantomLocalization;
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
                    ? L.S("alerts.title")
                    : L.F("alerts.title.count", result.Issues.Count),
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
                EditorGUILayout.LabelField(L.S("alerts.checking"), EditorStyles.miniLabel);
            }
            else
            {
                if (result.Issues.Count == 0)
                {
                    EditorGUILayout.LabelField(L.S("alerts.none"), EditorStyles.miniLabel);
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
                    L.S("alerts.compatibility"),
                    L.S("alerts.notScanned"),
                    EditorStyles.miniLabel);
                return;
            }

            var compatibilitySummary =
                L.F("alerts.components", result.NdmfEditorOnlyComponentCount);
            if (result.UnclassifiedComponentCount > 0)
            {
                compatibilitySummary +=
                    L.F("alerts.unclassified", result.UnclassifiedComponentTypeCount,
                        result.UnclassifiedComponentCount);
            }
            else
            {
                compatibilitySummary += L.S("alerts.classified");
            }

            EditorGUILayout.LabelField(
                L.S("alerts.compatibility"),
                compatibilitySummary,
                EditorStyles.miniLabel);
        }

        private void DrawValidationIssue(
            PhantomValidationIssue issue,
            VRCAvatarDescriptor source)
        {
            var displayMessage = string.IsNullOrEmpty(issue.Code)
                ? issue.Message
                : $"[{issue.Code}] {issue.Message}";
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
                new GUIContent(displayMessage),
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
                    ? L.F("common.select.count", selectionTargets.Length)
                    : L.S("common.select");
                if (GUI.Button(selectRect, selectLabel))
                {
                    Selection.objects = selectionTargets;
                    EditorGUIUtility.PingObject(selectionTargets[0]);
                }
            }

            EditorGUI.HelpBox(
                messageRect,
                displayMessage,
                MessageTypeFor(issue.Severity));
        }

        private string OverallStatus()
        {
            if (validationReport == null)
            {
                return L.S("status.checking");
            }

            if (validationReport.HasErrors)
            {
                return L.S("status.errors");
            }

            if (validationReport.Slots.Any(slot => slot.HasWarnings))
            {
                return L.S("status.warnings");
            }

            return slots.arraySize == 0 ? L.S("status.noSlots") : L.S("status.ready");
        }

        private string SlotStatus(int slotIndex)
        {
            var result = validationReport != null && slotIndex < validationReport.Slots.Count
                ? validationReport.Slots[slotIndex]
                : null;
            var bitCost = parameterPlan != null && slotIndex < parameterPlan.Slots.Count
                ? (int?)parameterPlan.Slots[slotIndex].FinalContributionCost
                : null;
            return FormatSlotStatus(result, bitCost);
        }

        internal static string FormatSlotStatus(
            PhantomSlotValidationResult result,
            int? bitCost)
        {
            if (result == null)
            {
                return L.S("status.checking");
            }

            if (result.HasErrors)
            {
                return L.S("status.error");
            }

            if (result.HasWarnings)
            {
                var warningCount = result.Issues.Count(issue =>
                    issue.Severity == PhantomValidationSeverity.Warning);
                var warningText = warningCount == 1 ? L.S("status.warning.one") : L.F("status.warning.many", warningCount);
                return bitCost.HasValue
                    ? L.F("status.warning.cost", warningText, bitCost.Value)
                    : warningText;
            }

            return bitCost.HasValue
                ? L.F("common.bits", bitCost.Value)
                : L.S("status.ready");
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
                case PhantomValidationSeverity.ConfigurationError:
                case PhantomValidationSeverity.InternalError:
                    return MessageType.Error;
                case PhantomValidationSeverity.Warning:
                    return MessageType.Warning;
                case PhantomValidationSeverity.Info:
                    return MessageType.Info;
                default:
                    return MessageType.None;
            }
        }
    }
}
