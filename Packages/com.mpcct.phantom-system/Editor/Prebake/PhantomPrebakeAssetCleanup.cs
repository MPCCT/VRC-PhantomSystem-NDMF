using System;
using System.Linq;
using UnityEditor;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Deletes package-owned prebake asset directories after explicit user confirmation.</summary>
    internal static class PhantomPrebakeAssetCleanup
    {
        private const string PrebakeDirectoryPrefix = "PhantomPrebake_";

        public static PhantomPrebakeAssetCleanupResult DeleteGeneratedAssets()
        {
            if (!AssetDatabase.IsValidFolder(PhantomPrebakeService.GeneratedAssetRoot))
            {
                return new PhantomPrebakeAssetCleanupResult(0, 0, 0);
            }

            var candidates = AssetDatabase.GetSubFolders(PhantomPrebakeService.GeneratedAssetRoot)
                .Where(IsOwnedPrebakeDirectory)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
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

            AssetDatabase.Refresh();
            return new PhantomPrebakeAssetCleanupResult(candidates.Length, removed, failed);
        }

        private static bool IsOwnedPrebakeDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)
                || !path.StartsWith(
                    PhantomPrebakeService.GeneratedAssetRoot + "/",
                    StringComparison.Ordinal))
            {
                return false;
            }

            var relativePath = path.Substring(PhantomPrebakeService.GeneratedAssetRoot.Length + 1);
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
