using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Installs a prebaked avatar's original FX controller and expression menu.</summary>
    internal static class PhantomSourceIntegrationBuilder
    {
        public static void Install(
            BuildContext ctx,
            PhantomSystemBuildState system,
            PhantomSlotBuildState slot,
            PhantomBuildReport report)
        {
            InstallPhysBoneParameterMappings(slot);

            var descriptor = slot.BakedAvatar;
            if (descriptor == null || slot.Slot == null || slot.Slot.removeOriginalFx)
            {
                return;
            }

            var host = EnsureOriginalIntegrationHost(slot);
            if (host == null)
            {
                return;
            }

            var fxController = slot.ProcessedFxController
                               ?? PhantomSourceFxControllerUtility.GetController(descriptor);
            if (fxController != null)
            {
                var mergeAnimator = host.AddComponent<ModularAvatarMergeAnimator>();
                mergeAnimator.animator = fxController;
                mergeAnimator.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
                mergeAnimator.pathMode = MergeAnimatorPathMode.Relative;
                mergeAnimator.matchAvatarWriteDefaults = true;

                var root = new AvatarObjectReference();
                root.Set(slot.CloneRoot);
                mergeAnimator.relativePathRoot = root;
                slot.OriginalMergeAnimator = mergeAnimator;
            }

            var parameterConfigs = PhantomParameterConfigBuilder.Build(slot, fxController);
            if (parameterConfigs.Count > 0)
            {
                var parameters = host.AddComponent<ModularAvatarParameters>();
                parameters.parameters = parameterConfigs;
            }

            if (system.AuthoringComponent.options.installPhantomMenu
                && slot.Slot.includePhantomMenu
                && descriptor.expressionsMenu != null)
            {
                InstallOriginalMenu(
                    ctx,
                    host,
                    slot,
                    descriptor.expressionsMenu,
                    slot.GeneratedCoreMenu,
                    report);
            }
        }

        private static void InstallPhysBoneParameterMappings(PhantomSlotBuildState slot)
        {
            var parameterConfigs = PhantomParameterConfigBuilder.BuildPhysBonePrefixes(slot);
            if (parameterConfigs.Count == 0 || slot.CloneRoot == null)
            {
                return;
            }

            var parameters = slot.CloneRoot.GetComponent<ModularAvatarParameters>();
            if (parameters == null)
            {
                parameters = slot.CloneRoot.AddComponent<ModularAvatarParameters>();
            }

            if (parameters.parameters == null)
            {
                parameters.parameters = new List<ParameterConfig>();
            }

            foreach (var config in parameterConfigs)
            {
                var existingIndex = parameters.parameters.FindIndex(existing =>
                    existing.isPrefix
                    && string.Equals(
                        existing.nameOrPrefix,
                        config.nameOrPrefix,
                        System.StringComparison.Ordinal));
                if (existingIndex >= 0)
                {
                    parameters.parameters[existingIndex] = config;
                }
                else
                {
                    parameters.parameters.Add(config);
                }
            }
        }

        private static void InstallOriginalMenu(
            BuildContext ctx,
            GameObject host,
            PhantomSlotBuildState slot,
            VRCExpressionsMenu sourceMenu,
            VRCExpressionsMenu coreMenu,
            PhantomBuildReport report)
        {
            if (coreMenu == null)
            {
                report.Warning($"Slot '{slot.SlotId}' needs a PhantomSystem menu target, but no generated menu is available.");
                return;
            }

            var wrapperMenu = CreateOriginalMenuWrapper(slot, sourceMenu);
            ctx.AssetSaver.SaveAsset(wrapperMenu);

            var installer = host.AddComponent<ModularAvatarMenuInstaller>();
            installer.menuToAppend = wrapperMenu;
            installer.installTargetMenu = coreMenu;
        }

        private static GameObject EnsureOriginalIntegrationHost(PhantomSlotBuildState slot)
        {
            if (slot.OriginalIntegrationHost != null)
            {
                return slot.OriginalIntegrationHost;
            }

            if (slot.SlotRoot == null)
            {
                return null;
            }

            var host = new GameObject("PhantomOriginalFX_MA");
            host.transform.SetParent(slot.SlotRoot.transform, false);
            slot.OriginalIntegrationHost = host;
            return host;
        }

        private static VRCExpressionsMenu CreateOriginalMenuWrapper(
            PhantomSlotBuildState slot,
            VRCExpressionsMenu menu)
        {
            var wrapper = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            wrapper.name = $"PhantomSystem_{slot.SlotId}_OriginalMenu";
            wrapper.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = OriginalMenuControlName(slot),
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = menu,
                    icon = PhantomMenuIconAssets.Load("menu-2")
                }
            };

            return wrapper;
        }

        private static string OriginalMenuControlName(PhantomSlotBuildState slot)
        {
            return string.IsNullOrWhiteSpace(slot.SourceAvatar?.name)
                ? "Original Menu"
                : $"{slot.SourceAvatar.name} Menu";
        }
    }
}
