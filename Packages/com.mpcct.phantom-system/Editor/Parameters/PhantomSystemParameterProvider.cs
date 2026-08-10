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
                        analysis.BaseParameters,
                        PhantomParameterNames.ScaleReset(slot),
                        AnimatorControllerParameterType.Bool,
                        0f);
                }

                if (slot.enablePhantomGrabbing)
                {
                    AddSyncedParameter(
                        output,
                        component,
                        analysis.BaseParameters,
                        PhantomParameterNames.PhantomGrabbingShowBones(slot),
                        AnimatorControllerParameterType.Bool,
                        0f);
                    AddAnimatorOnlyParameter(
                        output,
                        component,
                        analysis.BaseParameters,
                        PhantomParameterNames.PhantomGrabbingContactLeft(slot));
                    AddAnimatorOnlyParameter(
                        output,
                        component,
                        analysis.BaseParameters,
                        PhantomParameterNames.PhantomGrabbingContactRight(slot));
                }

                if (slot.enablePhantomView)
                {
                    AddLocalParameter(
                        output,
                        component,
                        analysis.BaseParameters,
                        PhantomParameterNames.PhantomViewEnabled(slot),
                        AnimatorControllerParameterType.Bool,
                        0f);
                    AddLocalParameter(
                        output,
                        component,
                        analysis.BaseParameters,
                        PhantomParameterNames.PhantomViewStereoStrength(slot),
                        AnimatorControllerParameterType.Float,
                        PhantomViewAnimatorModule.DefaultStereoStrengthParameter);
                    AddLocalParameter(
                        output,
                        component,
                        analysis.BaseParameters,
                        PhantomParameterNames.PhantomViewMaskSize(slot),
                        AnimatorControllerParameterType.Float,
                        PhantomViewAnimatorModule.DefaultMaskSizeParameter);
                    AddAnimatorOnlyParameter(
                        output,
                        component,
                        analysis.BaseParameters,
                        PhantomParameterNames.PhantomViewDirectWeight(slot),
                        AnimatorControllerParameterType.Float,
                        1f);
                }

                if (slot.tryConvertAnimatorTrackingControl && !slot.removeSourceControls)
                {
                    AddTrackingParameters(output, component, analysis.BaseParameters, slot);
                }

                if (slot.removeSourceControls)
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
                    if (sourceParameter.IsParameterPrefix)
                    {
                        var prefixName = slotAnalysis.FinalParameterNames.TryGetValue(
                            sourceParameter.Name,
                            out var resolvedPrefix)
                            ? resolvedPrefix
                            : PhantomParameterPolicy.FinalOriginalParameterName(
                                slot,
                                sourceParameter.Name,
                                slotAnalysis.NamesSharedWithBase);
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
                    if (shared)
                    {
                        // The base avatar already provides this exact compatible parameter. Omitting it here keeps
                        // NDMF's per-plugin usage attribution independent of Unity component ordering.
                        continue;
                    }

                    var finalName = slotAnalysis.FinalParameterNames.TryGetValue(
                        sourceParameter.Name,
                        out var resolvedName)
                        ? resolvedName
                        : PhantomParameterPolicy.FinalOriginalParameterName(
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
            if (BaseProvidesCompatibleParameter(
                    baseParameters,
                    name,
                    parameterType,
                    false,
                    true,
                    defaultValue))
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
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters,
            string name)
        {
            AddAnimatorOnlyParameter(
                output,
                source,
                baseParameters,
                name,
                AnimatorControllerParameterType.Bool,
                0f);
        }

        private static void AddTrackingParameters(
            List<ProvidedParameter> output,
            PhantomAuthoring source,
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters,
            PhantomSlot slot)
        {
            foreach (var parameter in PhantomTrackingControlGroups.Parameters(slot))
            {
                AddAnimatorOnlyParameter(
                    output,
                    source,
                    baseParameters,
                    parameter,
                    AnimatorControllerParameterType.Float,
                    1f);
            }

            AddAnimatorOnlyParameter(
                output,
                source,
                baseParameters,
                PhantomParameterNames.TrackingDirectWeight(slot),
                AnimatorControllerParameterType.Float,
                1f);
        }

        private static void AddAnimatorOnlyParameter(
            List<ProvidedParameter> output,
            PhantomAuthoring source,
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters,
            string name,
            AnimatorControllerParameterType parameterType,
            float defaultValue)
        {
            if (BaseProvidesCompatibleParameter(
                    baseParameters,
                    name,
                    parameterType,
                    true,
                    false,
                    defaultValue))
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
                IsAnimatorOnly = true,
                WantSynced = false,
                DefaultValue = defaultValue
            });
        }

        private static void AddLocalParameter(
            List<ProvidedParameter> output,
            PhantomAuthoring source,
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters,
            string name,
            AnimatorControllerParameterType parameterType,
            float defaultValue)
        {
            if (BaseProvidesCompatibleParameter(
                    baseParameters,
                    name,
                    parameterType,
                    false,
                    false,
                    defaultValue))
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
                IsAnimatorOnly = false,
                WantSynced = false,
                DefaultValue = defaultValue
            });
        }

        private static bool BaseProvidesCompatibleParameter(
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters,
            string name,
            AnimatorControllerParameterType parameterType,
            bool animatorOnly,
            bool synced,
            float defaultValue)
        {
            if (baseParameters == null || !baseParameters.TryGetValue(name, out var existing))
            {
                return false;
            }

            var expected = new PhantomParameterDefinition
            {
                Name = name,
                ParameterType = parameterType,
                IsAnimatorOnly = animatorOnly,
                IsHidden = false,
                WantSynced = synced,
                DefaultValue = defaultValue,
                Saved = animatorOnly ? (bool?)null : false
            };
            return PhantomParameterCompatibility.AreCompatible(existing, expected, out _);
        }
    }
}
