using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    internal sealed class PhantomParameterDefinition
    {
        public string Name;
        public AnimatorControllerParameterType? ParameterType;
        public bool IsAnimatorOnly;
        public bool IsHidden;
        public bool IsPhysBonePrefix;
        public bool IsRaycastPrefix;
        public bool IsParameterPrefix => IsPhysBonePrefix || IsRaycastPrefix;
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
        public string Name { get; }
        public PhantomParameterDefinition SourceParameter { get; }
        public bool IsCompatible { get; }
        public string IncompatibilityReason { get; }
        public bool IsSelected { get; }

        public PhantomSharedParameterCandidate(
            string name,
            PhantomParameterDefinition sourceParameter,
            bool isCompatible,
            string incompatibilityReason,
            bool isSelected)
        {
            Name = name;
            SourceParameter = sourceParameter;
            IsCompatible = isCompatible;
            IncompatibilityReason = incompatibilityReason;
            IsSelected = isSelected;
        }
    }

    internal sealed class PhantomSourceParameterCollection
    {
        public List<PhantomParameterDefinition> Definitions =
            new List<PhantomParameterDefinition>();
        public List<PhantomParameterDefinition> RetainedDefinitions =
            new List<PhantomParameterDefinition>();
        public HashSet<string> RetainedSourceParameterNames =
            new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>Immutable parameter result for one slot.</summary>
    internal sealed class PhantomSlotParameterPlan
    {
        public PhantomSlot Slot { get; }
        public PhantomSlotIdentity Identity { get; }
        public ImmutableList<PhantomParameterDefinition> SourceParameters { get; }
        public ImmutableHashSet<string> RetainedSourceParameterNames { get; }
        public ImmutableList<PhantomSharedParameterCandidate> Candidates { get; }
        public ImmutableHashSet<string> NamesSharedWithBase { get; }
        public int SourceParameterCost { get; }
        public int SharedParameterSavings { get; }
        public int GeneratedParameterCost { get; }
        public int FinalContributionCost { get; }
        public ImmutableDictionary<string, string> FinalParameterNames { get; }
        public ImmutableList<PhantomParameterRename> AutomaticRenames { get; }

        internal PhantomSlotParameterPlan(
            PhantomParameterSlotInput input,
            PhantomSlotParameterResolution resolution,
            IEnumerable<PhantomSharedParameterCandidate> candidates)
            : this(
                input?.Slot,
                input?.Identity,
                input?.SourceParameters,
                input?.RetainedSourceParameterNames,
                candidates,
                resolution?.SharedOriginalNames,
                resolution?.SourceParameterCost ?? 0,
                resolution?.SharedParameterSavings ?? 0,
                resolution?.GeneratedParameterCost ?? 0,
                resolution?.FinalContributionCost ?? 0,
                resolution?.FinalNames,
                resolution?.AutomaticRenames)
        {
        }

        internal PhantomSlotParameterPlan(
            PhantomSlot slot,
            PhantomSlotIdentity identity,
            IEnumerable<PhantomParameterDefinition> sourceParameters,
            IEnumerable<string> retainedSourceParameterNames,
            IEnumerable<PhantomSharedParameterCandidate> candidates,
            IEnumerable<string> namesSharedWithBase,
            int sourceParameterCost,
            int sharedParameterSavings,
            int generatedParameterCost,
            int finalContributionCost,
            IEnumerable<KeyValuePair<string, string>> finalParameterNames,
            IEnumerable<PhantomParameterRename> automaticRenames)
        {
            Slot = slot;
            Identity = identity;
            SourceParameters = (sourceParameters ?? Enumerable.Empty<PhantomParameterDefinition>())
                .ToImmutableList();
            RetainedSourceParameterNames = (retainedSourceParameterNames ?? Enumerable.Empty<string>())
                .ToImmutableHashSet(StringComparer.Ordinal);
            Candidates = (candidates ?? Enumerable.Empty<PhantomSharedParameterCandidate>())
                .ToImmutableList();
            NamesSharedWithBase = (namesSharedWithBase ?? Enumerable.Empty<string>())
                .ToImmutableHashSet(StringComparer.Ordinal);
            SourceParameterCost = sourceParameterCost;
            SharedParameterSavings = sharedParameterSavings;
            GeneratedParameterCost = generatedParameterCost;
            FinalContributionCost = finalContributionCost;
            FinalParameterNames = (finalParameterNames
                                   ?? Enumerable.Empty<KeyValuePair<string, string>>())
                .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            AutomaticRenames = (automaticRenames ?? Enumerable.Empty<PhantomParameterRename>())
                .ToImmutableList();
        }

        public bool TryGetFinalName(string originalName, out string finalName)
        {
            if (originalName != null
                && FinalParameterNames.TryGetValue(originalName, out var resolvedName))
            {
                finalName = resolvedName;
                return true;
            }

            finalName = originalName;
            return false;
        }

    }

    /// <summary>Immutable, context-specific parameter plan consumed by preview and build stages.</summary>
    internal sealed class PhantomParameterPlan
    {
        public static readonly PhantomParameterPlan Empty = new PhantomParameterPlan(
            ImmutableDictionary<string, PhantomParameterDefinition>.Empty,
            ImmutableList<PhantomSlotParameterPlan>.Empty,
            ImmutableList<string>.Empty);

        public ImmutableDictionary<string, PhantomParameterDefinition> BaseParameters { get; }
        public ImmutableList<PhantomSlotParameterPlan> Slots { get; }
        public ImmutableList<string> Errors { get; }
        public int TotalContributionCost => Slots.Sum(slot => slot.FinalContributionCost);

        internal PhantomParameterPlan(
            IEnumerable<KeyValuePair<string, PhantomParameterDefinition>> baseParameters,
            IEnumerable<PhantomSlotParameterPlan> slots,
            IEnumerable<string> errors)
        {
            BaseParameters = (baseParameters
                              ?? Enumerable.Empty<KeyValuePair<string, PhantomParameterDefinition>>())
                .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            Slots = (slots ?? Enumerable.Empty<PhantomSlotParameterPlan>()).ToImmutableList();
            Errors = (errors ?? Enumerable.Empty<string>()).ToImmutableList();
        }
    }
}
