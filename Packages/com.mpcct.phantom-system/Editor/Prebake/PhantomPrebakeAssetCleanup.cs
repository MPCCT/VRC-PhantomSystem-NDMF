using System;
using System.IO;
using System.Linq;
using UnityEditor;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Deletes only package-owned prebake asset directories.</summary>
    internal static class PhantomPrebakeAssetCleanup
    {
        private const string PrebakeDirectoryPrefix = "PhantomPrebake_";

        public static PhantomPrebakeAssetCleanupResult DeleteGeneratedAssets()
        {
            return DeleteGeneratedAssets(
                PhantomPrebakeService.GeneratedAssetRoot,
                PhantomPrebakeService.GeneratedAssetContainer);
        }

        internal static PhantomPrebakeAssetCleanupResult DeleteGeneratedAssets(string generatedAssetRoot)
        {
            return DeleteGeneratedAssets(generatedAssetRoot, null);
        }

        internal static PhantomPrebakeAssetCleanupResult DeleteGeneratedAssets(
            string generatedAssetRoot,
            string generatedAssetContainer)
        {
            if (!AssetDatabase.IsValidFolder(generatedAssetRoot))
            {
                DeleteFolderIfEmpty(generatedAssetContainer);
                return new PhantomPrebakeAssetCleanupResult(0, 0, 0);
            }

            var candidates = AssetDatabase.GetSubFolders(generatedAssetRoot)
                .Where(path => IsOwnedPrebakeDirectory(path, generatedAssetRoot))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
                DeleteFolderIfEmpty(generatedAssetRoot);
                DeleteFolderIfEmpty(generatedAssetContainer);
                return new PhantomPrebakeAssetCleanupResult(0, 0, 0);
            }

            var removed = 0;
            var failed = 0;
            foreach (var candidate in candidates)
            {
                if (AssetDatabase.DeleteAsset(candidate))
                {
                    removed++;
                }
                else
                {
                    failed++;
                }
            }

            DeleteFolderIfEmpty(generatedAssetRoot);
            DeleteFolderIfEmpty(generatedAssetContainer);
            AssetDatabase.Refresh();
            return new PhantomPrebakeAssetCleanupResult(candidates.Length, removed, failed);
        }

        private static void DeleteFolderIfEmpty(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)
                || !AssetDatabase.IsValidFolder(assetPath)
                || Directory.EnumerateFileSystemEntries(assetPath).Any())
            {
                return;
            }

            AssetDatabase.DeleteAsset(assetPath);
        }

        private static bool IsOwnedPrebakeDirectory(string path, string generatedAssetRoot)
        {
            if (string.IsNullOrEmpty(path)
                || !path.StartsWith(
                    generatedAssetRoot + "/",
                    StringComparison.Ordinal))
            {
                return false;
            }

            var relativePath = path.Substring(generatedAssetRoot.Length + 1);
            return relativePath.IndexOf('/') < 0
                   && relativePath.StartsWith(PrebakeDirectoryPrefix, StringComparison.Ordinal);
        }
    }

    internal readonly struct PhantomPrebakeAssetCleanupResult
    {
        public readonly int Candidates;
        public readonly int Removed;
        public readonly int Failed;

        public PhantomPrebakeAssetCleanupResult(int candidates, int removed, int failed)
        {
            Candidates = candidates;
            Removed = removed;
            Failed = failed;
        }

        public string ToDisplayMessage()
        {
            if (Candidates == 0)
            {
                return "No generated Phantom prebake asset directories were found.";
            }

            return $"Found {Candidates} generated prebake directories.\n\n"
                   + $"Removed: {Removed}\n"
                   + $"Failed to remove: {Failed}";
        }
    }
}
