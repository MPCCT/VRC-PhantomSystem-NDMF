using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Applies source parameter mappings to the exact components captured before rig generation.</summary>
    internal static class PhantomSourceComponentParameterMapper
    {
        public static void Capture(PhantomSlotBuildState slot)
        {
            slot.SourceComponentParameters.Clear();
            if (slot.CloneRoot == null)
            {
                return;
            }

            foreach (var contact in slot.CloneRoot.GetComponentsInChildren<VRCContactReceiver>(true))
            {
                Add(slot, contact, contact.parameter, PhantomSourceComponentParameterKind.Contact);
            }

            foreach (var physBone in slot.CloneRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                Add(slot, physBone, physBone.parameter, PhantomSourceComponentParameterKind.PhysBonePrefix);
            }

            foreach (var raycast in slot.CloneRoot.GetComponentsInChildren<VRCRaycast>(true))
            {
                Add(slot, raycast, raycast.Parameter, PhantomSourceComponentParameterKind.RaycastPrefix);
            }
        }

        public static void Apply(PhantomSlotBuildState slot)
        {
            if (slot == null)
            {
                return;
            }

            foreach (var reference in slot.SourceComponentParameters)
            {
                if (reference?.Component == null
                    || string.IsNullOrWhiteSpace(reference.OriginalName)
                    || !PhantomSourceParameterMapping.TryResolve(
                        slot,
                        reference.OriginalName,
                        UsageName(reference.Kind),
                        out var finalName))
                {
                    continue;
                }

                switch (reference.Kind)
                {
                    case PhantomSourceComponentParameterKind.Contact:
                        ((VRCContactReceiver)reference.Component).parameter = finalName;
                        break;
                    case PhantomSourceComponentParameterKind.PhysBonePrefix:
                        ((VRCPhysBone)reference.Component).parameter = finalName;
                        break;
                    case PhantomSourceComponentParameterKind.RaycastPrefix:
                        ((VRCRaycast)reference.Component).Parameter = finalName;
                        break;
                }
            }
        }

        private static void Add(
            PhantomSlotBuildState slot,
            UnityEngine.Component component,
            string originalName,
            PhantomSourceComponentParameterKind kind)
        {
            if (component == null || string.IsNullOrWhiteSpace(originalName))
            {
                return;
            }

            slot.SourceComponentParameters.Add(new PhantomSourceComponentParameterReference
            {
                Component = component,
                OriginalName = originalName,
                Kind = kind
            });
        }

        private static string UsageName(PhantomSourceComponentParameterKind kind)
        {
            return kind switch
            {
                PhantomSourceComponentParameterKind.Contact => "Contact Receiver",
                PhantomSourceComponentParameterKind.PhysBonePrefix => "PhysBone prefix",
                PhantomSourceComponentParameterKind.RaycastPrefix => "Raycast prefix",
                _ => "source component"
            };
        }
    }
}
