using System;
using System.Linq;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Resolves only names which were identified before PhantomSystem mutates the build hierarchy.</summary>
    internal static class PhantomSourceParameterMapping
    {
        private const int MaximumDisplayedReferences = 8;

        public static bool TryResolve(
            PhantomSlotBuildState slot,
            string originalName,
            string usage,
            out string finalName)
        {
            finalName = originalName;
            if (slot?.Slot == null || string.IsNullOrWhiteSpace(originalName))
            {
                return false;
            }

            if (slot.ParameterPlan != null
                && slot.ParameterPlan.TryGetFinalName(originalName, out finalName))
            {
                return true;
            }

            if (PhantomParameterPolicy.IsVrcReserved(originalName)
                || IsInEffectiveSlotNamespace(slot, originalName))
            {
                finalName = originalName;
                return true;
            }

            RegisterUnresolved(slot, originalName, usage);
            return false;
        }

        public static void ReportUnresolved(
            PhantomSlotBuildState slot,
            PhantomBuildReport report)
        {
            if (slot == null
                || report == null
                || slot.UnresolvedSourceParametersReported
                || slot.UnresolvedSourceParameterReferences.Count == 0)
            {
                return;
            }

            slot.UnresolvedSourceParametersReported = true;
            var references = slot.UnresolvedSourceParameterReferences
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SelectMany(pair => pair.Value
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .Select(usage => $"{pair.Key} ({usage})"))
                .ToArray();
            var examples = string.Join(", ", references.Take(MaximumDisplayedReferences));
            if (references.Length > MaximumDisplayedReferences)
            {
                examples += $", +{references.Length - MaximumDisplayedReferences} more";
            }

            report.Warning(
                $"Slot '{slot.SlotId}' kept {slot.UnresolvedSourceParameterReferences.Count} "
                + $"unresolved source parameter name(s) unchanged across {references.Length} "
                + $"reference type(s): {examples}. "
                + "These references were not present in the pre-generation parameter resolution.",
                slot.CloneRoot);
        }

        internal static bool IsInEffectiveSlotNamespace(
            PhantomSlotBuildState slot,
            string name)
        {
            if (slot?.Slot == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var prefix = slot.Identity?.ParameterPrefix
                         ?? PhantomSlotIdentity.Create(slot.Slot).ParameterPrefix;
            return string.Equals(name, prefix, StringComparison.Ordinal)
                   || name.StartsWith(prefix + "/", StringComparison.Ordinal);
        }

        private static void RegisterUnresolved(
            PhantomSlotBuildState slot,
            string name,
            string usage)
        {
            if (!slot.UnresolvedSourceParameterReferences.TryGetValue(name, out var usages))
            {
                usages = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                slot.UnresolvedSourceParameterReferences[name] = usages;
            }

            usages.Add(string.IsNullOrWhiteSpace(usage) ? "unknown usage" : usage);
        }
    }
}
