using L = MPCCT.PhantomSystem.Editor.PhantomLocalization;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    public sealed partial class PhantomSystemEditor
    {
        private void DrawSystemOptions()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(L.S("system.title"), EditorStyles.boldLabel);

            var installMenu = options.FindPropertyRelative("installPhantomMenu");
            EditorGUILayout.PropertyField(
                installMenu,
                L.G("system.installMenu"));

            var authoring = target as PhantomAuthoring;
            var installer = authoring != null ? authoring.coreMenuInstaller : null;
            using (new EditorGUI.DisabledScope(!installMenu.boolValue || installer == null))
            {
                if (GUILayout.Button(L.S("system.menuLocation")))
                {
                    installer.OpenSelectMenu();
                }
            }

            EditorGUILayout.LabelField(
                installer != null
                    ? L.S("system.installerHelp")
                    : L.S("diagnostic.ui.missingInstaller"),
                EditorStyles.miniLabel);

            EditorGUILayout.Space();

            if (GUILayout.Button(L.S("system.settings")))
            {
                PhantomSystemGlobalSettingsWindow.Open();
            }

            DrawManualBake();
        }

        private void DrawManualBake()
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                L.S("system.bakeHelp"),
                MessageType.Info);

            var authoring = target as PhantomAuthoring;
            var avatar = PhantomManualBakeService.FindAvatar(authoring);
            using (new EditorGUI.DisabledScope(
                       avatar == null
                       || EditorApplication.isPlaying
                       || EditorApplication.isCompiling
                       || EditorApplication.isUpdating
                       || PhantomPrebakeSession.IsPrebaking))
            {
                if (GUILayout.Button(
                        L.G("system.bake")))
                {
                    serializedObject.ApplyModifiedProperties();
                    EditorApplication.delayCall += () =>
                    {
                        if (authoring != null)
                        {
                            PhantomManualBakeService.Bake(authoring);
                        }
                    };
                    GUIUtility.ExitGUI();
                }
            }

            if (avatar == null)
            {
                EditorGUILayout.HelpBox(
                    L.S("diagnostic.ui.bakeMissingDescriptor"),
                    MessageType.Error);
            }
        }

        private void EnsureCoreMenuInstaller()
        {
            var authoring = target as PhantomAuthoring;
            if (authoring == null
                || (authoring.coreMenuInstaller != null
                    && authoring.coreMenuInstaller.gameObject == authoring.gameObject))
            {
                return;
            }

            Undo.RecordObject(authoring, "Attach Phantom Core Menu Installer");
            authoring.coreMenuInstaller =
                Undo.AddComponent<ModularAvatarMenuInstaller>(authoring.gameObject);
            EditorUtility.SetDirty(authoring);
        }
    }
}
