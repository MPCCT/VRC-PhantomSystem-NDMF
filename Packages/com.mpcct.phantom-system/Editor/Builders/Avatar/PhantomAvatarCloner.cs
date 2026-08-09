using nadena.dev.ndmf;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Creates and sanitizes the prebaked avatar clone used by a slot.</summary>
    public static class PhantomAvatarCloner
    {
        public static void CloneSystem(BuildContext ctx, PhantomSystemBuildState system)
        {
            system.RuntimeRoot = new GameObject("PhantomSystem_Runtime");
            system.RuntimeRoot.transform.SetParent(ctx.AvatarRootTransform, false);

            system.SlotsRoot = new GameObject("Slots");
            system.SlotsRoot.transform.SetParent(system.RuntimeRoot.transform, false);

            foreach (var slot in system.Slots)
            {
                if (slot.PrebakedRoot == null)
                {
                    continue;
                }

                slot.SlotRoot = new GameObject(slot.HierarchyName);
                slot.SlotRoot.transform.SetParent(system.SlotsRoot.transform, false);

                var clone = Object.Instantiate(slot.PrebakedRoot, slot.SlotRoot.transform);
                clone.name = "PhantomAvatar";
                clone.transform.localPosition = Vector3.zero;
                clone.transform.localRotation = Quaternion.identity;
                clone.transform.localScale = Vector3.one;
                clone.SetActive(false);
                RemoveBuildOnlyComponents(clone);
                ApplyPhysBoneOverrides(slot, clone);

                slot.CloneRoot = clone;
                slot.BakedAvatar = clone.GetComponent<VRCAvatarDescriptor>();
                slot.CloneAnimator = clone.GetComponent<Animator>();
            }
        }

        public static void CleanupNestedAvatarComponents(PhantomSlotBuildState slot)
        {
            if (slot.CloneRoot == null)
            {
                return;
            }

            foreach (var descriptor in slot.CloneRoot.GetComponentsInChildren<VRCAvatarDescriptor>(true))
            {
                Object.DestroyImmediate(descriptor);
            }

            var rootAnimator = slot.CloneRoot.GetComponent<Animator>();
            if (rootAnimator != null)
            {
                Object.DestroyImmediate(rootAnimator);
            }

            RemoveBuildOnlyComponents(slot.CloneRoot);
        }

        private static void RemoveBuildOnlyComponents(GameObject cloneRoot)
        {
            foreach (var authoring in cloneRoot.GetComponentsInChildren<PhantomAuthoring>(true))
            {
                Object.DestroyImmediate(authoring);
            }

            foreach (var component in cloneRoot.GetComponentsInChildren<Component>(true))
            {
                var typeName = component != null ? component.GetType().FullName : null;
                if (typeName == "VRC.Core.PipelineManager"
                    || typeName == "nadena.dev.ndmf.runtime.AlreadyProcessedTag"
                    || typeName == "nadena.dev.ndmf.multiplatform.components.PortableDynamicBone"
                    || typeName == "nadena.dev.ndmf.multiplatform.components.PortableDynamicBoneCollider")
                {
                    Object.DestroyImmediate(component);
                }
            }
        }

        private static void ApplyPhysBoneOverrides(
            PhantomSlotBuildState slot,
            GameObject cloneRoot)
        {
            if (slot.Slot == null || !slot.Slot.overridePhysBoneImmobileType)
            {
                return;
            }

            foreach (var physBone in cloneRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                physBone.immobileType = VRCPhysBoneBase.ImmobileType.AllMotion;
            }
        }
    }
}
