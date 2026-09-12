using L = MPCCT.PhantomSystem.Editor.PhantomLocalization;
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
            window.titleContent = new GUIContent(L.S("settings.window"));
            window.minSize = new Vector2(470f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            nadena.dev.ndmf.localization.LanguagePrefs.RegisterLanguageChangeCallback(
                this, window =>
                {
                    if (window == null) return;
                    window.titleContent = new GUIContent(L.S("settings.window"));
                    window.Repaint();
                });
        }

        private void OnGUI()
        {
            titleContent = new GUIContent(L.S("settings.window"));
            L.DrawLanguageSelector();
            var settings = PhantomSystemProjectSettings.instance;
            EditorGUILayout.LabelField(L.S("settings.title"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                L.S("settings.description"),
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(L.S("menu.view"), EditorStyles.boldLabel);
            var textureIndex = System.Array.IndexOf(
                PhantomSystemProjectSettings.ViewTextureSizes,
                settings.PhantomViewTextureSize);
            textureIndex = Mathf.Max(0, textureIndex);

            EditorGUI.BeginChangeCheck();
            textureIndex = EditorGUILayout.Popup(
                L.G("settings.texture"),
                textureIndex,
                TextureSizeLabels);
            if (EditorGUI.EndChangeCheck())
            {
                settings.PhantomViewTextureSize =
                    PhantomSystemProjectSettings.ViewTextureSizes[textureIndex];
                settings.SaveImmediately();
            }

            EditorGUILayout.LabelField(
                L.S("settings.textureHelp"),
                EditorStyles.wordWrappedMiniLabel);
            if (settings.PhantomViewTextureSize >= 4096)
            {
                EditorGUILayout.HelpBox(
                    L.S("settings.largeTexture"),
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(L.S("settings.conversion"), EditorStyles.boldLabel);
            DrawFloatSetting(
                L.S("settings.sampleRate"),
                "FPS",
                L.S("settings.sampleRate.tooltip"),
                settings.MaximumAdaptiveSampleRate,
                1f,
                120f,
                value => settings.MaximumAdaptiveSampleRate = value,
                settings);
            DrawFloatSetting(
                L.S("settings.positionError"),
                "m",
                L.S("settings.positionError.tooltip"),
                settings.PositionErrorTolerance,
                0.000001f,
                0.1f,
                value => settings.PositionErrorTolerance = value,
                settings);
            DrawFloatSetting(
                L.S("settings.rotationError"),
                L.S("settings.degrees"),
                L.S("settings.rotationError.tooltip"),
                settings.RotationErrorToleranceDegrees,
                0.001f,
                10f,
                value => settings.RotationErrorToleranceDegrees = value,
                settings);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                L.S("settings.toleranceHelp"),
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(L.S("settings.cache"), EditorStyles.boldLabel);
            var cacheStatistics = PhantomHumanoidBakeCacheSession.GetStatistics();
            EditorGUILayout.LabelField(
                L.S("settings.cacheData"),
                L.F("settings.cacheStats", cacheStatistics.EntryCount,
                    PhantomSystemToolsMenu.FormatBytes(cacheStatistics.Bytes)));
            EditorGUILayout.HelpBox(
                L.S("settings.cacheHelp"),
                MessageType.None);
            if (GUILayout.Button(L.S("settings.clearCache")))
            {
                PhantomSystemToolsMenu.ClearHumanoidBakeCacheWithConfirmation();
                Repaint();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button(L.S("settings.reset")))
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
