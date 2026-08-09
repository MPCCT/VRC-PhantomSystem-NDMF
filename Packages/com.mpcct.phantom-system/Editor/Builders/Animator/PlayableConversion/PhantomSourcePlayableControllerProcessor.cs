using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Processes source playable controllers for safe phantom-scoped integration.</summary>
    internal static class PhantomSourcePlayableControllerProcessor
    {
        public static PhantomSourcePlayableProcessingResult Process(
            BuildContext context,
            PhantomSlotBuildState slot,
            PhantomSystemProjectSettingsSnapshot projectSettings,
            PhantomBuildReport report)
        {
            if (slot?.Slot == null)
            {
                return default;
            }

            slot.ConvertedActionLayers.Clear();
            if (slot.Slot.removeSourceControls)
            {
                return default;
            }

            var sources = CollectSources(slot.BakedAvatar);
            var prepared = new Dictionary<VRCAvatarDescriptor.AnimLayerType, PreparedController>();
            foreach (var pair in sources)
            {
                if (!TryGetBaseController(pair.Value.Controller, out var baseController))
                {
                    report.Error(
                        $"Slot '{slot.SlotId}' uses unsupported {pair.Key} controller type "
                        + $"'{pair.Value.Controller.GetType().FullName}'.",
                        slot.BakedAvatar);
                    continue;
                }

                var controller = UnityEngine.Object.Instantiate(baseController);
                controller.name = $"PhantomSystem_{slot.SlotId}_Processed{pair.Key}";
                PrefixLayers(controller, slot.SlotId, pair.Key);
                if (pair.Key == VRCAvatarDescriptor.AnimLayerType.Action)
                {
                    RecordConvertedActionLayers(slot, controller);
                }
                prepared[pair.Key] = new PreparedController(pair.Value, controller);
            }

            var targetLayers = BuildTargetLayerMap(prepared);
            var state = new ProcessingState(slot, report, targetLayers);
            foreach (var pair in prepared)
            {
                state.OwnerPlayable = pair.Key;
                var controller = pair.Value.Controller;
                PhantomPlayableMotionConverter.Convert(
                    context,
                    slot,
                    projectSettings,
                    report,
                    pair.Key,
                    pair.Value.Source.Controller,
                    controller,
                    pair.Value.Source.Mask);

                ProcessControllerBehaviours(controller, state);
            }

            foreach (var driver in state.CreatedDrivers)
            {
                context.AssetSaver.SaveAsset(driver);
            }
            foreach (var marker in state.CreatedMarkers)
            {
                context.AssetSaver.SaveAsset(marker);
            }
            foreach (var pair in prepared)
            {
                context.AssetSaver.SaveAsset(pair.Value.Controller);
            }

            state.ReportSummary();
            return new PhantomSourcePlayableProcessingResult(
                BuildOutput(
                    prepared,
                    VRCAvatarDescriptor.AnimLayerType.FX),
                BuildOutput(
                    prepared,
                    VRCAvatarDescriptor.AnimLayerType.Gesture),
                BuildOutput(
                    prepared,
                    VRCAvatarDescriptor.AnimLayerType.Action),
                state.DriverCount > 0);
        }

        private static Dictionary<VRCAvatarDescriptor.AnimLayerType, PhantomSourcePlayableLayer>
            CollectSources(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<VRCAvatarDescriptor.AnimLayerType, PhantomSourcePlayableLayer>();
            foreach (var type in new[]
                     {
                         VRCAvatarDescriptor.AnimLayerType.FX,
                         VRCAvatarDescriptor.AnimLayerType.Gesture,
                         VRCAvatarDescriptor.AnimLayerType.Action
                     })
            {
                if (PhantomSourcePlayableControllerUtility.TryGetLayer(
                        descriptor,
                        type,
                        out var layer)
                    && !layer.IsDefault
                    && layer.Controller != null)
                {
                    result[type] = layer;
                }
            }

            return result;
        }

        private static Dictionary<VRCAvatarDescriptor.AnimLayerType, IReadOnlyList<string>>
            BuildTargetLayerMap(
                IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, PreparedController> prepared)
        {
            var result = new Dictionary<VRCAvatarDescriptor.AnimLayerType, IReadOnlyList<string>>();
            foreach (var pair in prepared)
            {
                var names = new List<string>();
                foreach (var layer in pair.Value.Controller.layers)
                {
                    names.Add(layer.name);
                }
                result[pair.Key] = names;
            }
            return result;
        }

        private static void PrefixLayers(
            AnimatorController controller,
            string slotId,
            VRCAvatarDescriptor.AnimLayerType playable)
        {
            var layers = controller.layers;
            for (var index = 0; index < layers.Length; index++)
            {
                var original = string.IsNullOrWhiteSpace(layers[index].name)
                    ? $"Layer{index}"
                    : layers[index].name;
                layers[index].name =
                    $"PhantomSystem_{TransformPathUtility.SafeName(slotId)}_{playable}_{index}_{TransformPathUtility.SafeName(original, $"Layer{index}")}";
                if (index == 0)
                {
                    layers[index].defaultWeight = 1f;
                }
            }
            controller.layers = layers;
        }

        private static void RecordConvertedActionLayers(
            PhantomSlotBuildState slot,
            AnimatorController controller)
        {
            var layers = controller.layers;
            for (var index = 0; index < layers.Length; index++)
            {
                slot.ConvertedActionLayers.Add(new PhantomConvertedActionLayer(
                    layers[index].name,
                    index == 0 ? 1f : layers[index].defaultWeight));
            }
        }

        private static RuntimeAnimatorController BuildOutput(
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, PreparedController> prepared,
            VRCAvatarDescriptor.AnimLayerType playable)
        {
            if (!prepared.TryGetValue(playable, out var value))
            {
                return null;
            }

            return value.Controller;
        }

        private static bool TryGetBaseController(
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

        private static void ProcessControllerBehaviours(
            AnimatorController controller,
            ProcessingState state)
        {
            var processedStateMachines = new HashSet<AnimatorStateMachine>();
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine != null
                    && processedStateMachines.Add(layer.stateMachine))
                {
                    ProcessStateMachine(layer.stateMachine, state);
                }
            }

            var layers = controller.layers;
            for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var syncedLayerIndex = layers[layerIndex].syncedLayerIndex;
                if (syncedLayerIndex < 0 || syncedLayerIndex >= layers.Length)
                {
                    continue;
                }

                foreach (var animatorState in EnumerateStates(
                             layers[syncedLayerIndex].stateMachine))
                {
                    var changes = ProcessBehaviours(
                        controller.GetStateEffectiveBehaviours(animatorState, layerIndex),
                        state,
                        $"{layers[layerIndex].name}/{animatorState.name}",
                        () => ScriptableObject.CreateInstance<VRCAvatarParameterDriver>());
                    controller.SetStateEffectiveBehaviours(
                        animatorState,
                        layerIndex,
                        AppendBehaviour(changes.Behaviours, changes.Driver));
                    AppendDriver(changes.Driver, state);
                }
            }
        }

        private static void ProcessStateMachine(
            AnimatorStateMachine machine,
            ProcessingState state)
        {
            var machineChanges = ProcessBehaviours(
                machine.behaviours,
                state,
                machine.name,
                () => machine.AddStateMachineBehaviour<VRCAvatarParameterDriver>());
            machine.behaviours = AppendBehaviour(machineChanges.Behaviours, machineChanges.Driver);
            AppendDriver(machineChanges.Driver, state);

            foreach (var childState in machine.states)
            {
                if (childState.state == null)
                {
                    continue;
                }

                var changes = ProcessBehaviours(
                    childState.state.behaviours,
                    state,
                    childState.state.name,
                    () => childState.state.AddStateMachineBehaviour<VRCAvatarParameterDriver>());
                childState.state.behaviours = AppendBehaviour(changes.Behaviours, changes.Driver);
                AppendDriver(changes.Driver, state);
            }

            foreach (var childMachine in machine.stateMachines)
            {
                if (childMachine.stateMachine != null)
                {
                    ProcessStateMachine(childMachine.stateMachine, state);
                }
            }
        }

        private static BehaviourChanges ProcessBehaviours(
            StateMachineBehaviour[] behaviours,
            ProcessingState state,
            string ownerName,
            Func<VRCAvatarParameterDriver> createDriver)
        {
            var kept = new List<StateMachineBehaviour>();
            var converted = new Dictionary<PhantomTrackingControlGroup, bool>();
            foreach (var behaviour in behaviours ?? Array.Empty<StateMachineBehaviour>())
            {
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
                return new BehaviourChanges(kept.ToArray(), null);
            }

            var driver = createDriver();
            state.CreatedDrivers.Add(driver);
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

            return new BehaviourChanges(kept.ToArray(), driver);
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
                state.CreatedMarkers.Add(marker);
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

            if (!state.TargetLayers.TryGetValue(sourceTarget, out var layers)
                || control.layer < 0
                || control.layer >= layers.Count)
            {
                state.LayerControlRemoved++;
                return null;
            }

            var sameController = sourceTarget == state.OwnerPlayable;
            if (sameController)
            {
                return control;
            }

            var marker = ScriptableObject.CreateInstance<PhantomAnimatorLayerControlMarker>();
            marker.name = "Phantom Animator Layer Control Retarget";
            marker.targetPlayable = sourceTarget;
            marker.targetLayerName = layers[control.layer];
            marker.goalWeight = control.goalWeight;
            marker.blendDuration = control.blendDuration;
            marker.debugString = control.debugString;
            state.CreatedMarkers.Add(marker);
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

        private static IEnumerable<AnimatorState> EnumerateStates(AnimatorStateMachine machine)
        {
            if (machine == null)
            {
                yield break;
            }
            foreach (var child in machine.states)
            {
                if (child.state != null)
                {
                    yield return child.state;
                }
            }
            foreach (var childMachine in machine.stateMachines)
            {
                foreach (var state in EnumerateStates(childMachine.stateMachine))
                {
                    yield return state;
                }
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

        private static StateMachineBehaviour[] AppendBehaviour(
            StateMachineBehaviour[] behaviours,
            StateMachineBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return behaviours ?? Array.Empty<StateMachineBehaviour>();
            }
            var source = behaviours ?? Array.Empty<StateMachineBehaviour>();
            var result = new StateMachineBehaviour[source.Length + 1];
            Array.Copy(source, result, source.Length);
            result[result.Length - 1] = behaviour;
            return result;
        }

        private readonly struct PreparedController
        {
            public readonly PhantomSourcePlayableLayer Source;
            public readonly AnimatorController Controller;

            public PreparedController(
                PhantomSourcePlayableLayer source,
                AnimatorController controller)
            {
                Source = source;
                Controller = controller;
            }
        }

        private readonly struct BehaviourChanges
        {
            public readonly StateMachineBehaviour[] Behaviours;
            public readonly VRCAvatarParameterDriver Driver;

            public BehaviourChanges(
                StateMachineBehaviour[] behaviours,
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
            public readonly List<VRCAvatarParameterDriver> CreatedDrivers = new List<VRCAvatarParameterDriver>();
            public readonly List<PhantomAnimatorLayerControlMarker> CreatedMarkers = new List<PhantomAnimatorLayerControlMarker>();

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
                if (TrackingRemoved > 0)
                {
                    Report.Warning(
                        TrackingConverted > 0
                            ? $"Slot '{Slot.SlotId}' converted {TrackingConverted} Animator Tracking Control behavior(s) into {DriverCount} phantom parameter driver(s)."
                            : $"Slot '{Slot.SlotId}' removed {TrackingRemoved} Animator Tracking Control behavior(s) without conversion.",
                        Slot.BakedAvatar);
                }
                if (LocomotionRemoved > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' removed {LocomotionRemoved} avatar-global Animator Locomotion Control behavior(s).", Slot.BakedAvatar);
                }
                if (TemporaryPoseRemoved > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' removed {TemporaryPoseRemoved} avatar-global Animator Temporary Pose Space behavior(s).", Slot.BakedAvatar);
                }
                if (PlayableLayerRemoved > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' removed {PlayableLayerRemoved} Playable Layer Control behavior(s) with unavailable or unsupported targets.", Slot.BakedAvatar);
                }
                if (ActionPlayableLayerConverted > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' converted {ActionPlayableLayerConverted} binary Action Playable Layer Control behavior(s) into instant controls for all Converted Action layers.", Slot.BakedAvatar);
                }
                if (NonBinaryActionPlayableLayerRemoved > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' removed {NonBinaryActionPlayableLayerRemoved} Action Playable Layer Control behavior(s) whose Goal Weight was neither 0 nor 1.", Slot.BakedAvatar);
                }
                if (ActionPlayableBlendDurationIgnored > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' ignored Blend Duration on {ActionPlayableBlendDurationIgnored} converted Action Playable Layer Control behavior(s); Converted Action switching is instant.", Slot.BakedAvatar);
                }
                if (ActionLayerControlRemoved > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' removed {ActionLayerControlRemoved} Animator Layer Control behavior(s) targeting Action because Converted Action weight is controlled as one playable group.", Slot.BakedAvatar);
                }
                if (LayerControlRetargeted > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' scheduled {LayerControlRetargeted} Animator Layer Control behavior(s) for final layer retargeting.", Slot.BakedAvatar);
                }
                if (LayerControlRemoved > 0)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' removed {LayerControlRemoved} Animator Layer Control behavior(s) with unavailable or unsupported targets.", Slot.BakedAvatar);
                }
                if (EyePartialConversion)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' converts Eyes & Eyelids using available eye bones only.", Slot.BakedAvatar);
                }
                if (MouthPartialConversion)
                {
                    Report.Warning($"Slot '{Slot.SlotId}' converts Mouth & Jaw using the available jaw bone only.", Slot.BakedAvatar);
                }
            }
        }
    }

    internal readonly struct PhantomSourcePlayableProcessingResult
    {
        public readonly RuntimeAnimatorController FxController;
        public readonly RuntimeAnimatorController GestureController;
        public readonly RuntimeAnimatorController ActionController;
        public readonly bool HasTrackingConversion;

        public PhantomSourcePlayableProcessingResult(
            RuntimeAnimatorController fxController,
            RuntimeAnimatorController gestureController,
            RuntimeAnimatorController actionController,
            bool hasTrackingConversion)
        {
            FxController = fxController;
            GestureController = gestureController;
            ActionController = actionController;
            HasTrackingConversion = hasTrackingConversion;
        }
    }
}
