using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    internal sealed class PhantomParameterSlotInput
    {
        public PhantomSlot Slot;
        public PhantomSlotIdentity Identity;
        public IReadOnlyList<PhantomParameterDefinition> SourceParameters =
            Array.Empty<PhantomParameterDefinition>();
    }

    internal sealed class PhantomParameterRename
    {
        public string OriginalName;
        public string FinalName;
        public string Reason;
    }

    internal sealed class PhantomSlotParameterResolution
    {
        public Dictionary<string, string> FinalNames { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public HashSet<string> SharedOriginalNames { get; } =
            new HashSet<string>(StringComparer.Ordinal);
        public List<PhantomParameterRename> AutomaticRenames { get; } =
            new List<PhantomParameterRename>();
        public int GeneratedParameterCost;
        public int SourceParameterCost;
        public int SharedParameterSavings;
        public int FinalContributionCost;

        public string FinalName(string originalName, PhantomSlot slot)
        {
            if (originalName != null && FinalNames.TryGetValue(originalName, out var finalName))
            {
                return finalName;
            }

            return PhantomParameterPolicy.FinalOriginalParameterName(slot, originalName);
        }
    }

    internal sealed class PhantomParameterResolution
    {
        public List<PhantomSlotParameterResolution> Slots { get; } =
            new List<PhantomSlotParameterResolution>();
        public List<string> Errors { get; } = new List<string>();
        public int TotalContributionCost => Slots.Sum(slot => slot.FinalContributionCost);
    }

    internal static class PhantomParameterResolver
    {
        private static readonly (string Suffix, AnimatorControllerParameterType Type)[] PhysBoneSubParameters =
        {
            ("_IsGrabbed", AnimatorControllerParameterType.Bool),
            ("_IsPosed", AnimatorControllerParameterType.Bool),
            ("_Angle", AnimatorControllerParameterType.Float),
            ("_Stretch", AnimatorControllerParameterType.Float),
            ("_Squish", AnimatorControllerParameterType.Float)
        };

        public static PhantomParameterResolution Resolve(
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters,
            IReadOnlyList<PhantomParameterSlotInput> inputs)
        {
            var result = new PhantomParameterResolution();
            var occupied = new Dictionary<string, PhantomParameterDefinition>(StringComparer.Ordinal);
            if (baseParameters != null)
            {
                foreach (var pair in baseParameters)
                {
                    occupied[pair.Key] = pair.Value;
                }
            }

            for (var index = 0; index < inputs.Count; index++)
            {
                result.Slots.Add(new PhantomSlotParameterResolution());
            }

            ReserveCoreParameters(inputs, result, occupied);

            for (var slotIndex = 0; slotIndex < inputs.Count; slotIndex++)
            {
                var input = inputs[slotIndex];
                var slotResult = result.Slots[slotIndex];
                if (input?.Slot == null || input.Slot.removeSourceControls)
                {
                    slotResult.FinalContributionCost = slotResult.GeneratedParameterCost;
                    continue;
                }

                foreach (var source in (input.SourceParameters ?? Array.Empty<PhantomParameterDefinition>())
                             .Where(parameter => parameter != null
                                                 && !string.IsNullOrWhiteSpace(parameter.Name))
                             .OrderByDescending(parameter => parameter.IsPhysBonePrefix)
                             .ThenBy(parameter => parameter.Name, StringComparer.Ordinal))
                {
                    var originalName = source.Name;
                    if (slotResult.FinalNames.ContainsKey(originalName))
                    {
                        continue;
                    }
                    if (PhantomParameterPolicy.IsVrcReserved(originalName))
                    {
                        slotResult.FinalNames[originalName] = originalName;
                        slotResult.SharedOriginalNames.Add(originalName);
                        continue;
                    }

                    if (source.IsPhysBonePrefix)
                    {
                        ResolvePhysBonePrefix(input, source, slotResult, occupied);
                        continue;
                    }

                    slotResult.SourceParameterCost += source.BitUsage;
                    var preferredName = PreferredName(input, source, occupied);
                    if (!occupied.TryGetValue(preferredName, out var existing))
                    {
                        occupied.Add(preferredName, source);
                        slotResult.FinalNames[originalName] = preferredName;
                        slotResult.FinalContributionCost += source.BitUsage;
                        continue;
                    }

                    if (PhantomParameterCompatibility.AreCompatible(existing, source, out _))
                    {
                        slotResult.FinalNames[originalName] = preferredName;
                        slotResult.SharedOriginalNames.Add(originalName);
                        slotResult.SharedParameterSavings += source.BitUsage;
                        continue;
                    }

                    PhantomParameterCompatibility.AreCompatible(existing, source, out var reason);
                    var fallbackBase = input.Identity.OriginalParameterName(originalName);
                    var finalName = AllocateUniqueName(fallbackBase, source, occupied);
                    slotResult.FinalNames[originalName] = finalName;
                    if (occupied.TryGetValue(finalName, out var fallbackExisting)
                        && PhantomParameterCompatibility.AreCompatible(fallbackExisting, source, out _))
                    {
                        slotResult.SharedOriginalNames.Add(originalName);
                        slotResult.SharedParameterSavings += source.BitUsage;
                    }
                    else
                    {
                        occupied[finalName] = source;
                        slotResult.FinalContributionCost += source.BitUsage;
                    }
                    slotResult.AutomaticRenames.Add(new PhantomParameterRename
                    {
                        OriginalName = originalName,
                        FinalName = finalName,
                        Reason = reason
                    });
                }

                slotResult.FinalContributionCost += slotResult.GeneratedParameterCost;
            }

            return result;
        }

        private static void ResolvePhysBonePrefix(
            PhantomParameterSlotInput input,
            PhantomParameterDefinition source,
            PhantomSlotParameterResolution slotResult,
            IDictionary<string, PhantomParameterDefinition> occupied)
        {
            var preferred = input.Slot.renamePhantomParameters
                ? input.Identity.OriginalParameterName(source.Name)
                : source.Name;
            var finalPrefix = preferred;
            if (!IsPhysBonePrefixAvailable(finalPrefix, occupied))
            {
                var fallback = input.Identity.OriginalParameterName(source.Name);
                finalPrefix = fallback;
                for (var suffix = 2; !IsPhysBonePrefixAvailable(finalPrefix, occupied); suffix++)
                {
                    finalPrefix = $"{fallback}~{suffix}";
                }
            }

            slotResult.FinalNames[source.Name] = finalPrefix;
            foreach (var subParameter in PhysBoneSubParameters)
            {
                var originalName = source.Name + subParameter.Suffix;
                var finalName = finalPrefix + subParameter.Suffix;
                slotResult.FinalNames[originalName] = finalName;
                slotResult.SharedOriginalNames.Add(originalName);
                occupied[finalName] = new PhantomParameterDefinition
                {
                    Name = finalName,
                    ParameterType = subParameter.Type,
                    IsAnimatorOnly = true,
                    WantSynced = false
                };
            }

            if (!string.Equals(preferred, finalPrefix, StringComparison.Ordinal))
            {
                slotResult.AutomaticRenames.Add(new PhantomParameterRename
                {
                    OriginalName = source.Name,
                    FinalName = finalPrefix,
                    Reason = "one or more derived PhysBone parameters already exist"
                });
            }
        }

        private static bool IsPhysBonePrefixAvailable(
            string prefix,
            IDictionary<string, PhantomParameterDefinition> occupied)
        {
            return PhysBoneSubParameters.All(subParameter =>
                !occupied.ContainsKey(prefix + subParameter.Suffix));
        }

        private static void ReserveCoreParameters(
            IReadOnlyList<PhantomParameterSlotInput> inputs,
            PhantomParameterResolution result,
            IDictionary<string, PhantomParameterDefinition> occupied)
        {
            var prefixOwners = new Dictionary<string, int>(StringComparer.Ordinal);
            var duplicateSlots = new HashSet<int>();
            for (var slotIndex = 0; slotIndex < inputs.Count; slotIndex++)
            {
                var input = inputs[slotIndex];
                if (input?.Slot == null)
                {
                    continue;
                }

                if (prefixOwners.TryGetValue(input.Identity.ParameterPrefix, out var previousSlot))
                {
                    duplicateSlots.Add(slotIndex);
                    result.Errors.Add(
                        $"Slots '{inputs[previousSlot].Identity.SlotId}' and '{input.Identity.SlotId}' "
                        + $"use the same core parameter prefix '{input.Identity.ParameterPrefix}'.");
                }
                else
                {
                    prefixOwners.Add(input.Identity.ParameterPrefix, slotIndex);
                }
            }

            for (var slotIndex = 0; slotIndex < inputs.Count; slotIndex++)
            {
                var input = inputs[slotIndex];
                if (input?.Slot == null || duplicateSlots.Contains(slotIndex))
                {
                    continue;
                }

                foreach (var core in EnumerateCoreParameters(input.Slot))
                {
                    if (occupied.TryGetValue(core.Name, out var existing))
                    {
                        if (!PhantomParameterCompatibility.AreCompatible(existing, core, out var reason))
                        {
                            result.Errors.Add(
                                $"Slot '{input.Identity.SlotId}' core parameter '{core.Name}' conflicts with "
                                + $"the base avatar ({reason}).");
                        }
                        continue;
                    }

                    occupied.Add(core.Name, core);
                    result.Slots[slotIndex].GeneratedParameterCost += core.BitUsage;
                }
            }
        }

        private static string PreferredName(
            PhantomParameterSlotInput input,
            PhantomParameterDefinition source,
            IReadOnlyDictionary<string, PhantomParameterDefinition> occupied)
        {
            if (!input.Slot.renamePhantomParameters)
            {
                return source.Name;
            }

            if (PhantomParameterPolicy.IsConfiguredShared(input.Slot, source.Name)
                && occupied.TryGetValue(source.Name, out var existing)
                && PhantomParameterCompatibility.AreCompatible(existing, source, out _))
            {
                return source.Name;
            }

            return input.Identity.OriginalParameterName(source.Name);
        }

        private static string AllocateUniqueName(
            string fallbackBase,
            PhantomParameterDefinition source,
            IReadOnlyDictionary<string, PhantomParameterDefinition> occupied)
        {
            if (!occupied.TryGetValue(fallbackBase, out var existing)
                || PhantomParameterCompatibility.AreCompatible(existing, source, out _))
            {
                return fallbackBase;
            }

            for (var suffix = 2; ; suffix++)
            {
                var candidate = $"{fallbackBase}~{suffix}";
                if (!occupied.TryGetValue(candidate, out existing)
                    || PhantomParameterCompatibility.AreCompatible(existing, source, out _))
                {
                    return candidate;
                }
            }
        }

        private static IEnumerable<PhantomParameterDefinition> EnumerateCoreParameters(PhantomSlot slot)
        {
            yield return Definition(PhantomParameterNames.Activate(slot), AnimatorControllerParameterType.Bool, true, false, 0f);
            yield return Definition(PhantomParameterNames.Freeze(slot), AnimatorControllerParameterType.Bool, true, false, 0f);
            yield return Definition(PhantomParameterNames.PositionLock(slot), AnimatorControllerParameterType.Bool, true, false, 1f);

            if (slot.enableScaleControl)
            {
                yield return Definition(PhantomParameterNames.Scale(slot), AnimatorControllerParameterType.Float, true, false, ScaleControlAnimatorModule.DefaultScaleParameter);
                yield return Definition(PhantomParameterNames.Mirror(slot), AnimatorControllerParameterType.Bool, true, false, 0f);
                yield return Definition(PhantomParameterNames.ScaleReset(slot), AnimatorControllerParameterType.Bool, false, false, 0f);
            }

            if (slot.enablePhantomGrabbing)
            {
                yield return Definition(PhantomParameterNames.PhantomGrabbingShowBones(slot), AnimatorControllerParameterType.Bool, true, false, 0f);
                yield return Definition(PhantomParameterNames.PhantomGrabbingContactLeft(slot), AnimatorControllerParameterType.Bool, false, true, 0f);
                yield return Definition(PhantomParameterNames.PhantomGrabbingContactRight(slot), AnimatorControllerParameterType.Bool, false, true, 0f);
            }

            if (slot.enablePhantomView)
            {
                yield return Definition(PhantomParameterNames.PhantomViewEnabled(slot), AnimatorControllerParameterType.Bool, false, false, 0f);
                yield return Definition(PhantomParameterNames.PhantomViewStereoStrength(slot), AnimatorControllerParameterType.Float, false, false, PhantomViewAnimatorModule.DefaultStereoStrengthParameter);
                yield return Definition(PhantomParameterNames.PhantomViewMaskSize(slot), AnimatorControllerParameterType.Float, false, false, PhantomViewAnimatorModule.DefaultMaskSizeParameter);
                yield return Definition(PhantomParameterNames.PhantomViewDirectWeight(slot), AnimatorControllerParameterType.Float, false, true, 1f);
            }

            if (slot.tryConvertAnimatorTrackingControl && !slot.removeSourceControls)
            {
                foreach (var name in PhantomTrackingControlGroups.Parameters(slot))
                {
                    yield return Definition(name, AnimatorControllerParameterType.Float, false, true, 1f);
                }
                yield return Definition(PhantomParameterNames.TrackingDirectWeight(slot), AnimatorControllerParameterType.Float, false, true, 1f);
            }
        }

        private static PhantomParameterDefinition Definition(
            string name,
            AnimatorControllerParameterType type,
            bool synced,
            bool animatorOnly,
            float defaultValue)
        {
            return new PhantomParameterDefinition
            {
                Name = name,
                ParameterType = type,
                WantSynced = synced,
                IsAnimatorOnly = animatorOnly,
                IsHidden = false,
                DefaultValue = defaultValue,
                Saved = animatorOnly ? (bool?)null : false
            };
        }
    }

    internal static class PhantomParameterCompatibility
    {
        public static bool AreCompatible(
            PhantomParameterDefinition left,
            PhantomParameterDefinition right,
            out string reason)
        {
            if (left == null || right == null)
            {
                reason = "parameter information is missing";
                return false;
            }
            if (left.ParameterType == null || right.ParameterType == null)
            {
                reason = "the parameter type is unknown";
                return false;
            }
            if (left.ParameterType != right.ParameterType)
            {
                reason = $"type mismatch: {left.ParameterType} vs {right.ParameterType}";
                return false;
            }
            if (left.IsAnimatorOnly != right.IsAnimatorOnly)
            {
                reason = "animator-only state differs";
                return false;
            }
            if (left.WantSynced != right.WantSynced)
            {
                reason = "network sync state differs";
                return false;
            }
            if (left.IsHidden != right.IsHidden)
            {
                reason = "hidden state differs";
                return false;
            }
            if (left.DefaultValue.HasValue && right.DefaultValue.HasValue
                && !Mathf.Approximately(left.DefaultValue.Value, right.DefaultValue.Value))
            {
                reason = $"default value differs: {left.DefaultValue.Value} vs {right.DefaultValue.Value}";
                return false;
            }
            if (left.Saved.HasValue && right.Saved.HasValue && left.Saved.Value != right.Saved.Value)
            {
                reason = "saved state differs";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
