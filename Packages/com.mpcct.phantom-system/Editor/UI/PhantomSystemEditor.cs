using System.Linq;
using UnityEditor;
using UnityEngine;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    [CustomEditor(typeof(PhantomAuthoring))]
    public sealed partial class PhantomSystemEditor : UnityEditor.Editor
    {
        private const string SlotFoldoutStatePrefix =
            "MPCCT.PhantomSystem.Editor.SlotFoldout.";

        private SerializedProperty slots;
        private SerializedProperty options;
        private PhantomSystemParameterAnalysis parameterAnalysis;
        private PhantomSourceValidationReport validationReport;
        private string slotFoldoutStateKey;
        private bool refreshPending;

        private void OnEnable()
        {
            EnsureCoreMenuInstaller();
            slots = serializedObject.FindProperty("slots");
            options = serializedObject.FindProperty("options");
            slotFoldoutStateKey = SlotFoldoutStatePrefix
                + GlobalObjectId.GetGlobalObjectIdSlow(target);
            ClearFoldoutCaches();
            Undo.undoRedoPerformed += ScheduleRefresh;
            EditorApplication.hierarchyChanged += ScheduleRefresh;
            ScheduleRefresh();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= ScheduleRefresh;
            EditorApplication.hierarchyChanged -= ScheduleRefresh;
            EditorApplication.delayCall -= RefreshAnalysis;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPhantomHeader();
            var changed = DrawSlots();
            DrawSystemOptions();

            if (serializedObject.ApplyModifiedProperties() || changed)
            {
                RefreshAnalysis();
            }
        }

        private void DrawPhantomHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("PhantomSystem", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(OverallStatus(), EditorStyles.miniLabel, GUILayout.Width(90f));
                if (GUILayout.Button("Refresh", GUILayout.Width(72f)))
                {
                    serializedObject.ApplyModifiedProperties();
                    RefreshAnalysis();
                    serializedObject.Update();
                }
            }

            var totalCost = parameterAnalysis?.Slots.Sum(slot => slot.FinalContributionCost);
            EditorGUILayout.LabelField(
                totalCost.HasValue
                    ? $"{slots.arraySize} slot(s) · estimated PhantomSystem contribution {totalCost.Value} bits"
                    : $"{slots.arraySize} slot(s) · analyzing parameters...",
                EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(
                "Each source avatar is independently prebaked through NDMF. Configure and validate each slot below; "
                + "the original source avatar is never modified.",
                MessageType.Info);

            if (validationReport != null)
            {
                foreach (var issue in validationReport.GlobalIssues)
                {
                    DrawValidationIssue(issue, null);
                }
            }
        }

        private void ScheduleRefresh()
        {
            if (refreshPending)
            {
                return;
            }

            refreshPending = true;
            EditorApplication.delayCall += RefreshAnalysis;
        }

        private void RefreshAnalysis()
        {
            EditorApplication.delayCall -= RefreshAnalysis;
            refreshPending = false;
            if (target is PhantomAuthoring authoring && authoring != null)
            {
                parameterAnalysis = PhantomParameterAnalysis.Analyze(authoring);
                validationReport = PhantomSourceValidator.Validate(authoring);
                Repaint();
            }
        }
    }
}
