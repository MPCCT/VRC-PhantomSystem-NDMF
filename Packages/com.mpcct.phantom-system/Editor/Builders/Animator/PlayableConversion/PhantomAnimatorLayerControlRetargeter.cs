using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Resolves build-only layer-control markers after the final layer order is stable.</summary>
    internal static class PhantomAnimatorLayerControlRetargeter
    {
        public static void Retarget(BuildContext context, PhantomBuildState state)
        {
            if (state == null || !state.HasWork)
            {
                return;
            }

            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                state.Report.Error("Cannot retarget Phantom Animator Layer Controls because the final avatar descriptor is missing.");
                return;
            }

            var controllers = CollectControllers(descriptor);
            var targets = BuildTargetMap(controllers, state.Report);
            foreach (var pair in controllers)
            {
                ProcessController(context, pair.Value, targets, state.Report);
            }

            foreach (var pair in controllers)
            {
                VerifyNoMarkers(pair.Key, pair.Value, state.Report);
            }
        }

        private static Dictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController>
            CollectControllers(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController>();
            AddLayers(descriptor.baseAnimationLayers, result);
            AddLayers(descriptor.specialAnimationLayers, result);
            return result;
        }

        private static void AddLayers(
            VRCAvatarDescriptor.CustomAnimLayer[] layers,
            IDictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController> result)
        {
            foreach (var layer in layers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
            {
                var controller = GetBaseController(layer.animatorController);
                if (controller != null)
                {
                    result[layer.type] = controller;
                }
            }
        }

        private static AnimatorController GetBaseController(RuntimeAnimatorController runtimeController)
        {
            var current = runtimeController;
            var visited = new HashSet<RuntimeAnimatorController>();
            while (current is AnimatorOverrideController overrideController
                   && visited.Add(current))
            {
                current = overrideController.runtimeAnimatorController;
            }
            return current as AnimatorController;
        }

        private static Dictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, int>>
            BuildTargetMap(
                IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController> controllers,
                PhantomBuildReport report)
        {
            var result = new Dictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, int>>();
            foreach (var pair in controllers)
            {
                var layers = new Dictionary<string, int>(StringComparer.Ordinal);
                var controllerLayers = pair.Value.layers;
                for (var index = 0; index < controllerLayers.Length; index++)
                {
                    if (!layers.TryAdd(controllerLayers[index].name, index)
                        && controllerLayers[index].name.StartsWith("PhantomSystem_", StringComparison.Ordinal))
                    {
                        report.Error(
                            $"Final {pair.Key} controller contains duplicate PhantomSystem layer name "
                            + $"'{controllerLayers[index].name}'. Animator Layer Control targets are ambiguous.",
                            pair.Value);
                    }
                }
                result[pair.Key] = layers;
            }
            return result;
        }

        private static void ProcessController(
            BuildContext context,
            AnimatorController controller,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, int>> targets,
            PhantomBuildReport report)
        {
            var processedMachines = new HashSet<AnimatorStateMachine>();
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine != null && processedMachines.Add(layer.stateMachine))
                {
                    ProcessStateMachine(context, layer.stateMachine, targets, report);
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

                foreach (var state in EnumerateStates(layers[syncedLayerIndex].stateMachine))
                {
                    var current = controller.GetStateEffectiveBehaviours(state, layerIndex);
                    var replacement = ReplaceMarkers(context, current, targets, report);
                    if (!ReferenceEquals(current, replacement))
                    {
                        controller.SetStateEffectiveBehaviours(state, layerIndex, replacement);
                    }
                }
            }
        }

        private static void ProcessStateMachine(
            BuildContext context,
            AnimatorStateMachine machine,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, int>> targets,
            PhantomBuildReport report)
        {
            machine.behaviours = ReplaceMarkers(context, machine.behaviours, targets, report);
            foreach (var child in machine.states)
            {
                if (child.state != null)
                {
                    child.state.behaviours = ReplaceMarkers(
                        context,
                        child.state.behaviours,
                        targets,
                        report);
                }
            }
            foreach (var child in machine.stateMachines)
            {
                if (child.stateMachine != null)
                {
                    ProcessStateMachine(context, child.stateMachine, targets, report);
                }
            }
        }

        private static StateMachineBehaviour[] ReplaceMarkers(
            BuildContext context,
            StateMachineBehaviour[] behaviours,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, int>> targets,
            PhantomBuildReport report)
        {
            var source = behaviours ?? Array.Empty<StateMachineBehaviour>();
            var replacement = new StateMachineBehaviour[source.Length];
            var changed = false;
            for (var index = 0; index < source.Length; index++)
            {
                if (!(source[index] is PhantomAnimatorLayerControlMarker marker))
                {
                    replacement[index] = source[index];
                    continue;
                }

                changed = true;
                replacement[index] = CreateLayerControl(context, marker, targets, report);
            }

            if (!changed)
            {
                return source;
            }

            var kept = new List<StateMachineBehaviour>(replacement.Length);
            foreach (var behaviour in replacement)
            {
                if (behaviour != null)
                {
                    kept.Add(behaviour);
                }
            }
            return kept.ToArray();
        }

        private static VRCAnimatorLayerControl CreateLayerControl(
            BuildContext context,
            PhantomAnimatorLayerControlMarker marker,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, int>> targets,
            PhantomBuildReport report)
        {
            if (!targets.TryGetValue(marker.targetPlayable, out var layers)
                || !layers.TryGetValue(marker.targetLayerName ?? string.Empty, out var layerIndex)
                || !TryConvertPlayable(marker.targetPlayable, out var playable))
            {
                report.Error(
                    $"Could not resolve Phantom Animator Layer Control target "
                    + $"'{marker.targetPlayable}/{marker.targetLayerName}'.",
                    marker);
                return null;
            }

            var control = ScriptableObject.CreateInstance<VRCAnimatorLayerControl>();
            control.name = "Phantom Animator Layer Control";
            control.playable = playable;
            control.layer = layerIndex;
            control.goalWeight = marker.goalWeight;
            control.blendDuration = marker.blendDuration;
            control.debugString = marker.debugString;
            context.AssetSaver.SaveAsset(control);
            return control;
        }

        private static bool TryConvertPlayable(
            VRCAvatarDescriptor.AnimLayerType playable,
            out VRC_AnimatorLayerControl.BlendableLayer result)
        {
            switch (playable)
            {
                case VRCAvatarDescriptor.AnimLayerType.FX:
                    result = VRC_AnimatorLayerControl.BlendableLayer.FX;
                    return true;
                case VRCAvatarDescriptor.AnimLayerType.Gesture:
                    result = VRC_AnimatorLayerControl.BlendableLayer.Gesture;
                    return true;
                case VRCAvatarDescriptor.AnimLayerType.Action:
                    result = VRC_AnimatorLayerControl.BlendableLayer.Action;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static void VerifyNoMarkers(
            VRCAvatarDescriptor.AnimLayerType playable,
            AnimatorController controller,
            PhantomBuildReport report)
        {
            var found = new HashSet<PhantomAnimatorLayerControlMarker>();
            foreach (var layer in controller.layers)
            {
                CollectMarkers(layer.stateMachine, found);
            }
            var layers = controller.layers;
            for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var syncedLayerIndex = layers[layerIndex].syncedLayerIndex;
                if (syncedLayerIndex < 0 || syncedLayerIndex >= layers.Length)
                {
                    continue;
                }
                foreach (var state in EnumerateStates(layers[syncedLayerIndex].stateMachine))
                {
                    AddMarkers(
                        controller.GetStateEffectiveBehaviours(state, layerIndex),
                        found);
                }
            }
            if (found.Count > 0)
            {
                report.Error(
                    $"Final {playable} controller still contains {found.Count} temporary Phantom Animator Layer Control marker(s).",
                    controller);
            }
        }

        private static void CollectMarkers(
            AnimatorStateMachine machine,
            ISet<PhantomAnimatorLayerControlMarker> found)
        {
            if (machine == null)
            {
                return;
            }
            AddMarkers(machine.behaviours, found);
            foreach (var child in machine.states)
            {
                if (child.state != null)
                {
                    AddMarkers(child.state.behaviours, found);
                }
            }
            foreach (var child in machine.stateMachines)
            {
                CollectMarkers(child.stateMachine, found);
            }
        }

        private static void AddMarkers(
            IEnumerable<StateMachineBehaviour> behaviours,
            ISet<PhantomAnimatorLayerControlMarker> found)
        {
            foreach (var marker in behaviours ?? Array.Empty<StateMachineBehaviour>())
            {
                if (marker is PhantomAnimatorLayerControlMarker typed)
                {
                    found.Add(typed);
                }
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
            foreach (var child in machine.stateMachines)
            {
                foreach (var state in EnumerateStates(child.stateMachine))
                {
                    yield return state;
                }
            }
        }
    }
}
