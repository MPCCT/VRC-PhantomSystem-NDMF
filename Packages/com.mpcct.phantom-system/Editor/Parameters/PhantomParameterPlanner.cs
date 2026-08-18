using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.preview;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    internal static class PhantomParameterPlanner
    {
        public static PhantomParameterPlan Analyze(
            PhantomAuthoring authoring,
            ComputeContext previewContext = null)
        {
            if (authoring == null)
            {
                return PhantomParameterPlan.Empty;
            }

            var avatarRoot = FindAvatarRoot(authoring.transform);
            var baseParameters = avatarRoot != null
                ? PhantomSourceParameterCollector.ReadBaseParameters(
                    avatarRoot.gameObject,
                    null,
                    previewContext)
                : new Dictionary<string, PhantomParameterDefinition>(StringComparer.Ordinal);
            var inputs = new List<PhantomParameterSlotInput>();
            foreach (var slot in authoring.slots ?? new List<PhantomSlot>())
            {
                var collection = slot?.phantomAvatar != null
                    ? PhantomSourceParameterCollector.Collect(
                        slot.phantomAvatar.gameObject,
                        null,
                        previewContext)
                    : new PhantomSourceParameterCollection();
                inputs.Add(CreateInput(slot, PhantomSlotIdentity.Create(slot), collection));
            }

            return Create(baseParameters, inputs);
        }

        public static PhantomParameterPlan Create(
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters,
            IReadOnlyList<PhantomParameterSlotInput> inputs)
        {
            baseParameters ??= new Dictionary<string, PhantomParameterDefinition>(StringComparer.Ordinal);
            inputs ??= Array.Empty<PhantomParameterSlotInput>();
            var resolution = PhantomParameterResolver.Resolve(baseParameters, inputs);
            var slots = new List<PhantomSlotParameterPlan>();
            for (var index = 0; index < inputs.Count; index++)
            {
                var input = inputs[index];
                var slotResolution = index < resolution.Slots.Count
                    ? resolution.Slots[index]
                    : new PhantomSlotParameterResolution();
                slots.Add(new PhantomSlotParameterPlan(
                    input,
                    slotResolution,
                    BuildCandidates(input, baseParameters)));
            }

            return new PhantomParameterPlan(baseParameters, slots, resolution.Errors);
        }

        public static PhantomParameterSlotInput CreateInput(
            PhantomSlot slot,
            PhantomSlotIdentity identity,
            PhantomSourceParameterCollection collection)
        {
            collection ??= new PhantomSourceParameterCollection();
            return new PhantomParameterSlotInput
            {
                Slot = slot,
                Identity = identity ?? PhantomSlotIdentity.Create(slot),
                SourceParameters = slot != null && slot.removeSourceControls
                    ? collection.RetainedDefinitions
                    : collection.Definitions,
                RetainedSourceParameterNames = collection.RetainedSourceParameterNames
            };
        }

        private static IEnumerable<PhantomSharedParameterCandidate> BuildCandidates(
            PhantomParameterSlotInput input,
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters)
        {
            if (input?.Slot == null)
            {
                return Enumerable.Empty<PhantomSharedParameterCandidate>();
            }

            var candidates = new List<PhantomSharedParameterCandidate>();
            foreach (var source in (input.SourceParameters ?? Array.Empty<PhantomParameterDefinition>())
                         .Where(parameter => parameter != null
                                             && !parameter.IsAnimatorOnly
                                             && !parameter.IsHidden))
            {
                if (!baseParameters.TryGetValue(source.Name, out var baseParameter))
                {
                    continue;
                }

                var compatible = PhantomParameterCompatibility.AreCompatible(
                    baseParameter,
                    source,
                    out var reason);
                candidates.Add(new PhantomSharedParameterCandidate(
                    source.Name,
                    source,
                    compatible,
                    reason,
                    PhantomParameterPolicy.IsConfiguredShared(input.Slot, source.Name)));
            }

            return candidates;
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
    }
}
