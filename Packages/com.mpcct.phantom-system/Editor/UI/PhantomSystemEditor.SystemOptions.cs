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
            EditorGUILayout.LabelField("System Options", EditorStyles.boldLabel);

            var installMenu = options.FindPropertyRelative("installPhantomMenu");
            EditorGUILayout.PropertyField(
                installMenu,
                new GUIContent(
                    "Install Phantom Menu",
                    "Generate and install the PhantomSystem Core menu."));

            var authoring = target as PhantomAuthoring;
            var installer = authoring != null ? authoring.coreMenuInstaller : null;
            using (new EditorGUI.DisabledScope(!installMenu.boolValue || installer == null))
            {
                if (GUILayout.Button("Select Core Menu Location"))
                {
                    installer.OpenSelectMenu();
                }
            }

            EditorGUILayout.LabelField(
                installer != null
                    ? "Configure only the attached MA Menu Installer's target; its source menu is supplied at build time."
                    : "The required MA Menu Installer is missing.",
                EditorStyles.miniLabel);

            DrawManualBake();
        }

        private void DrawManualBake()
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Modular Avatar's regular Manual Bake does not run the VRChat preprocess hook used by "
                + "PhantomSystem. Use this button to prebake all phantom sources before running NDMF's "
                + "normal manual avatar bake.",
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
                        new GUIContent(
                            "Bake Avatar with PhantomSystem",
                            "Prebake every configured phantom source, then run NDMF's normal Manual Bake Avatar workflow.")))
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
                    "PhantomSystem must be placed inside an avatar with a VRCAvatarDescriptor before it can be baked.",
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
