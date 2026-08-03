using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    internal sealed class PhantomParameterDefinition
    {
        public string Name;
        public AnimatorControllerParameterType? ParameterType;
        public bool IsAnimatorOnly;
        public bool IsHidden;
        public bool WantSynced;
        public float? DefaultValue;
        public Component SourceComponent;
        public PluginBase SourcePlugin;

        public int BitUsage
        {
            get
            {
                if (IsAnimatorOnly || !WantSynced || ParameterType == null)
                {
                    return 0;
                }

                return ParameterType == AnimatorControllerParameterType.Bool ? 1 : 8;
            }
        }
    }

    internal sealed class PhantomSharedParameterCandidate
    {
        public string Name;
        public PhantomParameterDefinition SourceParameter;
        public bool IsCompatible;
        public string IncompatibilityReason;
        public bool IsSelected;
    }

    internal sealed class PhantomSlotParameterAnalysis
    {
        public PhantomSlot Slot;
        public List<PhantomParameterDefinition> SourceParameters = new List<PhantomParameterDefinition>();
        public List<PhantomSharedParameterCandidate> Candidates = new List<PhantomSharedParameterCandidate>();
        public HashSet<string> NamesSharedWithBase = new HashSet<string>(StringComparer.Ordinal);
        public int SourceParameterCost;
        public int SharedParameterSavings;
        public int GeneratedParameterCost;
        public int FinalContributionCost;
    }

    internal sealed class PhantomSystemParameterAnalysis
    {
        public Dictionary<string, PhantomParameterDefinition> BaseParameters =
            new Dictionary<string, PhantomParameterDefinition>(StringComparer.Ordinal);

        public List<PhantomSlotParameterAnalysis> Slots = new List<PhantomSlotParameterAnalysis>();
    }

    internal sealed class PhantomSharedRuleResolution
    {
        public HashSet<string> ValidNames = new HashSet<string>(StringComparer.Ordinal);
        public List<string> Warnings = new List<string>();
    }

    internal static class PhantomParameterAnalysis
    {
        [ThreadStatic]
        private static int providerSuppressionDepth;

        public static bool IsProviderSuppressed => providerSuppressionDepth > 0;

        public static PhantomSystemParameterAnalysis Analyze(PhantomAuthoring authoring)
        {
            var analysis = new PhantomSystemParameterAnalysis();
            if (authoring == null)
            {
                return analysis;
            }

            var avatarRoot = FindAvatarRoot(authoring.transform);
            if (avatarRoot != null)
            {
                analysis.BaseParameters = ReadParameters(avatarRoot.gameObject, null, true);
            }

            var slots = authoring.slots ?? new List<PhantomSlot>();
            foreach (var slot in slots)
            {
                analysis.Slots.Add(AnalyzeSlot(slot, analysis.BaseParameters));
            }

            return analysis;
        }

        public static Dictionary<string, PhantomParameterDefinition> ReadBaseParameters(
            GameObject avatarRoot,
            BuildContext context)
        {
            return ReadParameters(avatarRoot, context, true);
        }

        public static PhantomSharedRuleResolution ResolveBuildSharedRules(
            PhantomSlot slot,
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters,
            VRCAvatarDescriptor prebakedDescriptor)
        {
            var resolution = new PhantomSharedRuleResolution();
            if (slot == null
                || slot.removeOriginalFx
                || !slot.renamePhantomParameters
                || slot.sharedParameterNames == null
                || slot.sharedParameterNames.Count == 0)
            {
                return resolution;
            }

            var sourceParameters = ReadDescriptorParameters(prebakedDescriptor);
            foreach (var name in slot.sharedParameterNames
                         .Where(name => !string.IsNullOrWhiteSpace(name))
                         .Distinct(StringComparer.Ordinal))
            {
                if (PhantomParameterPolicy.IsVrcReserved(name))
                {
                    continue;
                }

                if (!sourceParameters.TryGetValue(name, out var source))
                {
                    resolution.Warnings.Add(
                        $"Shared parameter '{name}' no longer exists in the prebaked phantom expression parameters; it will remain namespaced.");
                    continue;
                }

                if (baseParameters == null || !baseParameters.TryGetValue(name, out var baseParameter))
                {
                    resolution.Warnings.Add(
                        $"Shared parameter '{name}' no longer exists on the base avatar; it will remain namespaced.");
                    continue;
                }

                if (!AreShareCompatible(baseParameter, source, out var reason))
                {
                    resolution.Warnings.Add(
                        $"Shared parameter '{name}' is no longer compatible with the base avatar ({reason}); it will remain namespaced.");
                    continue;
                }

                resolution.ValidNames.Add(name);
            }

            return resolution;
        }

        public static Dictionary<string, PhantomParameterDefinition> ReadDescriptorParameters(
            VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<string, PhantomParameterDefinition>(StringComparer.Ordinal);
            var parameters = descriptor != null ? descriptor.expressionParameters?.parameters : null;
            if (parameters == null)
            {
                return result;
            }

            foreach (var parameter in parameters)
            {
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.name))
                {
                    continue;
                }

                result[parameter.name] = new PhantomParameterDefinition
                {
                    Name = parameter.name,
                    ParameterType = ConvertType(parameter.valueType),
                    IsAnimatorOnly = false,
                    IsHidden = false,
                    WantSynced = parameter.networkSynced,
                    DefaultValue = parameter.defaultValue,
                    SourceComponent = descriptor
                };
            }

            return result;
        }

        private static PhantomSlotParameterAnalysis AnalyzeSlot(
            PhantomSlot slot,
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters)
        {
            var analysis = new PhantomSlotParameterAnalysis { Slot = slot };
            analysis.GeneratedParameterCost = GeneratedParameterCost(slot);
            if (slot?.phantomAvatar == null)
            {
                analysis.FinalContributionCost = analysis.GeneratedParameterCost;
                return analysis;
            }

            if (slot.removeOriginalFx)
            {
                analysis.FinalContributionCost = analysis.GeneratedParameterCost;
                return analysis;
            }

            var sourceParameters = ReadParameters(slot.phantomAvatar.gameObject, null, true)
                .Values
                .Where(parameter => parameter != null
                                    && !string.IsNullOrWhiteSpace(parameter.Name)
                                    && !PhantomParameterPolicy.IsVrcReserved(parameter.Name))
                .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
                .ToList();
            analysis.SourceParameters = sourceParameters;
            analysis.SourceParameterCost = sourceParameters.Sum(parameter => parameter.BitUsage);

            foreach (var source in sourceParameters.Where(parameter => !parameter.IsAnimatorOnly && !parameter.IsHidden))
            {
                if (baseParameters == null || !baseParameters.TryGetValue(source.Name, out var baseParameter))
                {
                    continue;
                }

                var candidate = new PhantomSharedParameterCandidate
                {
                    Name = source.Name,
                    SourceParameter = source,
                    IsSelected = PhantomParameterPolicy.IsConfiguredShared(slot, source.Name)
                };
                candidate.IsCompatible = AreShareCompatible(baseParameter, source, out var reason);
                candidate.IncompatibilityReason = reason;
                analysis.Candidates.Add(candidate);

                if (candidate.IsCompatible && (!slot.renamePhantomParameters || candidate.IsSelected))
                {
                    analysis.NamesSharedWithBase.Add(source.Name);
                    analysis.SharedParameterSavings += source.BitUsage;
                }
            }

            analysis.FinalContributionCost = analysis.GeneratedParameterCost + sourceParameters
                .Where(parameter => !analysis.NamesSharedWithBase.Contains(parameter.Name))
                .Sum(parameter => parameter.BitUsage);
            return analysis;
        }

        private static int GeneratedParameterCost(PhantomSlot slot)
        {
            return 3 + (slot != null && slot.enableScaleControl ? 9 : 0);
        }

        private static Dictionary<string, PhantomParameterDefinition> ReadParameters(
            GameObject root,
            BuildContext context,
            bool suppressPhantomProvider)
        {
            var result = new Dictionary<string, PhantomParameterDefinition>(StringComparer.Ordinal);
            if (root == null)
            {
                return result;
            }

            if (suppressPhantomProvider)
            {
                providerSuppressionDepth++;
            }

            try
            {
                var info = context != null
                    ? ParameterInfo.ForContext(context)
                    : ParameterInfo.ForPreview(ComputeContext.NullContext);
                foreach (var parameter in info.GetParametersForObject(root))
                {
                    if (parameter == null
                        || parameter.Namespace != ParameterNamespace.Animator
                        || string.IsNullOrWhiteSpace(parameter.EffectiveName))
                    {
                        continue;
                    }

                    result[parameter.EffectiveName] = new PhantomParameterDefinition
                    {
                        Name = parameter.EffectiveName,
                        ParameterType = parameter.ParameterType,
                        IsAnimatorOnly = parameter.IsAnimatorOnly,
                        IsHidden = parameter.IsHidden,
                        WantSynced = parameter.WantSynced,
                        DefaultValue = parameter.DefaultValue,
                        SourceComponent = parameter.Source,
                        SourcePlugin = parameter.Plugin
                    };
                }
            }
            finally
            {
                if (suppressPhantomProvider)
                {
                    providerSuppressionDepth--;
                }
            }

            return result;
        }

        private static Transform FindAvatarRoot(Transform start)
        {
            for (var current = start; current != null; current = current.parent)
            {
                if (current.GetComponent<VRCAvatarDescriptor>() != null)
                {
                    return current;
                }
            }

            return null;
        }

        private static bool AreShareCompatible(
            PhantomParameterDefinition baseParameter,
            PhantomParameterDefinition sourceParameter,
            out string reason)
        {
            if (baseParameter == null || sourceParameter == null)
            {
                reason = "parameter information is missing";
                return false;
            }

            if (baseParameter.IsAnimatorOnly || sourceParameter.IsAnimatorOnly)
            {
                reason = "one side is animator-only";
                return false;
            }

            if (baseParameter.ParameterType == null || sourceParameter.ParameterType == null)
            {
                reason = "the parameter type is unknown";
                return false;
            }

            if (baseParameter.ParameterType != sourceParameter.ParameterType)
            {
                reason = $"type mismatch: base {baseParameter.ParameterType}, phantom {sourceParameter.ParameterType}";
                return false;
            }

            if (baseParameter.WantSynced != sourceParameter.WantSynced)
            {
                reason = baseParameter.WantSynced
                    ? "the base parameter is network-synced but the phantom parameter is local-only"
                    : "the base parameter is local-only but the phantom parameter is network-synced";
                return false;
            }

            reason = null;
            return true;
        }

        private static AnimatorControllerParameterType ConvertType(VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    return AnimatorControllerParameterType.Bool;
                case VRCExpressionParameters.ValueType.Int:
                    return AnimatorControllerParameterType.Int;
                case VRCExpressionParameters.ValueType.Float:
                    return AnimatorControllerParameterType.Float;
                default:
                    return AnimatorControllerParameterType.Trigger;
            }
        }
    }
}
