using System.Collections.Generic;
using System.Linq;
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
            slot.SourcePlayableRegistrations.Clear();
            slot.SourceFxMergeAnimator = null;
            slot.SourceActionMergeAnimator = null;
            slot.SourceGestureMergeAnimator = null;
            slot.GeneratedDriverNeutralController = null;
            slot.DriverNeutralMergeAnimator = null;
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

            foreach (var playable in new[]
                     {
                         VRCAvatarDescriptor.AnimLayerType.FX,
                         VRCAvatarDescriptor.AnimLayerType.Action,
                         VRCAvatarDescriptor.AnimLayerType.Gesture
                     })
            {
                if (!PhantomSourcePlayableControllerUtility.TryGetLayer(
                        descriptor,
                        playable,
                        out var source)
                    || source.IsDefault
                    || source.Controller == null)
                {
                    continue;
                }

                if (!PhantomSourcePlayableControllerProcessor.TryGetBaseController(
                        source.Controller,
                        out var baseController))
                {
                    report.Error(
                        $"Slot '{slot.SlotId}' uses unsupported {playable} controller type "
                        + $"'{source.Controller.GetType().FullName}'.",
                        descriptor);
                    continue;
                }

                var targetPlayable = ResolveMergeTarget(playable);
                var mergeAnimator = AddMergeAnimator(
                    host,
                    slot,
                    source.Controller,
                    targetPlayable);
                if (mergeAnimator == null)
                {
                    continue;
                }

                slot.SourcePlayableRegistrations[playable] =
                    new PhantomSourcePlayableRegistration
                    {
                        Playable = playable,
                        Source = source,
                        BaseController = baseController,
                        MergeAnimator = mergeAnimator
                    };
                AssignMergeAnimator(slot, playable, mergeAnimator);
            }

            PhantomDriverNeutralAnimatorBuilder.Install(
                ctx,
                system,
                slot,
                host,
                report);

            var sourceControllers = slot.SourcePlayableRegistrations.Values
                .Select(registration => registration.Source.Controller)
                .Where(controller => controller != null)
                .ToArray();

            var parameterConfigs = PhantomParameterConfigBuilder.Build(
                slot,
                sourceControllers);
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

        internal static VRCAvatarDescriptor.AnimLayerType ResolveMergeTarget(
            VRCAvatarDescriptor.AnimLayerType sourcePlayable)
        {
            switch (sourcePlayable)
            {
                case VRCAvatarDescriptor.AnimLayerType.FX:
                case VRCAvatarDescriptor.AnimLayerType.Gesture:
                case VRCAvatarDescriptor.AnimLayerType.Action:
                    return VRCAvatarDescriptor.AnimLayerType.FX;
                default:
                    return sourcePlayable;
            }
        }

        private static void AssignMergeAnimator(
            PhantomSlotBuildState slot,
            VRCAvatarDescriptor.AnimLayerType playable,
            ModularAvatarMergeAnimator mergeAnimator)
        {
            switch (playable)
            {
                case VRCAvatarDescriptor.AnimLayerType.FX:
                    slot.SourceFxMergeAnimator = mergeAnimator;
                    break;
                case VRCAvatarDescriptor.AnimLayerType.Action:
                    slot.SourceActionMergeAnimator = mergeAnimator;
                    break;
                case VRCAvatarDescriptor.AnimLayerType.Gesture:
                    slot.SourceGestureMergeAnimator = mergeAnimator;
                    break;
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

            if (slot.ContentRoot == null)
            {
                return null;
            }

            var host = new GameObject("PhantomSourceControls_MA");
            host.transform.SetParent(slot.ContentRoot, false);
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
