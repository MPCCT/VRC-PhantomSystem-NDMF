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
                    "[PhantomSystem] Manual bake requires PhantomSystem to be inside a VRCAvatarDescriptor.",
                    authoring);
                return;
            }

            if (PhantomPrebakeSession.IsPrebaking)
            {
                Debug.LogWarning(
                    "[PhantomSystem] A phantom prebake is already running. Wait for it to finish before starting a manual bake.",
                    authoring);
                return;
            }

            if (!PhantomPrebakeService.Prepare(avatar.gameObject))
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
                    "[PhantomSystem] Manual avatar bake failed. See the NDMF Console for details.",
                    authoring);
                Debug.LogException(exception);
            }
            finally
            {
                PhantomPrebakeSession.CleanupAll();
            }
        }
    }
}
