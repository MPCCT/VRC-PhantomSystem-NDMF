using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>
    /// Disambiguates cloned humanoid armature roots after Modular Avatar has merged the source controllers.
    /// </summary>
    public static class PhantomArmatureRenamer
    {
        public static void Rename(BuildContext ctx, PhantomSystemBuildState system)
        {
            if (system == null)
            {
                return;
            }

            var renamedAny = false;
            foreach (var slot in system.Slots)
            {
                var armature = slot.CloneArmature;
                if (armature == null)
                {
                    continue;
                }

                // Match Modular Avatar's Setup Outfit safeguard: only mangle the cloned
                // armature when the avatar root contains a direct child with the same name.
                var matchingRootObject = ctx.AvatarRootTransform.Find(armature.name);
                if (matchingRootObject == null || matchingRootObject == armature)
                {
                    continue;
                }

                armature.name = $"{armature.name}.Phantom_{slot.HierarchyName}";
                renamedAny = true;
            }

            if (!renamedAny)
            {
                return;
            }

            // The context snapshots the complete hierarchy before this pass. Invalidating
            // its cache makes it rewrite every affected descendant animation path on exit.
            ctx.Extension<AnimatorServicesContext>().ObjectPathRemapper.ClearCache();

            // Refresh Unity's humanoid armature cache in the same way as MA Setup Outfit.
            var avatarAnimator = ctx.AvatarRootObject.GetComponent<Animator>();
            if (avatarAnimator != null)
            {
                var humanoidAvatar = avatarAnimator.avatar;
                avatarAnimator.avatar = null;
                avatarAnimator.avatar = humanoidAvatar;
            }
        }
    }
}
