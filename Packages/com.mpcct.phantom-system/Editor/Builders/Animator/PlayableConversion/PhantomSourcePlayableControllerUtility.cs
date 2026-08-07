using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem.Editor
{
    internal readonly struct PhantomSourcePlayableLayer
    {
        public readonly VRCAvatarDescriptor.AnimLayerType Type;
        public readonly RuntimeAnimatorController Controller;
        public readonly AvatarMask Mask;
        public readonly bool IsDefault;

        public PhantomSourcePlayableLayer(
            VRCAvatarDescriptor.AnimLayerType type,
            RuntimeAnimatorController controller,
            AvatarMask mask,
            bool isDefault)
        {
            Type = type;
            Controller = controller;
            Mask = mask;
            IsDefault = isDefault;
        }
    }

    internal static class PhantomSourcePlayableControllerUtility
    {
        public static bool TryGetLayer(
            VRCAvatarDescriptor descriptor,
            VRCAvatarDescriptor.AnimLayerType type,
            out PhantomSourcePlayableLayer layer)
        {
            layer = default;
            if (descriptor == null
                || !descriptor.customizeAnimationLayers
                || descriptor.baseAnimationLayers == null)
            {
                return false;
            }

            foreach (var candidate in descriptor.baseAnimationLayers)
            {
                if (candidate.type != type)
                {
                    continue;
                }

                layer = new PhantomSourcePlayableLayer(
                    type,
                    candidate.animatorController,
                    candidate.mask,
                    candidate.isDefault);
                return true;
            }

            return false;
        }
    }
}
