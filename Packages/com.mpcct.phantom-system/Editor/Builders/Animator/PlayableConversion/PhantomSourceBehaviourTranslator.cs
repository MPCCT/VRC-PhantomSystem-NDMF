using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Aggregated, controller-independent result of source state behaviour translation.</summary>
    internal class PhantomSourceBehaviourTranslationResult
    {
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

        public void Merge(PhantomSourceBehaviourTranslationResult other)
        {
            if (other == null)
            {
                return;
            }

            TrackingRemoved += other.TrackingRemoved;
            TrackingConverted += other.TrackingConverted;
            LocomotionRemoved += other.LocomotionRemoved;
            TemporaryPoseRemoved += other.TemporaryPoseRemoved;
            PlayableLayerRemoved += other.PlayableLayerRemoved;
            ActionPlayableLayerConverted += other.ActionPlayableLayerConverted;
            NonBinaryActionPlayableLayerRemoved += other.NonBinaryActionPlayableLayerRemoved;
            ActionPlayableBlendDurationIgnored += other.ActionPlayableBlendDurationIgnored;
            ActionLayerControlRemoved += other.ActionLayerControlRemoved;
            LayerControlRemoved += other.LayerControlRemoved;
            LayerControlRetargeted += other.LayerControlRetargeted;
            DriverCount += other.DriverCount;
            EyePartialConversion |= other.EyePartialConversion;
            MouthPartialConversion |= other.MouthPartialConversion;
        }

        public void ReportSummary(PhantomSlotBuildState slot, PhantomBuildReport report)
        {
            if (slot == null || report == null)
            {
                return;
            }

            var reportContext = slot.CloneRoot;
            if (TrackingRemoved > 0)
            {
                if (TrackingConverted > 0)
                {
                    report.Info(
                        $"Slot '{slot.SlotId}' converted {TrackingConverted} Animator Tracking Control behavior(s) into {DriverCount} phantom parameter driver(s).",
                        reportContext);
                }
                else
                {
                    report.Warning(
                        $"Slot '{slot.SlotId}' removed {TrackingRemoved} Animator Tracking Control behavior(s) without conversion.",
                        reportContext);
                }
            }
            if (LocomotionRemoved > 0)
            {
                report.Warning($"Slot '{slot.SlotId}' removed {LocomotionRemoved} avatar-global Animator Locomotion Control behavior(s).", reportContext);
            }
            if (TemporaryPoseRemoved > 0)
            {
                report.Warning($"Slot '{slot.SlotId}' removed {TemporaryPoseRemoved} avatar-global Animator Temporary Pose Space behavior(s).", reportContext);
            }
            if (PlayableLayerRemoved > 0)
            {
                report.Warning($"Slot '{slot.SlotId}' removed {PlayableLayerRemoved} Playable Layer Control behavior(s) with unavailable or unsupported targets.", reportContext);
            }
            if (ActionPlayableLayerConverted > 0)
            {
                report.Info($"Slot '{slot.SlotId}' converted {ActionPlayableLayerConverted} binary Action Playable Layer Control behavior(s) into instant controls for all Converted Action layers.", reportContext);
            }
            if (NonBinaryActionPlayableLayerRemoved > 0)
            {
                report.Warning($"Slot '{slot.SlotId}' removed {NonBinaryActionPlayableLayerRemoved} Action Playable Layer Control behavior(s) whose Goal Weight was neither 0 nor 1.", reportContext);
            }
            if (ActionPlayableBlendDurationIgnored > 0)
            {
                report.Warning($"Slot '{slot.SlotId}' ignored Blend Duration on {ActionPlayableBlendDurationIgnored} converted Action Playable Layer Control behavior(s); Converted Action switching is instant.", reportContext);
            }
            if (ActionLayerControlRemoved > 0)
            {
                report.Warning($"Slot '{slot.SlotId}' removed {ActionLayerControlRemoved} Animator Layer Control behavior(s) targeting Action because Converted Action weight is controlled as one playable group.", reportContext);
            }
            if (LayerControlRetargeted > 0)
            {
                report.Info($"Slot '{slot.SlotId}' scheduled {LayerControlRetargeted} Animator Layer Control behavior(s) for final layer retargeting.", reportContext);
            }
            if (LayerControlRemoved > 0)
            {
                report.Warning($"Slot '{slot.SlotId}' removed {LayerControlRemoved} Animator Layer Control behavior(s) with unavailable or unsupported targets.", reportContext);
            }
            if (EyePartialConversion)
            {
                report.Warning($"Slot '{slot.SlotId}' converts Eyes & Eyelids using available eye bones only.", reportContext);
            }
            if (MouthPartialConversion)
            {
                report.Warning($"Slot '{slot.SlotId}' converts Mouth & Jaw using the available jaw bone only.", reportContext);
            }
        }
    }

    /// <summary>Translates state behaviours without owning controller preparation or motion conversion.</summary>
    internal static class PhantomSourceBehaviourTranslator
    {
        public static PhantomSourceBehaviourTranslationResult Translate(
            VirtualAnimatorController controller,
            PhantomSlotBuildState slot,
            VRCAvatarDescriptor.AnimLayerType ownerPlayable,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, IReadOnlyList<string>> targetLayers,
            IReadOnlyDictionary<int, int> ownerVirtualLayerIndices)
        {
            var state = new ProcessingState(
                slot,
                ownerPlayable,
                targetLayers,
                ownerVirtualLayerIndices);
            ProcessControllerBehaviours(controller, state);
            return state;
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
                kept.Add(CreateConvertedActionLayerControlMarker(
                    actionLayer,
                    enabled,
                    index == 0 ? control.debugString : string.Empty));
            }

            state.ActionPlayableLayerConverted++;
        }

        internal static PhantomAnimatorLayerControlMarker CreateConvertedActionLayerControlMarker(
            PhantomConvertedActionLayer actionLayer,
            bool enabled,
            string debugString)
        {
            var marker = ScriptableObject.CreateInstance<PhantomAnimatorLayerControlMarker>();
            marker.name = "Phantom Action Playable Layer Control Retarget";
            marker.targetPlayable = PhantomSourceIntegrationBuilder.ResolveMergeTarget(
                VRCAvatarDescriptor.AnimLayerType.Action);
            marker.targetLayerName = actionLayer.LayerName;
            marker.goalWeight = enabled ? actionLayer.EnabledWeight : 0f;
            marker.blendDuration = 0f;
            marker.debugString = debugString;
            return marker;
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

            var finalTarget = PhantomSourceIntegrationBuilder.ResolveMergeTarget(sourceTarget);
            if (CanPreserveLayerControl(sourceTarget, state.OwnerPlayable))
            {
                // NDMF already virtualized controls targeting the controller currently being processed.
                return control;
            }

            var targetLayerIndex = control.layer;
            if (state.OwnerVirtualLayerIndices != null
                && state.OwnerVirtualLayerIndices.TryGetValue(
                    targetLayerIndex,
                    out var originalLayerIndex))
            {
                // Recover the serialized index before resolving a cross-controller target by layer name.
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
            marker.targetPlayable = finalTarget;
            marker.targetLayerName = layers[targetLayerIndex];
            marker.goalWeight = control.goalWeight;
            marker.blendDuration = control.blendDuration;
            marker.debugString = control.debugString;
            state.LayerControlRetargeted++;
            return marker;
        }

        internal static bool CanPreserveLayerControl(
            VRCAvatarDescriptor.AnimLayerType sourceTarget,
            VRCAvatarDescriptor.AnimLayerType ownerPlayable)
        {
            return sourceTarget == ownerPlayable
                   && PhantomSourceIntegrationBuilder.ResolveMergeTarget(sourceTarget) == sourceTarget;
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
            IDictionary<PhantomTrackingControlGroup, bool> values,
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

        private sealed class ProcessingState : PhantomSourceBehaviourTranslationResult
        {
            public readonly PhantomSlotBuildState Slot;
            public readonly IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, IReadOnlyList<string>>
                TargetLayers;
            public readonly VRCAvatarDescriptor.AnimLayerType OwnerPlayable;
            public readonly IReadOnlyDictionary<int, int> OwnerVirtualLayerIndices;

            public ProcessingState(
                PhantomSlotBuildState slot,
                VRCAvatarDescriptor.AnimLayerType ownerPlayable,
                IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, IReadOnlyList<string>> targetLayers,
                IReadOnlyDictionary<int, int> ownerVirtualLayerIndices)
            {
                Slot = slot;
                OwnerPlayable = ownerPlayable;
                TargetLayers = targetLayers;
                OwnerVirtualLayerIndices = ownerVirtualLayerIndices;
            }
        }
    }
}
