using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Converts prebaked avatar parameter definitions into Modular Avatar mappings.</summary>
    internal static class PhantomParameterConfigBuilder
    {
        public static List<ParameterConfig> Build(
            PhantomSlotBuildState slot,
            IEnumerable<RuntimeAnimatorController> controllers)
        {
            return BuildStandardMappings(slot, controllers).Values.ToList();
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

            if (slot?.ParameterResolution != null)
            {
                foreach (var originalName in slot.ParameterResolution.FinalNames.Keys
                             .OrderBy(name => name, StringComparer.Ordinal))
                {
                    AddRemapOnlyParameter(configs, originalName, slot);
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

            if (PhantomSourceParameterMapping.TryResolve(
                    slot,
                    parameter.name,
                    "Expression Parameters",
                    out var finalName)
                && !string.Equals(finalName, parameter.name, StringComparison.Ordinal))
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

            if (!PhantomSourceParameterMapping.TryResolve(
                    slot,
                    name,
                    "Source Controller or Menu",
                    out var finalName)
                || string.Equals(finalName, name, StringComparison.Ordinal))
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

            return false;
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
