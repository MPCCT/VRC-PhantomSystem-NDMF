using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem.Editor
{
    internal static class PhantomSourceFxControllerUtility
    {
        public static RuntimeAnimatorController GetController(VRCAvatarDescriptor descriptor)
        {
            return descriptor != null
                   && descriptor.customizeAnimationLayers
                   && descriptor.baseAnimationLayers != null
                   && descriptor.baseAnimationLayers.Length > 4
                ? descriptor.baseAnimationLayers[4].animatorController
                : null;
        }
    }
}
