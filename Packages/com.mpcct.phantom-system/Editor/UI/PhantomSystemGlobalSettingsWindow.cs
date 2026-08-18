using UnityEditor;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    internal sealed class PhantomSystemGlobalSettingsWindow : EditorWindow
    {
        private const string MenuPath = "Tools/PhantomSystem/Global Settings";
        private const float ConversionValueWidth = 120f;
        private static readonly string[] TextureSizeLabels =
            { "256", "512", "1024", "2048", "4096" };

        [MenuItem(MenuPath, false, 2000)]
        internal static void Open()
        {
            var window = GetWindow<PhantomSystemGlobalSettingsWindow>();
            window.titleContent = new GUIContent("PhantomSystem Settings");
            window.minSize = new Vector2(470f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            var settings = PhantomSystemProjectSettings.instance;
            EditorGUILayout.LabelField("PhantomSystem Global Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These settings are shared by every PhantomSystem avatar in this Unity project. "
                + "A stable snapshot is taken at the beginning of each build.",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Phantom View", EditorStyles.boldLabel);
            var textureIndex = System.Array.IndexOf(
                PhantomSystemProjectSettings.ViewTextureSizes,
                settings.PhantomViewTextureSize);
            textureIndex = Mathf.Max(0, textureIndex);

            EditorGUI.BeginChangeCheck();
            textureIndex = EditorGUILayout.Popup(
                new GUIContent(
                    "Texture Size",
                    "Resolution of each shared Phantom View eye RenderTexture."),
                textureIndex,
                TextureSizeLabels);
            if (EditorGUI.EndChangeCheck())
            {
                settings.PhantomViewTextureSize =
                    PhantomSystemProjectSettings.ViewTextureSizes[textureIndex];
                settings.SaveImmediately();
            }

            EditorGUILayout.LabelField(
                "Applied equally to the shared left-eye and right-eye RenderTextures.",
                EditorStyles.wordWrappedMiniLabel);
            if (settings.PhantomViewTextureSize >= 4096)
            {
                EditorGUILayout.HelpBox(
                    "4096 creates two very large render targets and can consume substantial GPU memory. "
                    + "Use it only after profiling the target PC setup.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Humanoid Animation Conversion", EditorStyles.boldLabel);
            DrawFloatSetting(
                "Maximum Adaptive Sample Rate",
                "FPS",
                "Limits automatically inserted samples. Original key times are preserved as candidates.",
                settings.MaximumAdaptiveSampleRate,
                1f,
                120f,
                value => settings.MaximumAdaptiveSampleRate = value,
                settings);
            DrawFloatSetting(
                "Position Error Tolerance",
                "m",
                "Maximum local-position interpolation error used for adaptive subdivision and reduction.",
                settings.PositionErrorTolerance,
                0.000001f,
                0.1f,
                value => settings.PositionErrorTolerance = value,
                settings);
            DrawFloatSetting(
                "Rotation Error Tolerance",
                "degrees",
                "Maximum local-rotation angular error used for adaptive subdivision and reduction.",
                settings.RotationErrorToleranceDegrees,
                0.001f,
                10f,
                value => settings.RotationErrorToleranceDegrees = value,
                settings);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Smaller tolerances preserve more motion detail but produce larger AnimationClips. "
                + "If the sample-rate limit is reached before tolerance is met, the build reports a warning.",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Humanoid Bake Cache", EditorStyles.boldLabel);
            var cacheStatistics = PhantomHumanoidBakeCacheSession.GetStatistics();
            EditorGUILayout.LabelField(
                "Cached Pose Data",
                $"{cacheStatistics.EntryCount} entr"
                + $"{(cacheStatistics.EntryCount == 1 ? "y" : "ies")}, "
                + PhantomSystemToolsMenu.FormatBytes(cacheStatistics.Bytes));
            EditorGUILayout.HelpBox(
                "The cache is project-local derived data stored under Library. It is not referenced by "
                + "the built avatar and can be cleared at any time.",
                MessageType.None);
            if (GUILayout.Button("Clear Humanoid Bake Cache"))
            {
                PhantomSystemToolsMenu.ClearHumanoidBakeCacheWithConfirmation();
                Repaint();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Reset to Defaults"))
            {
                settings.ResetToDefaults();
                GUI.FocusControl(null);
                Repaint();
            }
        }

        private static void DrawFloatSetting(
            string label,
            string unit,
            string tooltip,
            float current,
            float minimum,
            float maximum,
            System.Action<float> assign,
            PhantomSystemProjectSettings settings)
        {
            EditorGUI.BeginChangeCheck();
            float value;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent($"{label} ({unit})", tooltip));
                value = EditorGUILayout.FloatField(
                    current,
                    GUILayout.Width(ConversionValueWidth));
            }

            if (EditorGUI.EndChangeCheck())
            {
                assign(Mathf.Clamp(value, minimum, maximum));
                settings.SaveImmediately();
            }
        }
    }
}
