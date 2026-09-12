using L = MPCCT.PhantomSystem.Editor.PhantomLocalization;
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
                    L.S("tools.deleteTitle"),
                    L.S("tools.deleteDescription"),
                    L.S("tools.deleteAll"),
                    L.S("common.cancel")))
            {
                return;
            }

            var result = PhantomPrebakeAssetCleanup.DeleteGeneratedAssets();
            var message = result.Candidates == 0
                ? L.S("tools.noAssets")
                : L.F("tools.deleted", result.Candidates, result.Removed, result.Failed);
            Debug.Log("[PhantomSystem] " + message);
            EditorUtility.DisplayDialog(L.S("tools.deletedTitle"), message, L.S("common.ok"));
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
                    L.S("tools.clearTitle"),
                    L.F("tools.clearDescription", statistics.EntryCount, FormatBytes(statistics.Bytes)),
                    L.S("tools.clear"),
                    L.S("common.cancel")))
            {
                return false;
            }

            if (!PhantomHumanoidBakeCacheSession.ClearAll(out var error))
            {
                Debug.LogWarning(L.D("diagnostic.cache.clearFailed", error));
                EditorUtility.DisplayDialog(
                    L.S("diagnostic.ui.cacheTitle"),
                    L.S("diagnostic.ui.cacheClearFailed"),
                    L.S("common.ok"));
                return false;
            }

            Debug.Log(L.D("diagnostic.cache.cleared"));
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
