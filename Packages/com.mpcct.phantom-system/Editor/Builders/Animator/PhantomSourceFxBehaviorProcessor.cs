using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Removes source behaviors that cannot be scoped to a phantom and optionally converts tracking.</summary>
    internal static class PhantomSourceFxBehaviorProcessor
    {
        public static PhantomSourceFxProcessingResult Process(
            BuildContext context,
            PhantomSlotBuildState slot,
            PhantomBuildReport report)
        {
            var source = PhantomSourceFxControllerUtility.GetController(slot.BakedAvatar);
            if (source == null || slot.Slot == null || slot.Slot.removeOriginalFx)
            {
                return new PhantomSourceFxProcessingResult(source, false);
            }

            if (!TryGetBaseController(source, out var sourceController))
            {
                report.Error(
                    $"Slot '{slot.SlotId}' uses unsupported FX controller type "
                    + $"'{source.GetType().FullName}'. Use an AnimatorController or AnimatorOverrideController asset, "
                    + "or disable the source FX integration.",
                    slot.BakedAvatar);
                return new PhantomSourceFxProcessingResult(source, false);
            }

            if (!RequiresProcessing(sourceController))
            {
                return new PhantomSourceFxProcessingResult(source, false);
            }

            var controller = UnityEngine.Object.Instantiate(sourceController);
            controller.name = $"PhantomSystem_{slot.SlotId}_ProcessedFX";
            var state = new ProcessingState(slot, report);
            var processedStateMachines = new HashSet<AnimatorStateMachine>();
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine != null
                    && processedStateMachines.Add(layer.stateMachine))
                {
                    ProcessStateMachine(layer.stateMachine, state);
                }
            }
            ProcessSyncedLayerBehaviours(controller, state);

            foreach (var driver in state.CreatedDrivers)
            {
                context.AssetSaver.SaveAsset(driver);
            }
            context.AssetSaver.SaveAsset(controller);
            var output = ApplyOverrides(context, source, controller, slot.SlotId);
            state.ReportSummary();
            return new PhantomSourceFxProcessingResult(
                output,
                state.DriverCount > 0);
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

        private static RuntimeAnimatorController ApplyOverrides(
            BuildContext context,
            RuntimeAnimatorController source,
            AnimatorController processedController,
            string slotId)
        {
            var overrideChain = new List<AnimatorOverrideController>();
            var current = source;
            while (current is AnimatorOverrideController sourceOverride)
            {
                overrideChain.Add(sourceOverride);
                current = sourceOverride.runtimeAnimatorController;
            }

            RuntimeAnimatorController output = processedController;
            for (var index = overrideChain.Count - 1; index >= 0; index--)
            {
                var sourceOverride = overrideChain[index];
                var rebuiltOverride = new AnimatorOverrideController(output)
                {
                    name = index == 0
                        ? $"PhantomSystem_{slotId}_ProcessedFXOverrides"
                        : $"PhantomSystem_{slotId}_ProcessedFXOverrides_{index}"
                };
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(
                    sourceOverride.overridesCount);
                sourceOverride.GetOverrides(overrides);
                rebuiltOverride.ApplyOverrides(overrides);
                context.AssetSaver.SaveAsset(rebuiltOverride);
                output = rebuiltOverride;
            }

            return output;
        }

        private static bool RequiresProcessing(AnimatorController controller)
        {
            var layers = controller.layers;
            foreach (var layer in layers)
            {
                if (layer.stateMachine != null && RequiresProcessing(layer.stateMachine))
                {
                    return true;
                }
            }

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
                    if (ContainsProcessableBehaviour(
                            controller.GetStateEffectiveBehaviours(
                                animatorState,
                                layerIndex)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void ProcessSyncedLayerBehaviours(
            AnimatorController controller,
            ProcessingState state)
        {
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
                        controller.GetStateEffectiveBehaviours(
                            animatorState,
                            layerIndex),
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

        private static IEnumerable<AnimatorState> EnumerateStates(
            AnimatorStateMachine machine)
        {
            if (machine == null)
            {
                yield break;
            }

            foreach (var childState in machine.states)
            {
                if (childState.state != null)
                {
                    yield return childState.state;
                }
            }

            foreach (var childMachine in machine.stateMachines)
            {
                foreach (var animatorState in EnumerateStates(
                             childMachine.stateMachine))
                {
                    yield return animatorState;
                }
            }
        }

        private static bool RequiresProcessing(AnimatorStateMachine machine)
        {
            if (ContainsProcessableBehaviour(machine.behaviours))
            {
                return true;
            }

            foreach (var state in machine.states)
            {
                if (state.state != null && ContainsProcessableBehaviour(state.state.behaviours))
                {
                    return true;
                }
            }

            foreach (var child in machine.stateMachines)
            {
                if (child.stateMachine != null && RequiresProcessing(child.stateMachine))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsProcessableBehaviour(StateMachineBehaviour[] behaviours)
        {
            if (behaviours == null)
            {
                return false;
            }

            foreach (var behaviour in behaviours)
            {
                if (behaviour is VRCAnimatorTrackingControl
                    || behaviour is VRCAnimatorLocomotionControl
                    || behaviour is VRCAnimatorTemporaryPoseSpace
                    || behaviour is VRCPlayableLayerControl
                    || IsNonFxLayerControl(behaviour))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNonFxLayerControl(StateMachineBehaviour behaviour)
        {
            return behaviour is VRCAnimatorLayerControl layerControl
                   && layerControl.playable != VRC_AnimatorLayerControl.BlendableLayer.FX;
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
            machine.behaviours = AppendBehaviour(
                machineChanges.Behaviours,
                machineChanges.Driver);
            AppendDriver(machineChanges.Driver, state);

            foreach (var childState in machine.states)
            {
                if (childState.state == null)
                {
                    continue;
                }

                var stateChanges = ProcessBehaviours(
                    childState.state.behaviours,
                    state,
                    childState.state.name,
                    () => childState.state.AddStateMachineBehaviour<VRCAvatarParameterDriver>());
                childState.state.behaviours = AppendBehaviour(
                    stateChanges.Behaviours,
                    stateChanges.Driver);
                AppendDriver(stateChanges.Driver, state);
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
            if (behaviours != null)
            {
                foreach (var behaviour in behaviours)
                {
                    if (behaviour is VRCAnimatorTrackingControl tracking)
                    {
                        state.TrackingRemoved++;
                        if (state.Slot.Slot.tryConvertAnimatorTrackingControl)
                        {
                            if (CollectTracking(converted, tracking))
                            {
                                state.TrackingConverted++;
                            }
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

                    if (behaviour is VRCPlayableLayerControl)
                    {
                        state.PlayableLayerRemoved++;
                        continue;
                    }

                    if (IsNonFxLayerControl(behaviour))
                    {
                        state.NonFxLayerRemoved++;
                        continue;
                    }

                    kept.Add(behaviour);
                }
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
            Dictionary<PhantomTrackingControlGroup, bool> values,
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

        private static void AppendDriver(
            VRCAvatarParameterDriver driver,
            ProcessingState state)
        {
            if (driver == null)
            {
                return;
            }

            state.DriverCount++;
            if (driver.parameters.Exists(parameter =>
                    parameter.name == PhantomParameterNames.TrackingEyes(state.Slot.Slot)))
            {
                state.EyePartialConversion = true;
            }

            if (driver.parameters.Exists(parameter =>
                    parameter.name == PhantomParameterNames.TrackingMouth(state.Slot.Slot)))
            {
                state.MouthPartialConversion = true;
            }
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
            result[source.Length] = behaviour;
            return result;
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
            public int TrackingRemoved;
            public int TrackingConverted;
            public int LocomotionRemoved;
            public int TemporaryPoseRemoved;
            public int PlayableLayerRemoved;
            public int NonFxLayerRemoved;
            public int DriverCount;
            public bool EyePartialConversion;
            public bool MouthPartialConversion;
            public readonly List<VRCAvatarParameterDriver> CreatedDrivers =
                new List<VRCAvatarParameterDriver>();

            public ProcessingState(PhantomSlotBuildState slot, PhantomBuildReport report)
            {
                Slot = slot;
                Report = report;
            }

            public void ReportSummary()
            {
                if (TrackingRemoved > 0)
                {
                    if (TrackingConverted > 0)
                    {
                        Report.Warning(
                            $"Slot '{Slot.SlotId}' converted {TrackingConverted} Animator Tracking Control behavior(s) into "
                            + $"{DriverCount} phantom parameter driver(s).",
                            Slot.BakedAvatar);
                    }
                    else if (!Slot.Slot.tryConvertAnimatorTrackingControl)
                    {
                        Report.Warning(
                            $"Slot '{Slot.SlotId}' removed {TrackingRemoved} Animator Tracking Control behavior(s); "
                            + "enable 'Try Convert Animator Tracking Control' to map them to phantom bone synchronization.",
                            Slot.BakedAvatar);
                    }
                    else
                    {
                        Report.Warning(
                            $"Slot '{Slot.SlotId}' removed {TrackingRemoved} Animator Tracking Control behavior(s), "
                            + "but they contained only No Change values and required no phantom bone conversion.",
                            Slot.BakedAvatar);
                    }
                }

                if (LocomotionRemoved > 0)
                {
                    Report.Warning(
                        $"Slot '{Slot.SlotId}' removed {LocomotionRemoved} Animator Locomotion Control behavior(s) because locomotion is avatar-global.",
                        Slot.BakedAvatar);
                }

                if (TemporaryPoseRemoved > 0)
                {
                    Report.Warning(
                        $"Slot '{Slot.SlotId}' removed {TemporaryPoseRemoved} Animator Temporary Pose Space behavior(s) because pose space is avatar-global.",
                        Slot.BakedAvatar);
                }

                if (PlayableLayerRemoved > 0)
                {
                    Report.Warning(
                        $"Slot '{Slot.SlotId}' removed {PlayableLayerRemoved} Playable Layer Control behavior(s) because playable-layer weight is avatar-global.",
                        Slot.BakedAvatar);
                }

                if (NonFxLayerRemoved > 0)
                {
                    Report.Warning(
                        $"Slot '{Slot.SlotId}' removed {NonFxLayerRemoved} non-FX Animator Layer Control behavior(s); only merged FX layers can be scoped to the phantom.",
                        Slot.BakedAvatar);
                }

                if (EyePartialConversion)
                {
                    Report.Warning(
                        $"Slot '{Slot.SlotId}' converts Eyes & Eyelids using available eye bones only; eyelid simulation and blend shapes are not translated.",
                        Slot.BakedAvatar);
                }

                if (MouthPartialConversion)
                {
                    Report.Warning(
                        $"Slot '{Slot.SlotId}' converts Mouth & Jaw using the available jaw bone only; viseme simulation and blend shapes are not translated.",
                        Slot.BakedAvatar);
                }
            }
        }
    }

    internal readonly struct PhantomSourceFxProcessingResult
    {
        public readonly RuntimeAnimatorController Controller;
        public readonly bool HasTrackingConversion;

        public PhantomSourceFxProcessingResult(
            RuntimeAnimatorController controller,
            bool hasTrackingConversion)
        {
            Controller = controller;
            HasTrackingConversion = hasTrackingConversion;
        }
    }
}
