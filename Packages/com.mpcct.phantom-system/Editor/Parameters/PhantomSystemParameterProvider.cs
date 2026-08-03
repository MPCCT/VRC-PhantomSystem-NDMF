using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using nadena.dev.ndmf;
using UnityEngine;
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
            if (component == null || PhantomParameterAnalysis.IsProviderSuppressed)
            {
                return Array.Empty<ProvidedParameter>();
            }

            var output = new List<ProvidedParameter>();
            var analysis = PhantomParameterAnalysis.Analyze(component);
            var slots = component.slots ?? new List<PhantomSlot>();

            for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                var slot = slots[slotIndex];
                if (slot == null)
                {
                    continue;
                }

                AddCoreParameter(
                    output,
                    component,
                    analysis.BaseParameters,
                    PhantomParameterNames.Activate(slot),
                    false);
                AddCoreParameter(
                    output,
                    component,
                    analysis.BaseParameters,
                    PhantomParameterNames.Freeze(slot),
                    false);
                AddCoreParameter(
                    output,
                    component,
                    analysis.BaseParameters,
                    PhantomParameterNames.PositionLock(slot),
                    true);

                if (slot.enableScaleControl)
                {
                    AddSyncedParameter(
                        output,
                        component,
                        analysis.BaseParameters,
                        PhantomParameterNames.Scale(slot),
                        AnimatorControllerParameterType.Float,
                        ScaleControlAnimatorModule.DefaultScaleParameter);
                    AddSyncedParameter(
                        output,
                        component,
                        analysis.BaseParameters,
                        PhantomParameterNames.Mirror(slot),
                        AnimatorControllerParameterType.Bool,
                        0f);
                    AddLocalParameter(
                        output,
                        component,
                        PhantomParameterNames.ScaleReset(slot),
                        AnimatorControllerParameterType.Bool,
                        0f);
                    AddAnimatorOnlyParameter(
                        output,
                        component,
                        PhantomParameterNames.ScaleControlWeight(slot),
                        AnimatorControllerParameterType.Float,
                        1f);
                }

                if (slot.enablePhantomGrabbing)
                {
                    AddLocalParameter(
                        output,
                        component,
                        PhantomParameterNames.PhantomGrabbingShowBones(slot),
                        AnimatorControllerParameterType.Bool,
                        0f);
                    AddAnimatorOnlyParameter(
                        output,
                        component,
                        PhantomParameterNames.PhantomGrabbingContactLeft(slot));
                    AddAnimatorOnlyParameter(
                        output,
                        component,
                        PhantomParameterNames.PhantomGrabbingContactRight(slot));
                }

                if (slot.removeOriginalFx)
                {
                    continue;
                }

                var slotAnalysis = slotIndex < analysis.Slots.Count ? analysis.Slots[slotIndex] : null;
                if (slotAnalysis == null)
                {
                    continue;
                }

                foreach (var sourceParameter in slotAnalysis.SourceParameters)
                {
                    if (sourceParameter == null
                        || string.IsNullOrWhiteSpace(sourceParameter.Name)
                        || PhantomParameterPolicy.IsVrcReserved(sourceParameter.Name))
                    {
                        continue;
                    }

                    var shared = slotAnalysis.NamesSharedWithBase.Contains(sourceParameter.Name);
                    if (shared)
                    {
                        // The base avatar already provides this exact compatible parameter. Omitting it here keeps
                        // NDMF's per-plugin usage attribution independent of Unity component ordering.
                        continue;
                    }

                    var finalName = PhantomParameterPolicy.FinalOriginalParameterName(
                        slot,
                        sourceParameter.Name,
                        slotAnalysis.NamesSharedWithBase);
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
            // This provider exposes the final external names directly. The actual build remaps are generated as
            // ModularAvatarParameters components by PhantomMenuAndParameterBuilder.
        }

        private static void AddCoreParameter(
            List<ProvidedParameter> output,
            PhantomAuthoring source,
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters,
            string name,
            bool defaultValue)
        {
            AddSyncedParameter(
                output,
                source,
                baseParameters,
                name,
                AnimatorControllerParameterType.Bool,
                defaultValue ? 1f : 0f);
        }

        private static void AddSyncedParameter(
            List<ProvidedParameter> output,
            PhantomAuthoring source,
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters,
            string name,
            AnimatorControllerParameterType parameterType,
            float defaultValue)
        {
            if (baseParameters != null
                && baseParameters.TryGetValue(name, out var existing)
                && !existing.IsAnimatorOnly
                && existing.ParameterType == parameterType
                && existing.WantSynced)
            {
                return;
            }

            output.Add(new ProvidedParameter(
                name,
                ParameterNamespace.Animator,
                source,
                PhantomSystemPlugin.Instance,
                parameterType)
            {
                ExpandTypeOnConflict = true,
                WantSynced = true,
                DefaultValue = defaultValue
            });
        }

        private static void AddAnimatorOnlyParameter(
            List<ProvidedParameter> output,
            PhantomAuthoring source,
            string name)
        {
            AddAnimatorOnlyParameter(
                output,
                source,
                name,
                AnimatorControllerParameterType.Bool,
                0f);
        }

        private static void AddAnimatorOnlyParameter(
            List<ProvidedParameter> output,
            PhantomAuthoring source,
            string name,
            AnimatorControllerParameterType parameterType,
            float defaultValue)
        {
            output.Add(new ProvidedParameter(
                name,
                ParameterNamespace.Animator,
                source,
                PhantomSystemPlugin.Instance,
                parameterType)
            {
                ExpandTypeOnConflict = true,
                IsAnimatorOnly = true,
                WantSynced = false,
                DefaultValue = defaultValue
            });
        }

        private static void AddLocalParameter(
            List<ProvidedParameter> output,
            PhantomAuthoring source,
            string name,
            AnimatorControllerParameterType parameterType,
            float defaultValue)
        {
            output.Add(new ProvidedParameter(
                name,
                ParameterNamespace.Animator,
                source,
                PhantomSystemPlugin.Instance,
                parameterType)
            {
                ExpandTypeOnConflict = true,
                IsAnimatorOnly = false,
                WantSynced = false,
                DefaultValue = defaultValue
            });
        }
    }
}
