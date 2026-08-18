using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    [InitializeOnLoad]
    internal static class PhantomPrebakeSession
    {
        private static readonly HashSet<GameObject> StagingRoots = new HashSet<GameObject>();
        private static readonly Dictionary<int, List<GameObject>> Bindings =
            new Dictionary<int, List<GameObject>>();
        private static readonly Dictionary<VRCAvatarDescriptor, GameObject> SourceBindings =
            new Dictionary<VRCAvatarDescriptor, GameObject>();

        public static bool IsPrebaking { get; set; }
        private static bool AutomaticCleanupPending { get; set; }

        static PhantomPrebakeSession()
        {
            AssemblyReloadEvents.beforeAssemblyReload += CleanupAll;
            EditorApplication.quitting += CleanupAll;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    CleanupAll();
                    if (ConsumeAutomaticCleanupPending())
                    {
                        PhantomPrebakeService.CleanupGeneratedAssets("leaving Play Mode");
                    }
                }
            };
        }

        public static void Begin(GameObject avatarRoot)
        {
            CleanupBindings(avatarRoot);
            CleanupAll();
            AutomaticCleanupPending = false;
        }

        internal static void MarkAutomaticCleanupPending()
        {
            AutomaticCleanupPending = true;
        }

        internal static bool ConsumeAutomaticCleanupPending()
        {
            var pending = AutomaticCleanupPending;
            AutomaticCleanupPending = false;
            return pending;
        }

        internal static void ClearAutomaticCleanupPending()
        {
            AutomaticCleanupPending = false;
        }

        public static void Register(GameObject stagingRoot)
        {
            if (stagingRoot != null)
            {
                StagingRoots.Add(stagingRoot);
            }
        }

        public static void Bind(PhantomAuthoring authoring, int slotIndex, GameObject stagingRoot)
        {
            if (authoring == null || slotIndex < 0)
            {
                return;
            }

            var key = authoring.GetInstanceID();
            if (!Bindings.TryGetValue(key, out var roots))
            {
                roots = new List<GameObject>();
                Bindings.Add(key, roots);
            }

            while (roots.Count <= slotIndex)
            {
                roots.Add(null);
            }

            roots[slotIndex] = stagingRoot;
        }

        public static void BindSource(VRCAvatarDescriptor sourceAvatar, GameObject stagingRoot)
        {
            if (sourceAvatar != null && stagingRoot != null)
            {
                SourceBindings[sourceAvatar] = stagingRoot;
            }
        }

        public static GameObject GetRoot(PhantomAuthoring authoring, int slotIndex)
        {
            if (authoring == null || slotIndex < 0)
            {
                return null;
            }

            if (Bindings.TryGetValue(authoring.GetInstanceID(), out var roots)
                && slotIndex < roots.Count
                && roots[slotIndex] != null)
            {
                return roots[slotIndex];
            }

            var slots = authoring.slots;
            if (slots == null || slotIndex >= slots.Count)
            {
                return null;
            }

            var sourceAvatar = slots[slotIndex]?.phantomAvatar;
            return sourceAvatar != null
                   && SourceBindings.TryGetValue(sourceAvatar, out var sourceRoot)
                ? sourceRoot
                : null;
        }

        public static bool HasBindings(GameObject avatarRoot)
        {
            return avatarRoot != null
                   && avatarRoot.GetComponentsInChildren<PhantomAuthoring>(true)
                       .Any(authoring => authoring != null && Bindings.ContainsKey(authoring.GetInstanceID()));
        }

        public static void Release(GameObject avatarRoot)
        {
            CleanupBindings(avatarRoot);
        }

        public static void CleanupBindings(GameObject avatarRoot)
        {
            if (avatarRoot == null)
            {
                return;
            }

            var authoringKeys = avatarRoot.GetComponentsInChildren<PhantomAuthoring>(true)
                .Where(authoring => authoring != null)
                .Select(authoring => authoring.GetInstanceID())
                .ToArray();
            var rootsToDestroy = authoringKeys
                .Where(Bindings.ContainsKey)
                .SelectMany(key => Bindings[key])
                .Where(root => root != null)
                .Distinct()
                .ToArray();
            foreach (var root in rootsToDestroy)
            {
                StagingRoots.Remove(root);
                Object.DestroyImmediate(root);
            }

            foreach (var source in SourceBindings
                         .Where(pair => pair.Value == null || rootsToDestroy.Contains(pair.Value))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                SourceBindings.Remove(source);
            }

            foreach (var key in authoringKeys)
            {
                Bindings.Remove(key);
            }
        }

        public static void CleanupAll()
        {
            foreach (var root in StagingRoots.Where(root => root != null).ToArray())
            {
                Object.DestroyImmediate(root);
            }

            StagingRoots.Clear();
            Bindings.Clear();
            SourceBindings.Clear();
            IsPrebaking = false;
        }
    }
}
