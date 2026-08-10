using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Builds PhantomSystem core parameters and expression menus.</summary>
    internal static class PhantomCoreMenuBuilder
    {
        public static void PrepareSystem(PhantomSystemBuildState system)
        {
            if (!system.AuthoringComponent.options.installPhantomMenu)
            {
                RemoveCoreMenuInstaller(system);
            }
        }

        public static VRCExpressionsMenu Install(
            BuildContext ctx,
            PhantomSystemBuildState system,
            PhantomSlotBuildState slot,
            GameObject host)
        {
            InstallCoreAnimatorAndParameters(host, slot);
            return InstallCoreMenu(ctx, system, slot);
        }

        private static void InstallCoreAnimatorAndParameters(GameObject host, PhantomSlotBuildState slot)
        {
            var mergeAnimator = host.AddComponent<ModularAvatarMergeAnimator>();
            mergeAnimator.animator = slot.GeneratedController;
            mergeAnimator.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            mergeAnimator.pathMode = MergeAnimatorPathMode.Absolute;
            mergeAnimator.matchAvatarWriteDefaults = true;
            slot.CoreMergeAnimator = mergeAnimator;

            if (slot.GeneratedPhantomViewController != null)
            {
                var phantomViewMergeAnimator = host.AddComponent<ModularAvatarMergeAnimator>();
                phantomViewMergeAnimator.animator = slot.GeneratedPhantomViewController;
                phantomViewMergeAnimator.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
                phantomViewMergeAnimator.pathMode = MergeAnimatorPathMode.Absolute;
                phantomViewMergeAnimator.matchAvatarWriteDefaults = false;
                slot.PhantomViewMergeAnimator = phantomViewMergeAnimator;
            }

            var parameters = host.AddComponent<ModularAvatarParameters>();
            parameters.parameters = new List<ParameterConfig>
            {
                BoolParameter(PhantomParameterNames.Activate(slot.Slot), false, false),
                BoolParameter(PhantomParameterNames.Freeze(slot.Slot), false, false),
                BoolParameter(PhantomParameterNames.PositionLock(slot.Slot), true, false)
            };
            if (slot.Slot.enableScaleControl)
            {
                parameters.parameters.Add(FloatParameter(
                    PhantomParameterNames.Scale(slot.Slot),
                    ScaleControlAnimatorModule.DefaultScaleParameter,
                    false));
                parameters.parameters.Add(BoolParameter(
                    PhantomParameterNames.Mirror(slot.Slot),
                    false,
                    false));
                parameters.parameters.Add(LocalBoolParameter(
                    PhantomParameterNames.ScaleReset(slot.Slot)));
            }
            if (slot.Slot.enablePhantomGrabbing)
            {
                parameters.parameters.Add(BoolParameter(
                    PhantomParameterNames.PhantomGrabbingShowBones(slot.Slot),
                    false,
                    false));
            }
            if (slot.Slot.enablePhantomView)
            {
                parameters.parameters.Add(LocalBoolParameter(
                    PhantomParameterNames.PhantomViewEnabled(slot.Slot)));
                parameters.parameters.Add(LocalFloatParameter(
                    PhantomParameterNames.PhantomViewStereoStrength(slot.Slot),
                    PhantomViewAnimatorModule.DefaultStereoStrengthParameter));
                parameters.parameters.Add(LocalFloatParameter(
                    PhantomParameterNames.PhantomViewMaskSize(slot.Slot),
                    PhantomViewAnimatorModule.DefaultMaskSizeParameter));
            }
        }

        internal static void InstallTrackingAnimator(PhantomSlotBuildState slot)
        {
            if (slot?.GeneratedTrackingController == null
                || slot.CoreMergeAnimator == null
                || slot.TrackingMergeAnimator != null)
            {
                return;
            }

            var trackingMergeAnimator =
                slot.CoreMergeAnimator.gameObject.AddComponent<ModularAvatarMergeAnimator>();
            trackingMergeAnimator.animator = slot.GeneratedTrackingController;
            trackingMergeAnimator.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            trackingMergeAnimator.pathMode = MergeAnimatorPathMode.Absolute;
            trackingMergeAnimator.matchAvatarWriteDefaults = false;
            slot.TrackingMergeAnimator = trackingMergeAnimator;
        }

        private static VRCExpressionsMenu InstallCoreMenu(
            BuildContext ctx,
            PhantomSystemBuildState system,
            PhantomSlotBuildState slot)
        {
            if (!system.AuthoringComponent.options.installPhantomMenu)
            {
                return null;
            }

            var menu = CreateCoreMenu(ctx, slot);
            if (system.Slots.Count == 1)
            {
                EnsureRootMenuInstaller(ctx, system, menu);
                return menu;
            }

            EnsureSystemMenuInstaller(ctx, system);
            system.GeneratedSystemMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = SlotMenuControlName(slot),
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = menu,
                icon = PhantomMenuIconAssets.Load("users")
            });
            return menu;
        }

        private static void EnsureSystemMenuInstaller(
            BuildContext ctx,
            PhantomSystemBuildState system)
        {
            if (system.GeneratedSystemMenu != null)
            {
                return;
            }

            var systemMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            systemMenu.name = "PhantomSystem_SlotsMenu";
            systemMenu.controls = new List<VRCExpressionsMenu.Control>();
            system.GeneratedSystemMenu = systemMenu;
            ctx.AssetSaver.SaveAsset(systemMenu);
            EnsureRootMenuInstaller(ctx, system, systemMenu);
        }

        private static void EnsureRootMenuInstaller(
            BuildContext ctx,
            PhantomSystemBuildState system,
            VRCExpressionsMenu targetMenu)
        {
            if (system.GeneratedRootMenu != null)
            {
                return;
            }

            var rootMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            rootMenu.name = "PhantomSystem_RootMenu";
            rootMenu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = "PhantomSystem",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = targetMenu
                }
            };

            var installer = EnsureCoreMenuInstaller(system);
            installer.menuToAppend = rootMenu;

            system.GeneratedRootMenu = rootMenu;
            ctx.AssetSaver.SaveAsset(rootMenu);
        }

        private static ModularAvatarMenuInstaller EnsureCoreMenuInstaller(
            PhantomSystemBuildState system)
        {
            var authoring = system.AuthoringComponent;
            var installer = authoring.coreMenuInstaller;
            if (installer != null && installer.gameObject == authoring.gameObject)
            {
                return installer;
            }

            installer = authoring.gameObject.AddComponent<ModularAvatarMenuInstaller>();
            authoring.coreMenuInstaller = installer;
            return installer;
        }

        private static void RemoveCoreMenuInstaller(PhantomSystemBuildState system)
        {
            var authoring = system.AuthoringComponent;
            var installer = authoring.coreMenuInstaller;
            if (installer != null)
            {
                Object.DestroyImmediate(installer);
            }

            authoring.coreMenuInstaller = null;
        }

        private static VRCExpressionsMenu CreateCoreMenu(
            BuildContext ctx,
            PhantomSlotBuildState slot)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = $"PhantomSystem_{slot.SlotId}_Menu";
            menu.controls = new List<VRCExpressionsMenu.Control>
            {
                Toggle("Activate", PhantomParameterNames.Activate(slot.Slot), "power"),
                Toggle("Freeze", PhantomParameterNames.Freeze(slot.Slot), "snowflake"),
                Toggle("Position Lock", PhantomParameterNames.PositionLock(slot.Slot), "lock")
            };

            if (slot.Slot.enableScaleControl
                || slot.Slot.enablePhantomGrabbing
                || slot.Slot.enablePhantomView)
            {
                var settingsMenu = CreateSettingsMenu(ctx, slot);
                ctx.AssetSaver.SaveAsset(settingsMenu);
                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Settings",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = settingsMenu,
                    icon = PhantomMenuIconAssets.Load("settings")
                });
            }

            return menu;
        }

        private static VRCExpressionsMenu CreateSettingsMenu(
            BuildContext ctx,
            PhantomSlotBuildState slot)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = $"PhantomSystem_{slot.SlotId}_SettingsMenu";
            menu.controls = new List<VRCExpressionsMenu.Control>();
            if (slot.Slot.enableScaleControl)
            {
                var scaleParameter = PhantomParameterNames.Scale(slot.Slot);
                menu.controls.AddRange(new[]
                {
                    new VRCExpressionsMenu.Control
                    {
                        name = "Scale",
                        type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                        icon = PhantomMenuIconAssets.Load("arrows-diagonal"),
                        subParameters = new[]
                        {
                            new VRCExpressionsMenu.Control.Parameter { name = scaleParameter }
                        }
                    },
                    new VRCExpressionsMenu.Control
                    {
                        name = "Reset Scale",
                        type = VRCExpressionsMenu.Control.ControlType.Button,
                        parameter = new VRCExpressionsMenu.Control.Parameter
                        {
                            name = PhantomParameterNames.ScaleReset(slot.Slot)
                        },
                        value = 1f,
                        icon = PhantomMenuIconAssets.Load("restore")
                    },
                    Toggle("Mirror", PhantomParameterNames.Mirror(slot.Slot), "flip-horizontal")
                });
            }
            if (slot.Slot.enablePhantomGrabbing)
            {
                menu.controls.Add(Toggle(
                    "Bone Display",
                    PhantomParameterNames.PhantomGrabbingShowBones(slot.Slot),
                    "bone"));
            }
            if (slot.Slot.enablePhantomView)
            {
                var phantomViewMenu = CreatePhantomViewMenu(slot);
                ctx.AssetSaver.SaveAsset(phantomViewMenu);
                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Phantom View",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = phantomViewMenu,
                    icon = PhantomMenuIconAssets.Load("eye")
                });
            }

            return menu;
        }

        private static VRCExpressionsMenu CreatePhantomViewMenu(
            PhantomSlotBuildState slot)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = $"PhantomSystem_{slot.SlotId}_PhantomViewMenu";
            menu.controls = new List<VRCExpressionsMenu.Control>
            {
                Toggle(
                    "Enabled",
                    PhantomParameterNames.PhantomViewEnabled(slot.Slot),
                    "eye"),
                new VRCExpressionsMenu.Control
                {
                    name = "Stereo Strength",
                    type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    icon = PhantomMenuIconAssets.Load("arrows-diagonal"),
                    subParameters = new[]
                    {
                        new VRCExpressionsMenu.Control.Parameter
                        {
                            name = PhantomParameterNames.PhantomViewStereoStrength(slot.Slot)
                        }
                    }
                },
                new VRCExpressionsMenu.Control
                {
                    name = "Mask Size",
                    type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    icon = PhantomMenuIconAssets.Load("arrows-diagonal"),
                    subParameters = new[]
                    {
                        new VRCExpressionsMenu.Control.Parameter
                        {
                            name = PhantomParameterNames.PhantomViewMaskSize(slot.Slot)
                        }
                    }
                }
            };
            return menu;
        }

        private static string SlotMenuControlName(PhantomSlotBuildState slot)
        {
            return string.IsNullOrWhiteSpace(slot.SlotId)
                ? PhantomSlot.DefaultId
                : slot.SlotId;
        }

        private static ParameterConfig BoolParameter(string name, bool defaultValue, bool saved)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name,
                defaultValue = defaultValue ? 1f : 0f,
                saved = saved,
                localOnly = false,
                syncType = ParameterSyncType.Bool
            };
        }

        private static ParameterConfig FloatParameter(string name, float defaultValue, bool saved)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name,
                defaultValue = defaultValue,
                hasExplicitDefaultValue = true,
                saved = saved,
                localOnly = false,
                syncType = ParameterSyncType.Float
            };
        }

        private static ParameterConfig LocalBoolParameter(string name)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name,
                defaultValue = 0f,
                hasExplicitDefaultValue = true,
                saved = false,
                localOnly = true,
                syncType = ParameterSyncType.Bool
            };
        }

        private static ParameterConfig LocalFloatParameter(
            string name,
            float defaultValue)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name,
                defaultValue = defaultValue,
                hasExplicitDefaultValue = true,
                saved = false,
                localOnly = true,
                syncType = ParameterSyncType.Float
            };
        }

        private static VRCExpressionsMenu.Control Toggle(
            string name,
            string parameter,
            string iconName = null)
        {
            return new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                icon = string.IsNullOrEmpty(iconName)
                    ? null
                    : PhantomMenuIconAssets.Load(iconName)
            };
        }
    }

    internal static class PhantomMenuIconAssets
    {
        private const string Directory =
            "Packages/com.mpcct.phantom-system/Asset/TablerIcons/";

        public static Texture2D Load(string iconName)
        {
            var assetPath = $"{Directory}{iconName}.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            return sprite != null
                ? sprite.texture
                : AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}
