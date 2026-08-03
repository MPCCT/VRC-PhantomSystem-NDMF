using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    internal static class PhantomSystemSetupMenu
    {
        private const string SetupMenuPath =
            "GameObject/PhantomSystem/Setup PhantomSystem";

        [MenuItem(SetupMenuPath, false, 20)]
        private static void SetupPhantomSystem(MenuCommand menuCommand)
        {
            var avatarRoot = menuCommand.context as GameObject;
            if (!CanSetup(avatarRoot))
            {
                return;
            }

            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Setup PhantomSystem");

            var systemObject = new GameObject("PhantomSystem");
            Undo.RegisterCreatedObjectUndo(systemObject, "Setup PhantomSystem");
            Undo.SetTransformParent(systemObject.transform, avatarRoot.transform, "Setup PhantomSystem");

            systemObject.transform.localPosition = Vector3.zero;
            systemObject.transform.localRotation = Quaternion.identity;
            systemObject.transform.localScale = Vector3.one;
            systemObject.layer = avatarRoot.layer;

            Undo.AddComponent<PhantomAuthoring>(systemObject);
            Undo.CollapseUndoOperations(undoGroup);

            Selection.activeGameObject = systemObject;
            EditorGUIUtility.PingObject(systemObject);
        }

        [MenuItem(SetupMenuPath, true)]
        private static bool CanSetupPhantomSystem()
        {
            return CanSetup(Selection.activeGameObject);
        }

        private static bool CanSetup(GameObject avatarRoot)
        {
            return avatarRoot != null
                   && !EditorApplication.isPlayingOrWillChangePlaymode
                   && !EditorApplication.isCompiling
                   && !EditorApplication.isUpdating
                   && !EditorUtility.IsPersistent(avatarRoot)
                   && avatarRoot.scene.IsValid()
                   && avatarRoot.GetComponent<VRCAvatarDescriptor>() != null
                   && avatarRoot.GetComponentInChildren<PhantomAuthoring>(true) == null;
        }
    }
}
