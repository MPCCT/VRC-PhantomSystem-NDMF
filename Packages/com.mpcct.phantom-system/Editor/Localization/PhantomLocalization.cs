using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using nadena.dev.ndmf.localization;
using UnityEditor;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Editor UI and generated menus share the NDMF language preference.</summary>
    [InitializeOnLoad]
    internal static class PhantomLocalization
    {
        internal const string AssetDirectory = "Packages/com.mpcct.phantom-system/Editor/Localization/";
        internal static readonly string[] Locales = { "en-US", "zh-Hans", "ja-JP" };
        internal static readonly string[] LanguageNames = { "English", "简体中文", "日本語" };
        private static readonly Dictionary<string, Dictionary<string, string>> Tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        internal static readonly Localizer Localizer = new Localizer("en-US", LoadLanguages);

        [Serializable]
        private sealed class Table
        {
            public Entry[] entries;
        }

        [Serializable]
        private sealed class Entry
        {
            public string key;
            public string value;
        }

        private static List<(string, Func<string, string>)> LoadLanguages()
        {
            Tables.Clear();
            var languages = new List<(string, Func<string, string>)>();
            foreach (var locale in Locales)
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                // InitializeOnLoad can run before Unity imports new TextAssets.
                // Read the package files directly so the first import has translations too.
                var package = PackageInfo.FindForAssembly(typeof(PhantomLocalization).Assembly);
                var directory = package != null
                    ? Path.Combine(package.resolvedPath, "Editor", "Localization")
                    : AssetDirectory;
                var json = File.ReadAllText(Path.Combine(directory, locale + ".json"));
                foreach (var entry in JsonUtility.FromJson<Table>(json).entries)
                {
                    values.Add(entry.key, entry.value);
                }
                Tables.Add(locale, values);
                languages.Add((locale, key => values.TryGetValue(key, out var value) ? value : null));
            }
            return languages;
        }

        internal static PhantomDiagnostic D(string key, params object[] arguments) =>
            new PhantomDiagnostic(key, arguments);

        internal static string S(string key)
        {
            return Localizer.GetLocalizedString(key);
        }

        internal static string F(string key, params object[] arguments)
        {
            return string.Format(CultureInfo.CurrentCulture, S(key), arguments);
        }

        internal static GUIContent G(string key)
        {
            return new GUIContent(S(key),
                Localizer.TryGetLocalizedString(key + ".tooltip", out var tooltip) ? tooltip : null);
        }

        internal static IReadOnlyDictionary<string, string> GetTable(string locale) => Tables[locale];

        internal static void DrawLanguageSelector()
        {
            var current = LanguagePrefs.Language;
            var index = current.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? 1
                : current.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
            EditorGUI.BeginChangeCheck();
            var selected = EditorGUILayout.Popup(S("language.editor"), index, LanguageNames);
            if (EditorGUI.EndChangeCheck())
            {
                LanguagePrefs.Language = Locales[selected];
            }
        }
    }
}
