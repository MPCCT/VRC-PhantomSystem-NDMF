using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;
using Object = UnityEngine.Object;

namespace MPCCT.PhantomSystem.Editor
{
    internal sealed class PhantomPrebakeHook : IVRCSDKPreprocessAvatarCallback
    {
        // NDMF starts the main avatar build at -11000. Phantom sources must be fully processed before that context exists.
        public int callbackOrder => -12000;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (avatarGameObject == null
                || PhantomPrebakeSession.IsPrebaking
                || avatarGameObject.GetComponentsInChildren<PhantomAuthoring>(true).Length == 0)
            {
                return true;
            }

            return PhantomPrebakeService.Prepare(avatarGameObject);
        }
    }

    internal sealed class PhantomPrebakeConsumptionGuard : IVRCSDKPreprocessAvatarCallback
    {
        // Runs immediately after NDMF's early hook. A remaining binding means NDMF was disabled or did not consume it.
        public int callbackOrder => -10999;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (avatarGameObject == null
                || !PhantomPrebakeSession.HasBindings(avatarGameObject))
            {
                return true;
            }

            Debug.LogError(
                "[PhantomSystem] The phantom sources were prebaked, but the main NDMF build did not consume them. "
                + "Enable NDMF Apply on Build/Play and retry.");
            PhantomPrebakeSession.CleanupBindings(avatarGameObject);
            return false;
        }
    }

    internal static class PhantomPrebakeService
    {
        internal const string GeneratedAssetRoot = "Assets/PhantomSystemGenerated/Prebake";

        public static bool Prepare(GameObject avatarRoot)
        {
            PhantomPrebakeSession.Begin(avatarRoot);
            PhantomPrebakeSession.IsPrebaking = true;

            try
            {
                var authoringComponents = avatarRoot.GetComponentsInChildren<PhantomAuthoring>(true);
                if (authoringComponents.Length > 1)
                {
                    // The main NDMF collection pass owns the user-facing error so it can
                    // report through the NDMF Console. Do not prebake an invalid layout.
                    return true;
                }

                if (authoringComponents.Length == 0)
                {
                    return true;
                }

                var authoring = authoringComponents[0];
                ValidateSources(authoring);

                var prebakedBySource = new Dictionary<VRCAvatarDescriptor, GameObject>();
                var slots = authoring.slots ?? new List<PhantomSlot>();
                for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                {
                    var slot = slots[slotIndex];
                    if (slot?.phantomAvatar == null)
                    {
                        continue;
                    }

                    if (!prebakedBySource.TryGetValue(slot.phantomAvatar, out var prebakedRoot))
                    {
                        prebakedRoot = Prebake(slot.phantomAvatar);
                        prebakedBySource.Add(slot.phantomAvatar, prebakedRoot);
                        PhantomPrebakeSession.BindSource(slot.phantomAvatar, prebakedRoot);
                    }

                    PhantomPrebakeSession.Bind(authoring, slotIndex, prebakedRoot);
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[PhantomSystem] Automatic phantom prebake failed. The avatar build has been stopped.");
                Debug.LogException(exception);
                PhantomPrebakeSession.CleanupBindings(avatarRoot);
                PhantomPrebakeSession.CleanupAll();
                return false;
            }
            finally
            {
                PhantomPrebakeSession.IsPrebaking = false;
            }
        }

        private static GameObject Prebake(VRCAvatarDescriptor sourceAvatar)
        {
            var stagingRoot = Object.Instantiate(sourceAvatar.gameObject);
            stagingRoot.name = BuildStagingName(sourceAvatar);
            stagingRoot.transform.SetParent(null, true);
            stagingRoot.SetActive(true);
            PhantomPrebakeSession.Register(stagingRoot);
            DisableMmdWorldSupportForPrebake(stagingRoot);

            BuildContext context;
            using (new OverrideTemporaryDirectoryScope(GeneratedAssetRoot))
            {
                context = AvatarProcessor.ProcessAvatar(stagingRoot, AmbientPlatform.CurrentPlatform);
            }

            if (context == null || !context.Successful)
            {
                throw new InvalidOperationException(
                    $"NDMF reported errors while prebaking phantom '{sourceAvatar.name}'.");
            }

            var descriptor = stagingRoot.GetComponent<VRCAvatarDescriptor>();
            var animator = stagingRoot.GetComponent<Animator>();
            if (descriptor == null)
            {
                throw new InvalidOperationException(
                    $"Prebaked phantom '{sourceAvatar.name}' no longer has a VRCAvatarDescriptor.");
            }

            if (animator == null || !animator.isHuman)
            {
                throw new InvalidOperationException(
                    $"Prebaked phantom '{sourceAvatar.name}' no longer has a humanoid Animator.");
            }

            return stagingRoot;
        }

        internal static void DisableMmdWorldSupportForPrebake(GameObject stagingRoot)
        {
            if (stagingRoot == null)
            {
                return;
            }

            var settings = stagingRoot.GetComponentsInChildren<ModularAvatarVRChatSettings>(true);
            if (settings.Length == 0)
            {
                var temporarySettings = stagingRoot.AddComponent<ModularAvatarVRChatSettings>();
                temporarySettings.MMDWorldSupport = false;
                return;
            }

            if (settings.Length == 1)
            {
                settings[0].MMDWorldSupport = false;
            }

            // Multiple settings components are already an invalid MA configuration.
            // Leave them untouched so MA can report the original source error.
        }

        private static void ValidateSources(PhantomAuthoring authoring)
        {
            var validation = PhantomSourceValidator.ValidateAuthoring(authoring);
            var errors = validation.GlobalIssues
                .Concat(validation.Slots.SelectMany(slot => slot.Issues))
                .Where(issue => issue.Severity == PhantomValidationSeverity.ConfigurationError
                                || issue.Severity == PhantomValidationSeverity.InternalError)
                .Select(issue => string.IsNullOrEmpty(issue.Code)
                    ? issue.Message
                    : $"[{issue.Code}] {issue.Message}")
                .ToArray();
            if (errors.Length > 0)
            {
                throw new InvalidOperationException(
                    "PhantomSystem configuration validation failed:\n" + string.Join("\n", errors));
            }
        }

        private static string BuildStagingName(VRCAvatarDescriptor sourceAvatar)
        {
            string identity;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sourceAvatar, out var guid, out long localId))
            {
                identity = $"{guid}:{localId}";
            }
            else
            {
                identity = $"{sourceAvatar.gameObject.scene.path}:{sourceAvatar.name}";
            }

            return $"PhantomPrebake_{Hash128.Compute(identity)}";
        }
    }
}
