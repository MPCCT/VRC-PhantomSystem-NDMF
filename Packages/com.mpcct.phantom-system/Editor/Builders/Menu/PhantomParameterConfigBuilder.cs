using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Projects the resolved slot parameter plan into Modular Avatar mappings.</summary>
    internal static class PhantomParameterConfigBuilder
    {
        public static List<ParameterConfig> Build(PhantomSlotBuildState slot)
        {
            var configs = new Dictionary<string, ParameterConfig>(StringComparer.Ordinal);
            var plan = slot?.ParameterPlan;
            if (plan == null)
            {
                return configs.Values.ToList();
            }

            foreach (var parameter in plan.SourceParameters)
            {
                if (parameter == null || ShouldSkipOriginalParameter(parameter.Name, slot))
                {
                    continue;
                }

                if (!parameter.IsAnimatorOnly && !parameter.IsParameterPrefix)
                {
                    AddDeclaredParameter(configs, parameter, slot);
                }
                else
                {
                    AddRemapOnlyParameter(configs, parameter.Name, slot);
                }
            }

            foreach (var originalName in plan.FinalParameterNames.Keys
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                AddRemapOnlyParameter(configs, originalName, slot);
            }

            return configs.Values.ToList();
        }

        private static void AddDeclaredParameter(
            IDictionary<string, ParameterConfig> configs,
            PhantomParameterDefinition parameter,
            PhantomSlotBuildState slot)
        {
            var config = new ParameterConfig
            {
                nameOrPrefix = parameter.Name,
                defaultValue = parameter.DefaultValue ?? 0f,
                hasExplicitDefaultValue = true,
                saved = parameter.Saved ?? false,
                localOnly = !parameter.WantSynced,
                syncType = ConvertSyncType(parameter.ParameterType)
            };

            if (PhantomSourceParameterMapping.TryResolve(
                    slot,
                    parameter.Name,
                    "Expression Parameters",
                    out var finalName)
                && !string.Equals(finalName, parameter.Name, StringComparison.Ordinal))
            {
                config.remapTo = finalName;
            }

            configs[parameter.Name] = config;
        }

        private static void AddRemapOnlyParameter(
            IDictionary<string, ParameterConfig> configs,
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
            return string.IsNullOrWhiteSpace(name)
                   || PhantomParameterPolicy.IsVrcReserved(name)
                   || slot?.Slot == null;
        }

        private static ParameterSyncType ConvertSyncType(AnimatorControllerParameterType? type)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Bool:
                    return ParameterSyncType.Bool;
                case AnimatorControllerParameterType.Int:
                    return ParameterSyncType.Int;
                case AnimatorControllerParameterType.Float:
                    return ParameterSyncType.Float;
                default:
                    return ParameterSyncType.NotSynced;
            }
        }
    }
}
