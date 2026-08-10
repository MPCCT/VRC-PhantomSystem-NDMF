using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Processes source playable controllers through NDMF's virtual animator graph.</summary>
    internal static class PhantomSourcePlayableControllerProcessor
    {
        public static void ProcessVirtual(
            BuildContext context,
            PhantomSlotBuildState slot,
            PhantomSystemProjectSettingsSnapshot projectSettings,
            PhantomBuildReport report)
        {
            if (slot?.Slot == null || slot.Slot.removeSourceControls)
            {
                return;
            }

            slot.ConvertedActionLayers.Clear();
            slot.MissingHumanoidBoneClips.Clear();
            slot.ConvertedClipReferences.Clear();
            slot.WarnedUnsupportedAnimatorClips.Clear();
            slot.HasTrackingControlConversion = false;

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

            var targetLayers = BuildTargetLayerMap(prepared);
            var state = new ProcessingState(slot, report, targetLayers);
            foreach (var pair in prepared)
            {
                state.OwnerPlayable = pair.Key;
                state.OwnerVirtualLayerIndices = pair.Value.Controller.Layers
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
                    report,
                    pair.Key,
                    pair.Value.Controller,
                    pair.Value.Registration.Source.Mask,
                    pair.Value.Registration.BaseController);
                ProcessControllerBehaviours(pair.Value.Controller, state);
            }

            var missingBoneSummary = BuildMissingBoneSummary(slot);
            if (!string.IsNullOrEmpty(missingBoneSummary))
            {
                report.Warning(missingBoneSummary, slot.CloneRoot);
            }

            state.ReportSummary();
            slot.HasTrackingControlConversion = state.DriverCount > 0;
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

        private static void ProcessControllerBehaviours(
            VirtualAnimatorController controller,
            ProcessingState state)
        {
            var layers = controller.Layers.ToArray();
            var processedStateMachines = new HashSet<VirtualStateMachine>();
            foreach (var layer in layers)
            {
                if (layer.StateMachine != null
                    && processedStateMachines.Add(layer.StateMachine))
                {
                    ProcessStateMachine(layer.StateMachine, state);
                }
            }

            foreach (var layer in layers)
            {
                if (layer.SyncedLayerBehaviourOverrides.Count == 0)
                {
                    continue;
                }

                var overrides = layer.SyncedLayerBehaviourOverrides.ToBuilder();
                foreach (var pair in layer.SyncedLayerBehaviourOverrides)
                {
                    var changes = ProcessBehaviours(
                        pair.Value,
                        state,
                        $"{layer.Name}/{pair.Key.Name}");
                    overrides[pair.Key] = AppendBehaviour(changes.Behaviours, changes.Driver);
                    AppendDriver(changes.Driver, state);
                }
                layer.SyncedLayerBehaviourOverrides = overrides.ToImmutable();
            }
        }

        private static void ProcessStateMachine(
            VirtualStateMachine machine,
            ProcessingState state)
        {
            var machineChanges = ProcessBehaviours(machine.Behaviours, state, machine.Name);
            machine.Behaviours = AppendBehaviour(machineChanges.Behaviours, machineChanges.Driver);
            AppendDriver(machineChanges.Driver, state);

            foreach (var childState in machine.States)
            {
                if (childState.State == null)
                {
                    continue;
                }

                var changes = ProcessBehaviours(
                    childState.State.Behaviours,
                    state,
                    childState.State.Name);
                childState.State.Behaviours = AppendBehaviour(changes.Behaviours, changes.Driver);
                AppendDriver(changes.Driver, state);
            }

            foreach (var childMachine in machine.StateMachines)
            {
                if (childMachine.StateMachine != null)
                {
                    ProcessStateMachine(childMachine.StateMachine, state);
                }
            }
        }

        private static BehaviourChanges ProcessBehaviours(
            IEnumerable<StateMachineBehaviour> behaviours,
            ProcessingState state,
            string ownerName)
        {
            var kept = new List<StateMachineBehaviour>();
            var converted = new Dictionary<PhantomTrackingControlGroup, bool>();
            foreach (var behaviour in behaviours ?? Enumerable.Empty<StateMachineBehaviour>())
            {
                if (behaviour is VRCAnimatorPlayAudio playAudio)
                {
                    RemapPlayAudioParameter(playAudio, state.Slot);
                }

                if (behaviour is VRCAnimatorTrackingControl tracking)
                {
                    state.TrackingRemoved++;
                    if (state.Slot.Slot.tryConvertAnimatorTrackingControl
                        && CollectTracking(converted, tracking))
                    {
                        state.TrackingConverted++;
                    }
                    continue;
                }

                if (behaviour is VRCAnimatorLocomotionControl)
                {
                    state.LocomotionRemoved++;
                    continue;
                }
                if (behaviour is VRCAnimatorTemporaryPoseSpace)
                {
                    state.TemporaryPoseRemoved++;
                    continue;
                }
                if (behaviour is VRCPlayableLayerControl playableLayerControl)
                {
                    ProcessPlayableLayerControl(playableLayerControl, state, kept);
                    continue;
                }
                if (behaviour is VRCAnimatorLayerControl layerControl)
                {
                    var replacement = ProcessLayerControl(layerControl, state);
                    if (replacement != null)
                    {
                        kept.Add(replacement);
                    }
                    continue;
                }

                kept.Add(behaviour);
            }

            if (converted.Count == 0)
            {
                return new BehaviourChanges(kept.ToImmutableList(), null);
            }

            var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
            driver.name = $"Phantom Tracking Conversion ({ownerName})";
            driver.localOnly = false;
            driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>();
            foreach (var pair in converted)
            {
                driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type = VRC_AvatarParameterDriver.ChangeType.Set,
                    name = PhantomTrackingControlGroups.Parameter(state.Slot.Slot, pair.Key),
                    value = pair.Value ? 1f : 0f
                });
            }

            return new BehaviourChanges(kept.ToImmutableList(), driver);
        }

        internal static void RemapPlayAudioParameter(
            VRCAnimatorPlayAudio playAudio,
            PhantomSlotBuildState slot)
        {
            if (playAudio == null
                || string.IsNullOrWhiteSpace(playAudio.ParameterName)
                || slot?.Slot == null)
            {
                return;
            }

            if (PhantomSourceParameterMapping.TryResolve(
                    slot,
                    playAudio.ParameterName,
                    "Animator Play Audio",
                    out var finalName))
            {
                playAudio.ParameterName = finalName;
            }
        }

        private static void ProcessPlayableLayerControl(
            VRCPlayableLayerControl control,
            ProcessingState state,
            ICollection<StateMachineBehaviour> kept)
        {
            if (control.layer != VRC_PlayableLayerControl.BlendableLayer.Action
                || state.Slot.ConvertedActionLayers.Count == 0)
            {
                state.PlayableLayerRemoved++;
                return;
            }

            bool enabled;
            if (Mathf.Approximately(control.goalWeight, 0f))
            {
                enabled = false;
            }
            else if (Mathf.Approximately(control.goalWeight, 1f))
            {
                enabled = true;
            }
            else
            {
                state.NonBinaryActionPlayableLayerRemoved++;
                return;
            }

            if (!Mathf.Approximately(control.blendDuration, 0f))
            {
                state.ActionPlayableBlendDurationIgnored++;
            }

            for (var index = 0; index < state.Slot.ConvertedActionLayers.Count; index++)
            {
                var actionLayer = state.Slot.ConvertedActionLayers[index];
                var marker = ScriptableObject.CreateInstance<PhantomAnimatorLayerControlMarker>();
                marker.name = "Phantom Action Playable Layer Control Retarget";
                marker.targetPlayable = VRCAvatarDescriptor.AnimLayerType.FX;
                marker.targetLayerName = actionLayer.LayerName;
                marker.goalWeight = enabled ? actionLayer.EnabledWeight : 0f;
                marker.blendDuration = 0f;
                marker.debugString = index == 0 ? control.debugString : string.Empty;
                kept.Add(marker);
            }

            state.ActionPlayableLayerConverted++;
        }

        private static StateMachineBehaviour ProcessLayerControl(
            VRCAnimatorLayerControl control,
            ProcessingState state)
        {
            if (!TryConvertPlayable(control.playable, out var sourceTarget))
            {
                state.LayerControlRemoved++;
                return null;
            }

            if (sourceTarget == VRCAvatarDescriptor.AnimLayerType.Action)
            {
                state.ActionLayerControlRemoved++;
                return null;
            }

            if (sourceTarget == state.OwnerPlayable)
            {
                // NDMF has already virtualized the layer index for controls targeting
                // the controller currently being processed. Preserve it for commit.
                return control;
            }

            var targetLayerIndex = control.layer;
            if (state.OwnerVirtualLayerIndices != null
                && state.OwnerVirtualLayerIndices.TryGetValue(
                    targetLayerIndex,
                    out var originalLayerIndex))
            {
                // Action controllers are merged into FX, so NDMF can virtualize an FX
                // control against the Action controller. Recover its serialized index
                // before resolving the actual source FX layer name.
                targetLayerIndex = originalLayerIndex;
            }

            if (!state.TargetLayers.TryGetValue(sourceTarget, out var layers)
                || targetLayerIndex < 0
                || targetLayerIndex >= layers.Count)
            {
                state.LayerControlRemoved++;
                return null;
            }

            var marker = ScriptableObject.CreateInstance<PhantomAnimatorLayerControlMarker>();
            marker.name = "Phantom Animator Layer Control Retarget";
            marker.targetPlayable = sourceTarget;
            marker.targetLayerName = layers[targetLayerIndex];
            marker.goalWeight = control.goalWeight;
            marker.blendDuration = control.blendDuration;
            marker.debugString = control.debugString;
            state.LayerControlRetargeted++;
            return marker;
        }

        private static bool TryConvertPlayable(
            VRC_AnimatorLayerControl.BlendableLayer playable,
            out VRCAvatarDescriptor.AnimLayerType result)
        {
            switch (playable)
            {
                case VRC_AnimatorLayerControl.BlendableLayer.FX:
                    result = VRCAvatarDescriptor.AnimLayerType.FX;
                    return true;
                case VRC_AnimatorLayerControl.BlendableLayer.Gesture:
                    result = VRCAvatarDescriptor.AnimLayerType.Gesture;
                    return true;
                case VRC_AnimatorLayerControl.BlendableLayer.Action:
                    result = VRCAvatarDescriptor.AnimLayerType.Action;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool CollectTracking(
            Dictionary<PhantomTrackingControlGroup, bool> values,
            VRCAnimatorTrackingControl tracking)
        {
            var converted = false;
            converted |= Add(values, PhantomTrackingControlGroup.Head, tracking.trackingHead);
            converted |= Add(values, PhantomTrackingControlGroup.LeftHand, tracking.trackingLeftHand);
            converted |= Add(values, PhantomTrackingControlGroup.RightHand, tracking.trackingRightHand);
            converted |= Add(values, PhantomTrackingControlGroup.Hip, tracking.trackingHip);
            converted |= Add(values, PhantomTrackingControlGroup.LeftFoot, tracking.trackingLeftFoot);
            converted |= Add(values, PhantomTrackingControlGroup.RightFoot, tracking.trackingRightFoot);
            converted |= Add(values, PhantomTrackingControlGroup.LeftFingers, tracking.trackingLeftFingers);
            converted |= Add(values, PhantomTrackingControlGroup.RightFingers, tracking.trackingRightFingers);
            converted |= Add(values, PhantomTrackingControlGroup.Eyes, tracking.trackingEyes);
            converted |= Add(values, PhantomTrackingControlGroup.Mouth, tracking.trackingMouth);
            return converted;
        }

        private static bool Add(
            IDictionary<PhantomTrackingControlGroup, bool> values,
            PhantomTrackingControlGroup group,
            VRC_AnimatorTrackingControl.TrackingType value)
        {
            if (value == VRC_AnimatorTrackingControl.TrackingType.NoChange)
            {
                return false;
            }
            values[group] = value == VRC_AnimatorTrackingControl.TrackingType.Tracking;
            return true;
        }

        private static void AppendDriver(VRCAvatarParameterDriver driver, ProcessingState state)
        {
            if (driver == null)
            {
                return;
            }
            state.DriverCount++;
            state.EyePartialConversion |= driver.parameters.Exists(parameter =>
                parameter.name == PhantomParameterNames.TrackingEyes(state.Slot.Slot));
            state.MouthPartialConversion |= driver.parameters.Exists(parameter =>
                parameter.name == PhantomParameterNames.TrackingMouth(state.Slot.Slot));
        }

        private static ImmutableList<StateMachineBehaviour> AppendBehaviour(
            ImmutableList<StateMachineBehaviour> behaviours,
            StateMachineBehaviour behaviour)
        {
            var source = behaviours ?? ImmutableList<StateMachineBehaviour>.Empty;
            return behaviour == null ? source : source.Add(behaviour);
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

        private readonly struct BehaviourChanges
        {
            public readonly ImmutableList<StateMachineBehaviour> Behaviours;
            public readonly VRCAvatarParameterDriver Driver;

            public BehaviourChanges(
                ImmutableList<StateMachineBehaviour> behaviours,
                VRCAvatarParameterDriver driver)
            {
                Behaviours = behaviours;
                Driver = driver;
            }
        }

        private sealed class ProcessingState
        {
            public readonly PhantomSlotBuildState Slot;
            public readonly PhantomBuildReport Report;
            public readonly IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, IReadOnlyList<string>> TargetLayers;
            public VRCAvatarDescriptor.AnimLayerType OwnerPlayable;
            public IReadOnlyDictionary<int, int> OwnerVirtualLayerIndices;
            public int TrackingRemoved;
            public int TrackingConverted;
            public int LocomotionRemoved;
            public int TemporaryPoseRemoved;
            public int PlayableLayerRemoved;
            public int ActionPlayableLayerConverted;
            public int NonBinaryActionPlayableLayerRemoved;
            public int ActionPlayableBlendDurationIgnored;
            public int ActionLayerControlRemoved;
            public int LayerControlRemoved;
            public int LayerControlRetargeted;
            public int DriverCount;
            public bool EyePartialConversion;
            public bool MouthPartialConversion;

            public ProcessingState(
                PhantomSlotBuildState slot,
                PhantomBuildReport report,
                IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, IReadOnlyList<string>> targetLayers)
            {
                Slot = slot;
                Report = report;
                TargetLayers = targetLayers;
            }

            public void ReportSummary()
            {
                var reportContext = Slot.CloneRoot;
                if (TrackingRemoved > 0)
                {
                    if (TrackingConverted > 0)
                    {
                        Report.Info(
                            $"Slot '{Slot.SlotId}' converted {TrackingConverted} Animator Tracking Control behavior(s) into {DriverCount} phantom parameter driver(s).",
                            reportContext);
                    }
                    else
                    {
                        Report.Warning(
                            $"Slot '{Slot.SlotId}' removed {TrackingRemoved} Animator Tracking Control behavior(s) without conversion.",
                            reportContext);
                    }
                }
                if (LocomotionRemoved > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' removed {LocomotionRemoved} avatar-global Animator Locomotion Control behavior(s).", reportContext);
                }
                if (TemporaryPoseRemoved > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' removed {TemporaryPoseRemoved} avatar-global Animator Temporary Pose Space behavior(s).", reportContext);
                }
                if (PlayableLayerRemoved > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' removed {PlayableLayerRemoved} Playable Layer Control behavior(s) with unavailable or unsupported targets.", reportContext);
                }
                if (ActionPlayableLayerConverted > 0)
                {
                    Report.Info($"Slot '{Slot.SlotId}' converted {ActionPlayableLayerConverted} binary Action Playable Layer Control behavior(s) into instant controls for all Converted Action layers.", reportContext);
                }
                if (NonBinaryActionPlayableLayerRemoved > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' removed {NonBinaryActionPlayableLayerRemoved} Action Playable Layer Control behavior(s) whose Goal Weight was neither 0 nor 1.", reportContext);
                }
                if (ActionPlayableBlendDurationIgnored > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' ignored Blend Duration on {ActionPlayableBlendDurationIgnored} converted Action Playable Layer Control behavior(s); Converted Action switching is instant.", reportContext);
                }
                if (ActionLayerControlRemoved > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' removed {ActionLayerControlRemoved} Animator Layer Control behavior(s) targeting Action because Converted Action weight is controlled as one playable group.", reportContext);
                }
                if (LayerControlRetargeted > 0)
                {
                    Report.Info($"Slot '{Slot.SlotId}' scheduled {LayerControlRetargeted} Animator Layer Control behavior(s) for final layer retargeting.", reportContext);
                }
                if (LayerControlRemoved > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' removed {LayerControlRemoved} Animator Layer Control behavior(s) with unavailable or unsupported targets.", reportContext);
                }
                if (EyePartialConversion)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' converts Eyes & Eyelids using available eye bones only.", reportContext);
                }
                if (MouthPartialConversion)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' converts Mouth & Jaw using the available jaw bone only.", reportContext);
                }
            }
        }
    }
}
