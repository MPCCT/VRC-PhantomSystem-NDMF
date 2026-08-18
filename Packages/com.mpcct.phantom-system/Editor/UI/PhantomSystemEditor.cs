using System;
using System.Linq;
using nadena.dev.ndmf.preview;
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
        private PhantomParameterPlan parameterPlan;
        private PhantomSourceValidationReport validationReport;
        private string slotFoldoutStateKey;
        private bool refreshPending;
        private ComputeContext analysisContext;
        private IDisposable analysisInvalidationSubscription;

        private void OnEnable()
        {
            EnsureCoreMenuInstaller();
            slots = serializedObject.FindProperty("slots");
            options = serializedObject.FindProperty("options");
            slotFoldoutStateKey = SlotFoldoutStatePrefix
                + GlobalObjectId.GetGlobalObjectIdSlow(target);
            ClearFoldoutCaches();
            Undo.undoRedoPerformed += ScheduleRefresh;
            ScheduleRefresh();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= ScheduleRefresh;
            EditorApplication.delayCall -= RefreshAnalysis;
            refreshPending = false;
            DisposeAnalysisContext();
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

            var totalCost = parameterPlan?.Slots.Sum(slot => slot.FinalContributionCost);
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
            DisposeAnalysisContext();
            if (target is PhantomAuthoring authoring && authoring != null)
            {
                var context = new ComputeContext($"PhantomSystem Inspector: {authoring.name}");
                analysisContext = context;
                try
                {
                    parameterPlan = PhantomParameterPlanner.Analyze(authoring, context);
                    validationReport = PhantomSourceValidator.Validate(authoring, parameterPlan, context);
                    analysisInvalidationSubscription = context.InvokeOnInvalidate(
                        this,
                        editor => editor.OnAnalysisDependenciesInvalidated());
                }
                catch
                {
                    DisposeAnalysisContext();
                    throw;
                }

                Repaint();
            }
        }

        private void OnAnalysisDependenciesInvalidated()
        {
            if (analysisContext == null || !analysisContext.IsInvalidated)
            {
                return;
            }

            // ComputeContext invalidation listeners are one-shot. NDMF is currently
            // iterating and removing this listener while invoking us, so disposing
            // the token here would re-enter ListenerSet.Deregister during FireAll.
            analysisInvalidationSubscription = null;
            ScheduleRefresh();
        }

        private void DisposeAnalysisContext()
        {
            analysisInvalidationSubscription?.Dispose();
            analysisInvalidationSubscription = null;

            var context = analysisContext;
            analysisContext = null;
            context?.Invalidate();
        }
    }
}
