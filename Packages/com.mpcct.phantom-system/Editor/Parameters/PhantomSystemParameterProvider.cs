using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using nadena.dev.ndmf;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    [ParameterProviderFor(typeof(PhantomAuthoring))]
    internal sealed class PhantomSystemParameterProvider : IParameterProvider
    {
        private readonly PhantomAuthoring component;

        public PhantomSystemParameterProvider(PhantomAuthoring component)
        {
            this.component = component;
        }

        public IEnumerable<ProvidedParameter> GetSuppliedParameters(BuildContext context = null)
        {
            if (component == null || PhantomSourceParameterCollector.IsProviderSuppressed)
            {
                return Array.Empty<ProvidedParameter>();
            }

            var output = new List<ProvidedParameter>();
            var plan = PhantomParameterPlanner.Analyze(component);
            foreach (var slotPlan in plan.Slots)
            {
                var slot = slotPlan.Slot;
                if (slot == null)
                {
                    continue;
                }

                foreach (var entry in PhantomCoreParameterCatalog.ForSlot(slot))
                {
                    AddCoreParameter(output, component, plan, entry.Parameter);
                }

                foreach (var sourceParameter in slotPlan.SourceParameters)
                {
                    if (sourceParameter == null
                        || string.IsNullOrWhiteSpace(sourceParameter.Name)
                        || PhantomParameterPolicy.IsVrcReserved(sourceParameter.Name))
                    {
                        continue;
                    }

                    if (sourceParameter.IsParameterPrefix)
                    {
                        var prefixName = slotPlan.FinalParameterNames.TryGetValue(
                            sourceParameter.Name,
                            out var resolvedPrefix)
                            ? resolvedPrefix
                            : sourceParameter.Name;
                        output.Add(new ProvidedParameter(
                            prefixName,
                            ParameterNamespace.PhysBonesPrefix,
                            component,
                            PhantomSystemPlugin.Instance,
                            null)
                        {
                            IsHidden = sourceParameter.IsHidden,
                            WantSynced = false
                        });
                        continue;
                    }

                    if (slotPlan.NamesSharedWithBase.Contains(sourceParameter.Name))
                    {
                        // The base avatar already provides this exact compatible parameter. Omitting it keeps
                        // NDMF's per-plugin usage attribution independent of Unity component ordering.
                        continue;
                    }

                    var finalName = slotPlan.FinalParameterNames.TryGetValue(
                        sourceParameter.Name,
                        out var resolvedName)
                        ? resolvedName
                        : sourceParameter.Name;
                    output.Add(new ProvidedParameter(
                        finalName,
                        ParameterNamespace.Animator,
                        component,
                        PhantomSystemPlugin.Instance,
                        sourceParameter.ParameterType)
                    {
                        ExpandTypeOnConflict = true,
                        IsAnimatorOnly = sourceParameter.IsAnimatorOnly,
                        IsHidden = sourceParameter.IsHidden,
                        WantSynced = sourceParameter.WantSynced,
                        DefaultValue = sourceParameter.DefaultValue
                    });
                }
            }

            return output;
        }

        public void RemapParameters(
            ref ImmutableDictionary<(ParameterNamespace, string), ParameterMapping> nameMap,
            BuildContext context = null)
        {
            // This provider exposes final external names. Build-time source remaps are emitted from the same plan
            // as ModularAvatarParameters components by PhantomParameterConfigBuilder.
        }

        private static void AddCoreParameter(
            ICollection<ProvidedParameter> output,
            PhantomAuthoring source,
            PhantomParameterPlan plan,
            PhantomParameterDefinition definition)
        {
            if (definition == null || definition.ParameterType == null)
            {
                return;
            }

            if (plan.BaseParameters.TryGetValue(definition.Name, out var existing)
                && PhantomParameterCompatibility.AreCompatible(existing, definition, out _))
            {
                return;
            }

            output.Add(new ProvidedParameter(
                definition.Name,
                ParameterNamespace.Animator,
                source,
                PhantomSystemPlugin.Instance,
                definition.ParameterType)
            {
                ExpandTypeOnConflict = true,
                IsAnimatorOnly = definition.IsAnimatorOnly,
                IsHidden = definition.IsHidden,
                WantSynced = definition.WantSynced,
                DefaultValue = definition.DefaultValue
            });
        }
    }
}
