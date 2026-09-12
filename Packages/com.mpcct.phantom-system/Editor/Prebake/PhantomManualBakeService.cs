using L = MPCCT.PhantomSystem.Editor.PhantomLocalization;
using System;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    internal static class PhantomManualBakeService
    {
        public static VRCAvatarDescriptor FindAvatar(PhantomAuthoring authoring)
        {
            return authoring != null
                ? authoring.GetComponentInParent<VRCAvatarDescriptor>(true)
                : null;
        }

        public static void Bake(PhantomAuthoring authoring)
        {
            var avatar = FindAvatar(authoring);
            if (avatar == null)
            {
                Debug.LogError(
                    L.D("diagnostic.prebake.manualMissingDescriptor"),
                    authoring);
                return;
            }

            if (PhantomPrebakeSession.IsPrebaking)
            {
                Debug.LogWarning(
                    L.D("diagnostic.prebake.alreadyRunning"),
                    authoring);
                return;
            }

            if (!PhantomPrebakeService.Prepare(avatar.gameObject, false))
            {
                return;
            }

            try
            {
                var bakedAvatar = AvatarProcessor.ManualProcessAvatar(
                    avatar.gameObject,
                    AmbientPlatform.CurrentPlatform);
                if (bakedAvatar != null)
                {
                    Selection.activeGameObject = bakedAvatar;
                    EditorGUIUtility.PingObject(bakedAvatar);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    L.D("diagnostic.prebake.manualFailed"),
                    authoring);
                Debug.LogException(exception);
            }
            finally
            {
                PhantomPrebakeSession.CleanupAll();
                PhantomPrebakeService.CleanupGeneratedAssets(L.D("diagnostic.cleanup.manual"));
            }
        }
    }
}
