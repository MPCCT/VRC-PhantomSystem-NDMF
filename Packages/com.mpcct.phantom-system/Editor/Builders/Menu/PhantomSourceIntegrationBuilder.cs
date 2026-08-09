using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Installs a prebaked avatar's source playable controllers, parameters, and menu.</summary>
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
            if (descriptor == null || slot.Slot == null || slot.Slot.removeSourceControls)
            {
                return;
            }

            var host = EnsureSourceIntegrationHost(slot);
            if (host == null)
            {
                return;
            }

            slot.SourceFxMergeAnimator = AddMergeAnimator(
                host,
                slot,
                slot.ProcessedFxController,
                VRCAvatarDescriptor.AnimLayerType.FX);
            slot.SourceActionMergeAnimator = AddMergeAnimator(
                host,
                slot,
                slot.ProcessedActionController,
                VRCAvatarDescriptor.AnimLayerType.FX);
            slot.SourceGestureMergeAnimator = AddMergeAnimator(
                host,
                slot,
                slot.ProcessedGestureController,
                VRCAvatarDescriptor.AnimLayerType.Gesture);

            var parameterConfigs = PhantomParameterConfigBuilder.Build(
                slot,
                new[]
                {
                    slot.ProcessedFxController,
                    slot.ProcessedActionController,
                    slot.ProcessedGestureController
                });
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

        private static ModularAvatarMergeAnimator AddMergeAnimator(
            GameObject host,
            PhantomSlotBuildState slot,
            RuntimeAnimatorController controller,
            VRCAvatarDescriptor.AnimLayerType layerType)
        {
            if (controller == null || slot.CloneRoot == null)
            {
                return null;
            }

            var mergeAnimator = host.AddComponent<ModularAvatarMergeAnimator>();
            mergeAnimator.animator = controller;
            mergeAnimator.layerType = layerType;
            mergeAnimator.pathMode = MergeAnimatorPathMode.Relative;
            mergeAnimator.matchAvatarWriteDefaults = true;

            var root = new AvatarObjectReference();
            root.Set(slot.CloneRoot);
            mergeAnimator.relativePathRoot = root;
            return mergeAnimator;
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
                report.InternalError(
                    $"Slot '{slot.SlotId}' requested source menu installation, but the enabled Core Menu builder returned no menu.");
                return;
            }

            var wrapperMenu = CreateOriginalMenuWrapper(slot, sourceMenu);
            ctx.AssetSaver.SaveAsset(wrapperMenu);

            var installer = host.AddComponent<ModularAvatarMenuInstaller>();
            installer.menuToAppend = wrapperMenu;
            installer.installTargetMenu = coreMenu;
        }

        private static GameObject EnsureSourceIntegrationHost(PhantomSlotBuildState slot)
        {
            if (slot.SourceIntegrationHost != null)
            {
                return slot.SourceIntegrationHost;
            }

            if (slot.SlotRoot == null)
            {
                return null;
            }

            var host = new GameObject("PhantomSourceControls_MA");
            host.transform.SetParent(slot.SlotRoot.transform, false);
            slot.SourceIntegrationHost = host;
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
