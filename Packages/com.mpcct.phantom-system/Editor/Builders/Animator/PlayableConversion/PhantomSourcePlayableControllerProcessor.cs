using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Coordinates source playable preparation, motion conversion, and behaviour translation.</summary>
    internal static class PhantomSourcePlayableControllerProcessor
    {
        public static void ProcessVirtual(
            BuildContext context,
            PhantomSlotBuildState slot,
            PhantomSystemProjectSettingsSnapshot projectSettings,
            PhantomBuildReport report)
        {
            ProcessVirtual(context, slot, projectSettings, null, report);
        }

        public static void ProcessVirtual(
            BuildContext context,
            PhantomSlotBuildState slot,
            PhantomSystemProjectSettingsSnapshot projectSettings,
            PhantomHumanoidBakeCacheSession bakeCache,
            PhantomBuildReport report)
        {
            if (slot?.Slot == null || slot.Slot.removeSourceControls)
            {
                return;
            }

            ResetConversionState(slot);
            var prepared = PrepareControllers(context, slot, report);
            var targetLayers = BuildTargetLayerMap(prepared);
            var behaviourResult = new PhantomSourceBehaviourTranslationResult();
            foreach (var pair in prepared)
            {
                var ownerVirtualLayerIndices = pair.Value.Controller.Layers
                    .Select((layer, index) => new
                    {
                        layer.VirtualLayerIndex,
                        PhysicalIndex = layer.OriginalPhysicalLayerIndex ?? index
                    })
                    .ToDictionary(value => value.VirtualLayerIndex, value => value.PhysicalIndex);

                PhantomPlayableMotionConverter.Convert(
                    context,
                    slot,
                    projectSettings,
                    bakeCache,
                    report,
                    pair.Key,
                    pair.Value.Controller,
                    pair.Value.Registration.Source.Mask,
                    pair.Value.Registration.BaseController);
                behaviourResult.Merge(PhantomSourceBehaviourTranslator.Translate(
                    pair.Value.Controller,
                    slot,
                    pair.Key,
                    targetLayers,
                    ownerVirtualLayerIndices));
            }

            var missingBoneSummary = BuildMissingBoneSummary(slot);
            if (!string.IsNullOrEmpty(missingBoneSummary))
            {
                report.Warning(missingBoneSummary, slot.CloneRoot);
            }

            behaviourResult.ReportSummary(slot, report);
            slot.HasTrackingControlConversion = behaviourResult.DriverCount > 0;
        }

        internal static bool TryGetBaseController(
            RuntimeAnimatorController source,
            out AnimatorController controller)
        {
            var current = source;
            var visited = new HashSet<RuntimeAnimatorController>();
            while (current is AnimatorOverrideController overrideController)
            {
                if (!visited.Add(current))
                {
                    controller = null;
                    return false;
                }
                current = overrideController.runtimeAnimatorController;
            }

            controller = current as AnimatorController;
            return controller != null;
        }

        internal static string BuildMissingBoneSummary(PhantomSlotBuildState slot)
        {
            if (slot == null || slot.MissingHumanoidBoneClips.Count == 0)
            {
                return null;
            }

            var bones = slot.MissingHumanoidBoneClips
                .OrderBy(pair => (int)pair.Key)
                .ToArray();
            var affectedClips = bones
                .SelectMany(pair => pair.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            const int maximumDisplayedBones = 12;
            const int maximumDisplayedClips = 5;
            var boneSummary = string.Join(", ", bones
                .Take(maximumDisplayedBones)
                .Select(pair => $"{pair.Key} ({pair.Value.Count} clip(s))"));
            if (bones.Length > maximumDisplayedBones)
            {
                boneSummary += $", +{bones.Length - maximumDisplayedBones} more";
            }

            var clipExamples = string.Join(", ", affectedClips.Take(maximumDisplayedClips));
            if (affectedClips.Length > maximumDisplayedClips)
            {
                clipExamples += $", +{affectedClips.Length - maximumDisplayedClips} more";
            }

            return $"Slot '{slot.SlotId}' could not bake curves for {bones.Length} unavailable optional "
                   + $"humanoid bone(s) referenced by {affectedClips.Length} converted clip(s): {boneSummary}. "
                   + $"Affected clip examples: {clipExamples}. Curves for unavailable bones were skipped; "
                   + "remaining animation curves were preserved. Add the missing bones to the source Humanoid "
                   + "mapping only if those animations are required.";
        }

        private static void ResetConversionState(PhantomSlotBuildState slot)
        {
            slot.ConvertedActionLayers.Clear();
            slot.MissingHumanoidBoneClips.Clear();
            slot.ConvertedClipReferences.Clear();
            slot.WarnedUnsupportedAnimatorClips.Clear();
            slot.HasTrackingControlConversion = false;
        }

        private static Dictionary<VRCAvatarDescriptor.AnimLayerType, PreparedController>
            PrepareControllers(
                BuildContext context,
                PhantomSlotBuildState slot,
                PhantomBuildReport report)
        {
            var controllerContext = context.Extension<AnimatorServicesContext>().ControllerContext;
            var prepared = new Dictionary<VRCAvatarDescriptor.AnimLayerType, PreparedController>();
            foreach (var pair in slot.SourcePlayableRegistrations)
            {
                var registration = pair.Value;
                if (registration?.MergeAnimator == null)
                {
                    continue;
                }

                if (!controllerContext.Controllers.TryGetValue(
                        registration.MergeAnimator,
                        out var controller)
                    || controller == null)
                {
                    report.InternalError(
                        $"Slot '{slot.SlotId}' {pair.Key} Source Merge Animator was not registered "
                        + "in NDMF Animator Services.",
                        slot.CloneRoot);
                    continue;
                }

                PrefixLayers(controller, slot.HierarchyName, pair.Key);
                if (pair.Key == VRCAvatarDescriptor.AnimLayerType.Action)
                {
                    RecordConvertedActionLayers(slot, controller);
                }
                prepared[pair.Key] = new PreparedController(registration, controller);
            }

            return prepared;
        }

        private static Dictionary<VRCAvatarDescriptor.AnimLayerType, IReadOnlyList<string>>
            BuildTargetLayerMap(
                IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, PreparedController> prepared)
        {
            return prepared.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.Controller.Layers
                    .Select(layer => layer.Name)
                    .ToArray());
        }

        private static void PrefixLayers(
            VirtualAnimatorController controller,
            string slotId,
            VRCAvatarDescriptor.AnimLayerType playable)
        {
            var layers = controller.Layers.ToArray();
            for (var index = 0; index < layers.Length; index++)
            {
                var original = string.IsNullOrWhiteSpace(layers[index].Name)
                    ? $"Layer{index}"
                    : layers[index].Name;
                layers[index].Name = PhantomAnimatorGraphUtility.BuildSlotLayerName(
                    slotId,
                    $"{playable}_{index}_{TransformPathUtility.SafeName(original, $"Layer{index}")}");
                if (index == 0)
                {
                    layers[index].DefaultWeight = 1f;
                }
            }
        }

        private static void RecordConvertedActionLayers(
            PhantomSlotBuildState slot,
            VirtualAnimatorController controller)
        {
            var layers = controller.Layers.ToArray();
            for (var index = 0; index < layers.Length; index++)
            {
                slot.ConvertedActionLayers.Add(new PhantomConvertedActionLayer(
                    layers[index].Name,
                    index == 0 ? 1f : layers[index].DefaultWeight));
            }
        }

        private readonly struct PreparedController
        {
            public readonly PhantomSourcePlayableRegistration Registration;
            public readonly VirtualAnimatorController Controller;

            public PreparedController(
                PhantomSourcePlayableRegistration registration,
                VirtualAnimatorController controller)
            {
                Registration = registration;
                Controller = controller;
            }
        }
    }
}
