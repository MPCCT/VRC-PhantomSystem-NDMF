using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Converts prebaked avatar parameter definitions into Modular Avatar mappings.</summary>
    internal static class PhantomParameterConfigBuilder
    {
        // Modular Avatar uses the legacy PhysBonesPrefix namespace for both PhysBones and Raycasts,
        // and expands a prefix mapping over this complete suffix set.
        private static readonly string[] DynamicParameterSuffixes =
        {
            "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish",
            "_Hit", "_Ratio", "_Distance"
        };

        public static List<ParameterConfig> Build(
            PhantomSlotBuildState slot,
            IEnumerable<RuntimeAnimatorController> controllers)
        {
            return BuildStandardMappings(slot, controllers).Values.ToList();
        }

        public static List<ParameterConfig> BuildCloneMappings(
            PhantomSlotBuildState slot,
            IEnumerable<RuntimeAnimatorController> controllers)
        {
            var configs = slot?.Slot != null && !slot.Slot.removeSourceControls
                ? BuildStandardMappings(slot, controllers)
                : new Dictionary<string, ParameterConfig>(StringComparer.Ordinal);
            var prefixConfigs = new Dictionary<string, ParameterConfig>(StringComparer.Ordinal);

            if (slot?.ParameterResolution != null)
            {
                foreach (var originalName in slot.ParameterResolution.FinalNames.Keys)
                {
                    AddRemapOnlyParameter(configs, originalName, slot);
                }
            }

            if (slot?.CloneRoot != null)
            {
                foreach (var contact in slot.CloneRoot.GetComponentsInChildren<VRCContactReceiver>(true))
                {
                    AddRemapOnlyParameter(configs, contact.parameter, slot);
                }

                foreach (var animator in slot.CloneRoot.GetComponentsInChildren<Animator>(true))
                {
                    var controller = GetBaseController(animator.runtimeAnimatorController);
                    if (controller == null)
                    {
                        continue;
                    }

                    foreach (var parameter in controller.parameters)
                    {
                        AddRemapOnlyParameter(configs, parameter.name, slot);
                    }
                }
            }

            foreach (var prefixConfig in BuildDynamicParameterPrefixes(slot))
            {
                foreach (var suffix in PrefixSuffixes(slot.CloneRoot, prefixConfig.nameOrPrefix))
                {
                    configs.Remove(prefixConfig.nameOrPrefix + suffix);
                }
                prefixConfigs[prefixConfig.nameOrPrefix] = prefixConfig;
            }

            return configs.Values.Concat(prefixConfigs.Values).ToList();
        }

        public static bool IsDerivedDynamicParameter(string prefix, string parameterName)
        {
            return !string.IsNullOrEmpty(prefix)
                   && !string.IsNullOrEmpty(parameterName)
                   && DynamicParameterSuffixes.Any(suffix =>
                       string.Equals(prefix + suffix, parameterName, StringComparison.Ordinal));
        }

        private static Dictionary<string, ParameterConfig> BuildStandardMappings(
            PhantomSlotBuildState slot,
            IEnumerable<RuntimeAnimatorController> controllers)
        {
            var configs = new Dictionary<string, ParameterConfig>(StringComparer.Ordinal);
            var parameters = slot.BakedAvatar != null ? slot.BakedAvatar.expressionParameters : null;
            if (parameters?.parameters != null)
            {
                foreach (var parameter in parameters.parameters)
                {
                    AddExpressionParameterConfig(configs, parameter, slot);
                }
            }

            if (controllers != null)
            {
                foreach (var runtimeController in controllers)
                {
                    var animatorController = GetBaseController(runtimeController);
                    if (animatorController == null)
                    {
                        continue;
                    }

                    foreach (var parameter in animatorController.parameters)
                    {
                        AddRemapOnlyParameter(configs, parameter.name, slot);
                    }
                }
            }

            return configs;
        }

        private static AnimatorController GetBaseController(
            RuntimeAnimatorController controller)
        {
            var current = controller;
            var visited = new HashSet<RuntimeAnimatorController>();
            while (current is AnimatorOverrideController overrideController)
            {
                if (!visited.Add(current))
                {
                    return null;
                }

                current = overrideController.runtimeAnimatorController;
            }

            return current as AnimatorController;
        }

        private static List<ParameterConfig> BuildDynamicParameterPrefixes(PhantomSlotBuildState slot)
        {
            var configs = new Dictionary<string, ParameterConfig>(StringComparer.Ordinal);
            if (slot?.Slot == null
                || slot.CloneRoot == null)
            {
                return configs.Values.ToList();
            }

            foreach (var physBone in slot.CloneRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                var parameter = physBone.parameter;
                if (ShouldSkipOriginalParameter(parameter, slot))
                {
                    continue;
                }

                var finalName = FinalName(slot, parameter);
                if (string.Equals(finalName, parameter, StringComparison.Ordinal))
                {
                    continue;
                }

                configs[parameter] = new ParameterConfig
                {
                    nameOrPrefix = parameter,
                    remapTo = finalName,
                    isPrefix = true,
                    syncType = ParameterSyncType.NotSynced,
                    localOnly = true,
                    saved = false
                };
            }

            foreach (var raycast in slot.CloneRoot.GetComponentsInChildren<VRCRaycast>(true))
            {
                var parameter = raycast.Parameter;
                if (ShouldSkipOriginalParameter(parameter, slot))
                {
                    continue;
                }

                var finalName = FinalName(slot, parameter);
                if (string.Equals(finalName, parameter, StringComparison.Ordinal))
                {
                    continue;
                }

                configs[parameter] = new ParameterConfig
                {
                    nameOrPrefix = parameter,
                    remapTo = finalName,
                    isPrefix = true,
                    syncType = ParameterSyncType.NotSynced,
                    localOnly = true,
                    saved = false
                };
            }

            return configs.Values.ToList();
        }

        private static IEnumerable<string> PrefixSuffixes(GameObject root, string prefix)
        {
            if (root == null)
            {
                yield break;
            }

            var hasDynamicPrefix = root.GetComponentsInChildren<VRCPhysBone>(true).Any(physBone =>
                string.Equals(physBone.parameter, prefix, StringComparison.Ordinal));

            hasDynamicPrefix |= root.GetComponentsInChildren<VRCRaycast>(true).Any(raycast =>
                string.Equals(raycast.Parameter, prefix, StringComparison.Ordinal));

            if (hasDynamicPrefix)
            {
                foreach (var suffix in DynamicParameterSuffixes)
                {
                    yield return suffix;
                }
            }
        }

        private static void AddExpressionParameterConfig(
            Dictionary<string, ParameterConfig> configs,
            VRCExpressionParameters.Parameter parameter,
            PhantomSlotBuildState slot)
        {
            if (parameter == null || ShouldSkipOriginalParameter(parameter.name, slot))
            {
                return;
            }

            var config = new ParameterConfig
            {
                nameOrPrefix = parameter.name,
                defaultValue = parameter.defaultValue,
                hasExplicitDefaultValue = true,
                saved = parameter.saved,
                localOnly = !parameter.networkSynced,
                syncType = ConvertSyncType(parameter.valueType)
            };

            var finalName = FinalName(slot, parameter.name);
            if (!string.Equals(finalName, parameter.name, StringComparison.Ordinal))
            {
                config.remapTo = finalName;
            }

            configs[parameter.name] = config;
        }

        private static void AddRemapOnlyParameter(
            Dictionary<string, ParameterConfig> configs,
            string name,
            PhantomSlotBuildState slot)
        {
            if (ShouldSkipOriginalParameter(name, slot) || configs.ContainsKey(name))
            {
                return;
            }

            var finalName = FinalName(slot, name);
            if (string.Equals(finalName, name, StringComparison.Ordinal))
            {
                return;
            }
            configs[name] = new ParameterConfig
            {
                nameOrPrefix = name,
                remapTo = finalName,
                syncType = ParameterSyncType.NotSynced,
                localOnly = true,
                saved = false
            };
        }

        private static bool ShouldSkipOriginalParameter(string name, PhantomSlotBuildState slot)
        {
            if (string.IsNullOrWhiteSpace(name)
                || PhantomParameterPolicy.IsVrcReserved(name)
                || slot?.Slot == null)
            {
                return true;
            }

            var prefix = PhantomParameterNames.OriginalParameterPrefix(slot.Slot);
            return slot.Slot.renamePhantomParameters
                   && name.StartsWith(prefix, StringComparison.Ordinal);
        }

        private static string FinalName(PhantomSlotBuildState slot, string originalName)
        {
            return slot.ParameterResolution?.FinalName(originalName, slot.Slot)
                   ?? PhantomParameterPolicy.FinalOriginalParameterName(
                       slot.Slot,
                       originalName,
                       slot.ValidSharedParameterNames);
        }

        private static ParameterSyncType ConvertSyncType(VRCExpressionParameters.ValueType type)
        {
            return type switch
            {
                VRCExpressionParameters.ValueType.Bool => ParameterSyncType.Bool,
                VRCExpressionParameters.ValueType.Int => ParameterSyncType.Int,
                VRCExpressionParameters.ValueType.Float => ParameterSyncType.Float,
                _ => ParameterSyncType.NotSynced
            };
        }
    }
}
