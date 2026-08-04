using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Converts prebaked avatar parameter definitions into Modular Avatar mappings.</summary>
    internal static class PhantomParameterConfigBuilder
    {
        public static List<ParameterConfig> Build(
            PhantomSlotBuildState slot,
            RuntimeAnimatorController fxController)
        {
            var configs = new Dictionary<string, ParameterConfig>();
            var parameters = slot.BakedAvatar != null ? slot.BakedAvatar.expressionParameters : null;
            if (parameters?.parameters != null)
            {
                foreach (var parameter in parameters.parameters)
                {
                    AddExpressionParameterConfig(configs, parameter, slot);
                }
            }

            if (slot.Slot.renamePhantomParameters && fxController is AnimatorController animatorController)
            {
                foreach (var parameter in animatorController.parameters)
                {
                    AddRemapOnlyParameter(configs, parameter.name, slot);
                }
            }

            return configs.Values.ToList();
        }

        public static List<ParameterConfig> BuildPhysBonePrefixes(PhantomSlotBuildState slot)
        {
            var configs = new Dictionary<string, ParameterConfig>(StringComparer.Ordinal);
            if (slot?.Slot == null
                || !slot.Slot.renamePhantomParameters
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

                var finalName = PhantomParameterPolicy.FinalOriginalParameterName(
                    slot.Slot,
                    parameter,
                    slot.ValidSharedParameterNames);
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

            if (slot.Slot.renamePhantomParameters)
            {
                var finalName = PhantomParameterPolicy.FinalOriginalParameterName(
                    slot.Slot,
                    parameter.name,
                    slot.ValidSharedParameterNames);
                if (!string.Equals(finalName, parameter.name, StringComparison.Ordinal))
                {
                    config.remapTo = finalName;
                }
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

            configs[name] = new ParameterConfig
            {
                nameOrPrefix = name,
                remapTo = slot.Slot.renamePhantomParameters
                    ? PhantomParameterPolicy.FinalOriginalParameterName(
                        slot.Slot,
                        name,
                        slot.ValidSharedParameterNames)
                    : "",
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
