using nadena.dev.ndmf;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Coordinates generated menus, parameters, and prebaked avatar integration.</summary>
    public static class PhantomMenuAndParameterBuilder
    {
        public static void Install(
            BuildContext ctx,
            PhantomSystemBuildState system,
            PhantomSlotBuildState slot,
            PhantomBuildReport report)
        {
            PhantomCoreMenuBuilder.PrepareSystem(system);

            if (slot.SlotRoot == null || slot.BakedAvatar == null)
            {
                return;
            }

            var host = new GameObject("PhantomMA");
            host.transform.SetParent(slot.ContentRoot, false);

            slot.GeneratedCoreMenu = PhantomCoreMenuBuilder.Install(ctx, system, slot, host);
            PhantomSourceIntegrationBuilder.Install(ctx, system, slot, report);

            if (slot.GeneratedCoreMenu != null)
            {
                ctx.AssetSaver.SaveAsset(slot.GeneratedCoreMenu);
            }
        }
    }
}
