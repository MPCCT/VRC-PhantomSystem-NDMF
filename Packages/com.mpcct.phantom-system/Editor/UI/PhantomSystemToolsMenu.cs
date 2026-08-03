using UnityEditor;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    internal static class PhantomSystemToolsMenu
    {
        private const string DeletePrebakeAssetsMenuPath =
            "Tools/PhantomSystem/Delete Prebake Assets";

        [MenuItem(DeletePrebakeAssetsMenuPath, false, 2100)]
        private static void DeletePrebakeAssets()
        {
            if (!EditorUtility.DisplayDialog(
                    "Delete all generated Phantom prebake assets?",
                    "Every PhantomSystem-managed PhantomPrebake_<Hash> directory will be deleted without checking references. Existing manual-bake results may lose generated asset references and must be baked again. This cannot be undone.",
                    "Delete All",
                    "Cancel"))
            {
                return;
            }

            var result = PhantomPrebakeAssetCleanup.DeleteGeneratedAssets();
            var message = result.ToDisplayMessage();
            Debug.Log("[PhantomSystem] " + message);
            EditorUtility.DisplayDialog("Phantom prebake asset deletion", message, "OK");
        }

        [MenuItem(DeletePrebakeAssetsMenuPath, true)]
        private static bool CanDeletePrebakeAssets()
        {
            return !EditorApplication.isPlaying
                   && !EditorApplication.isCompiling
                   && !EditorApplication.isUpdating
                   && !PhantomPrebakeSession.IsPrebaking;
        }
    }
}
