using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Owns one build's memory cache, persistent cache I/O, and aggregate diagnostics.</summary>
    internal sealed class PhantomHumanoidBakeCacheSession
    {
        private static readonly string DefaultCacheBasePath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "Library",
            "PhantomSystem",
            "HumanoidBakeCache");

        private readonly Dictionary<string, PhantomHumanoidPoseBakeData> memoryCache =
            new Dictionary<string, PhantomHumanoidPoseBakeData>(StringComparer.Ordinal);
        private readonly string cacheBasePath;

        internal int HitCount { get; private set; }
        internal int MissCount { get; private set; }
        internal int WriteFailureCount { get; private set; }
        internal int BypassCount { get; private set; }
        internal int VirtualClipFastPathHitCount { get; private set; }
        internal int RequestCount => HitCount + MissCount + BypassCount;

        private static string SchemaDirectoryName =>
            $"v{PhantomHumanoidBakeCacheSerializer.SchemaVersion}";
        private string CurrentCachePath => Path.Combine(cacheBasePath, SchemaDirectoryName);

        internal PhantomHumanoidBakeCacheSession()
            : this(DefaultCacheBasePath)
        {
        }

        internal PhantomHumanoidBakeCacheSession(string cacheBasePath)
        {
            this.cacheBasePath = cacheBasePath ?? throw new ArgumentNullException(nameof(cacheBasePath));
            CleanupIncompatibleCaches();
        }

        internal string TryCreateKey(
            AnimationClip source,
            Avatar avatar,
            Transform sourceRoot,
            float sampleRate,
            float positionTolerance,
            float rotationToleranceDegrees,
            PhantomHumanoidClipAnalysis analysis,
            PhantomHumanoidClipBakeOptions options,
            bool effectiveMirror)
        {
            try
            {
                return PhantomHumanoidBakeCacheKeyBuilder.Create(
                    source,
                    avatar,
                    sourceRoot,
                    sampleRate,
                    positionTolerance,
                    rotationToleranceDegrees,
                    analysis,
                    options,
                    effectiveMirror);
            }
            catch (Exception exception)
            {
                BypassCount++;
                Debug.LogWarning(
                    $"[PhantomSystem] Could not create a Humanoid bake cache key for '{source?.name}'. "
                    + $"The clip will be baked normally. {exception.Message}");
                return null;
            }
        }

        internal bool TryLoad(string key, out PhantomHumanoidPoseBakeData data)
        {
            data = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (memoryCache.TryGetValue(key, out data))
            {
                HitCount++;
                return true;
            }

            var path = GetEntryPath(key);
            if (!File.Exists(path))
            {
                MissCount++;
                return false;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, false))
                {
                    data = PhantomHumanoidBakeCacheSerializer.Read(reader, key);
                    if (stream.Position != stream.Length)
                    {
                        throw new InvalidDataException("The cache entry contains trailing data.");
                    }
                }

                memoryCache[key] = data;
                HitCount++;
                return true;
            }
            catch (Exception exception)
            {
                TryDeleteFile(path);
                MissCount++;
                Debug.LogWarning(
                    $"[PhantomSystem] Ignored a damaged Humanoid bake cache entry '{key}'. "
                    + $"The clip will be baked again. {exception.Message}");
                data = null;
                return false;
            }
        }

        internal void Store(string key, PhantomHumanoidPoseBakeData data)
        {
            if (string.IsNullOrEmpty(key) || data == null)
            {
                return;
            }

            memoryCache[key] = data;
            string temporaryPath = null;
            try
            {
                Directory.CreateDirectory(CurrentCachePath);
                var path = GetEntryPath(key);
                temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, false))
                {
                    PhantomHumanoidBakeCacheSerializer.Write(writer, key, data);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
                temporaryPath = null;
            }
            catch (Exception exception)
            {
                WriteFailureCount++;
                Debug.LogWarning(
                    $"[PhantomSystem] Could not save Humanoid bake cache entry '{key}'. "
                    + $"The current build can continue without it. {exception.Message}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporaryPath))
                {
                    TryDeleteFile(temporaryPath);
                }
            }
        }

        internal void RecordVirtualClipFastPathHit()
        {
            VirtualClipFastPathHitCount++;
        }

        internal void ReportSummary(PhantomBuildReport report, UnityEngine.Object context)
        {
            if (report == null || RequestCount == 0)
            {
                return;
            }

            var message = $"Humanoid bake cache: {HitCount} hit(s), {MissCount} miss(es)";
            if (BypassCount > 0)
            {
                message += $", {BypassCount} bypass(es)";
            }
            if (WriteFailureCount > 0)
            {
                message += $", {WriteFailureCount} write failure(s)";
            }
            if (VirtualClipFastPathHitCount > 0)
            {
                message += $", {VirtualClipFastPathHitCount} VirtualClip fast-path hit(s)";
            }
            report.Info(message + ".", context);
        }

        internal static PhantomHumanoidBakeCacheStatistics GetStatistics()
        {
            return GetStatistics(DefaultCacheBasePath);
        }

        internal static PhantomHumanoidBakeCacheStatistics GetStatistics(string cacheBasePath)
        {
            if (!Directory.Exists(cacheBasePath))
            {
                return new PhantomHumanoidBakeCacheStatistics(0, 0L);
            }

            try
            {
                var files = Directory.GetFiles(cacheBasePath, "*.bin", SearchOption.AllDirectories);
                long bytes = 0;
                foreach (var file in files)
                {
                    bytes += new FileInfo(file).Length;
                }
                return new PhantomHumanoidBakeCacheStatistics(files.Length, bytes);
            }
            catch
            {
                return new PhantomHumanoidBakeCacheStatistics(0, 0L);
            }
        }

        internal static bool ClearAll(out string error)
        {
            return ClearAll(DefaultCacheBasePath, out error);
        }

        internal static bool ClearAll(string cacheBasePath, out string error)
        {
            error = null;
            try
            {
                if (Directory.Exists(cacheBasePath))
                {
                    Directory.Delete(cacheBasePath, true);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private string GetEntryPath(string key)
        {
            return Path.Combine(CurrentCachePath, key + ".bin");
        }

        private void CleanupIncompatibleCaches()
        {
            try
            {
                if (!Directory.Exists(cacheBasePath))
                {
                    return;
                }

                foreach (var schemaPath in Directory.GetDirectories(cacheBasePath))
                {
                    if (!string.Equals(
                            Path.GetFileName(schemaPath),
                            SchemaDirectoryName,
                            StringComparison.Ordinal))
                    {
                        Directory.Delete(schemaPath, true);
                    }
                }

                var currentSchemaPath = Path.Combine(cacheBasePath, SchemaDirectoryName);
                if (!Directory.Exists(currentSchemaPath))
                {
                    return;
                }
                // Older development builds placed entries below a Unity-version directory.
                // The supported editor version is fixed, so those directories are obsolete.
                foreach (var legacyVersionPath in Directory.GetDirectories(currentSchemaPath))
                {
                    Directory.Delete(legacyVersionPath, true);
                }

                foreach (var temporaryFile in Directory.GetFiles(
                             currentSchemaPath,
                             "*.tmp",
                             SearchOption.AllDirectories))
                {
                    TryDeleteFile(temporaryFile);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[PhantomSystem] Could not remove incompatible Humanoid bake cache data. "
                    + exception.Message);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Cache cleanup must never fail an avatar build.
            }
        }
    }

    internal readonly struct PhantomHumanoidBakeCacheStatistics
    {
        internal int EntryCount { get; }
        internal long Bytes { get; }

        internal PhantomHumanoidBakeCacheStatistics(int entryCount, long bytes)
        {
            EntryCount = entryCount;
            Bytes = bytes;
        }
    }
}
