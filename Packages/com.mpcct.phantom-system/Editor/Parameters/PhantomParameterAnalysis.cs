using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.PhysBone.Components;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    internal sealed class PhantomParameterDefinition
    {
        public string Name;
        public AnimatorControllerParameterType? ParameterType;
        public bool IsAnimatorOnly;
        public bool IsHidden;
        public bool IsPhysBonePrefix;
        public bool WantSynced;
        public float? DefaultValue;
        public bool? Saved;
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
        public Dictionary<string, string> FinalParameterNames =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public List<PhantomParameterRename> AutomaticRenames = new List<PhantomParameterRename>();
    }

    internal sealed class PhantomSystemParameterAnalysis
    {
        public Dictionary<string, PhantomParameterDefinition> BaseParameters =
            new Dictionary<string, PhantomParameterDefinition>(StringComparer.Ordinal);

        public List<PhantomSlotParameterAnalysis> Slots = new List<PhantomSlotParameterAnalysis>();
        public List<string> ResolutionErrors = new List<string>();
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

            var parameterResolution = PhantomParameterResolver.Resolve(
                analysis.BaseParameters,
                analysis.Slots.Select(slotAnalysis => new PhantomParameterSlotInput
                {
                    Slot = slotAnalysis.Slot,
                    Identity = PhantomSlotIdentity.Create(slotAnalysis.Slot),
                    SourceParameters = slotAnalysis.SourceParameters
                }).ToList());
            analysis.ResolutionErrors.AddRange(parameterResolution.Errors);
            for (var index = 0; index < analysis.Slots.Count && index < parameterResolution.Slots.Count; index++)
            {
                var slotAnalysis = analysis.Slots[index];
                var resolved = parameterResolution.Slots[index];
                slotAnalysis.GeneratedParameterCost = resolved.GeneratedParameterCost;
                slotAnalysis.SourceParameterCost = resolved.SourceParameterCost;
                slotAnalysis.SharedParameterSavings = resolved.SharedParameterSavings;
                slotAnalysis.FinalContributionCost = resolved.FinalContributionCost;
                slotAnalysis.NamesSharedWithBase = resolved.SharedOriginalNames;
                slotAnalysis.FinalParameterNames = resolved.FinalNames;
                slotAnalysis.AutomaticRenames = resolved.AutomaticRenames;
            }

            return analysis;
        }

        public static Dictionary<string, PhantomParameterDefinition> ReadBaseParameters(
            GameObject avatarRoot,
            BuildContext context)
        {
            return ReadParameters(avatarRoot, context, true);
        }

        public static Dictionary<string, PhantomParameterDefinition> ReadParametersForObject(
            GameObject root,
            BuildContext context)
        {
            return ReadParameters(root, context, true);
        }

        public static List<PhantomParameterDefinition> ReadPhysBonePrefixes(GameObject root)
        {
            if (root == null)
            {
                return new List<PhantomParameterDefinition>();
            }

            return root.GetComponentsInChildren<VRCPhysBone>(true)
                .Select(physBone => physBone.parameter)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Select(name => new PhantomParameterDefinition
                {
                    Name = name,
                    IsPhysBonePrefix = true,
                    IsAnimatorOnly = true,
                    WantSynced = false
                })
                .ToList();
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
                    Saved = parameter.saved,
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

            if (slot.removeSourceControls)
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
            sourceParameters.AddRange(ReadPhysBonePrefixes(slot.phantomAvatar.gameObject));
            sourceParameters = sourceParameters
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
            return 3
                   + (slot != null && slot.enablePhantomGrabbing ? 1 : 0)
                   + (slot != null && slot.enableScaleControl ? 9 : 0);
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

                var descriptorDefinitions = ReadDescriptorParameters(
                    root.GetComponent<VRCAvatarDescriptor>());
                foreach (var pair in descriptorDefinitions)
                {
                    if (!result.TryGetValue(pair.Key, out var definition))
                    {
                        continue;
                    }

                    definition.Saved = pair.Value.Saved;
                    definition.DefaultValue = pair.Value.DefaultValue;
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
            return PhantomParameterCompatibility.AreCompatible(
                baseParameter,
                sourceParameter,
                out reason);
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
