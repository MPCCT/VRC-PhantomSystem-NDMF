using UnityEditor;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    internal static class PhantomSystemToolsMenu
    {
        private const string DeletePrebakeAssetsMenuPath =
            "Tools/PhantomSystem/Delete Prebake Assets";
        private const string ClearHumanoidBakeCacheMenuPath =
            "Tools/PhantomSystem/Clear Humanoid Bake Cache";

        [MenuItem(DeletePrebakeAssetsMenuPath, false, 2100)]
        private static void DeletePrebakeAssets()
        {
            if (!EditorUtility.DisplayDialog(
                    "Delete all generated Phantom prebake assets?",
                    "Successful VRC builds normally remove these assets automatically. Every remaining "
                    + "PhantomSystem-managed PhantomPrebake_<Hash> directory will be deleted without "
                    + "checking references. Completed VRC and manual-bake results do not depend on these "
                    + "intermediate assets. This cannot be undone.",
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

        [MenuItem(ClearHumanoidBakeCacheMenuPath, false, 2110)]
        private static void ClearHumanoidBakeCache()
        {
            ClearHumanoidBakeCacheWithConfirmation();
        }

        [MenuItem(ClearHumanoidBakeCacheMenuPath, true)]
        private static bool CanClearHumanoidBakeCache()
        {
            return !EditorApplication.isPlaying
                   && !EditorApplication.isCompiling
                   && !EditorApplication.isUpdating
                   && !PhantomPrebakeSession.IsPrebaking;
        }

        internal static bool ClearHumanoidBakeCacheWithConfirmation()
        {
            var statistics = PhantomHumanoidBakeCacheSession.GetStatistics();
            if (!EditorUtility.DisplayDialog(
                    "Clear Humanoid bake cache?",
                    $"Delete {statistics.EntryCount} cached Humanoid bake entr"
                    + $"{(statistics.EntryCount == 1 ? "y" : "ies")} "
                    + $"({FormatBytes(statistics.Bytes)}) from this project's Library folder?\n\n"
                    + "The cache contains only derived pose data and will be rebuilt when needed.",
                    "Clear Cache",
                    "Cancel"))
            {
                return false;
            }

            if (!PhantomHumanoidBakeCacheSession.ClearAll(out var error))
            {
                Debug.LogWarning("[PhantomSystem] Could not clear the Humanoid bake cache. " + error);
                EditorUtility.DisplayDialog(
                    "Humanoid bake cache",
                    "The cache could not be cleared. See the Console for details.",
                    "OK");
                return false;
            }

            Debug.Log("[PhantomSystem] Cleared the Humanoid bake cache.");
            return true;
        }

        internal static string FormatBytes(long bytes)
        {
            if (bytes < 1024L)
            {
                return bytes + " B";
            }
            if (bytes < 1024L * 1024L)
            {
                return (bytes / 1024d).ToString("0.0") + " KiB";
            }
            if (bytes < 1024L * 1024L * 1024L)
            {
                return (bytes / (1024d * 1024d)).ToString("0.0") + " MiB";
            }
            return (bytes / (1024d * 1024d * 1024d)).ToString("0.0") + " GiB";
        }
    }
}
