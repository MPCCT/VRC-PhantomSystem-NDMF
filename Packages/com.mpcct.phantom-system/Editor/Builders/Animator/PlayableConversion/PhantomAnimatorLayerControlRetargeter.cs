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
    /// <summary>
    /// Resolves build-only layer-control markers while NDMF still owns stable virtual layer identities.
    /// </summary>
    internal static class PhantomAnimatorLayerControlRetargeter
    {
        private static readonly VRCAvatarDescriptor.AnimLayerType[] BlendablePlayables =
        {
            VRCAvatarDescriptor.AnimLayerType.FX,
            VRCAvatarDescriptor.AnimLayerType.Gesture,
            VRCAvatarDescriptor.AnimLayerType.Action,
            VRCAvatarDescriptor.AnimLayerType.Additive
        };

        public static void RetargetVirtual(BuildContext context, PhantomBuildState state)
        {
            if (state == null || !state.HasWork)
            {
                return;
            }

            var controllerContext = context.Extension<AnimatorServicesContext>().ControllerContext;
            var controllers = CollectVirtualControllers(controllerContext);
            var targets = BuildVirtualTargetMap(controllers, state.Report, state.System.AuthoringComponent);
            DisableConvertedActionLayersVirtual(state, controllers, targets);

            var processedControllers = new HashSet<VirtualAnimatorController>();
            foreach (var controller in controllers.Values)
            {
                if (controller != null && processedControllers.Add(controller))
                {
                    ProcessVirtualController(controller, targets, state.Report, state.System.AuthoringComponent);
                }
            }

            foreach (var pair in controllers)
            {
                VerifyNoVirtualMarkers(pair.Key, pair.Value, state.Report, state.System.AuthoringComponent);
            }
        }

        public static void VerifyFinal(BuildContext context, PhantomBuildState state)
        {
            if (state == null || !state.HasWork)
            {
                return;
            }

            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                state.Report.Error(
                    "Cannot validate Phantom Animator Layer Controls because the final avatar descriptor is missing.");
                return;
            }

            foreach (var pair in CollectFinalControllers(descriptor))
            {
                VerifyNoFinalMarkers(pair.Key, pair.Value, state.Report);
            }
        }

        private static Dictionary<VRCAvatarDescriptor.AnimLayerType, VirtualAnimatorController>
            CollectVirtualControllers(VirtualControllerContext controllerContext)
        {
            var result = new Dictionary<VRCAvatarDescriptor.AnimLayerType, VirtualAnimatorController>();
            foreach (var playable in BlendablePlayables)
            {
                if (controllerContext.Controllers.TryGetValue(playable, out var controller)
                    && controller != null)
                {
                    result[playable] = controller;
                }
            }
            return result;
        }

        private static Dictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, VirtualLayer>>
            BuildVirtualTargetMap(
                IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, VirtualAnimatorController> controllers,
                PhantomBuildReport report,
                UnityEngine.Object context)
        {
            var result =
                new Dictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, VirtualLayer>>();
            foreach (var pair in controllers)
            {
                var layers = new Dictionary<string, VirtualLayer>(StringComparer.Ordinal);
                foreach (var layer in pair.Value.Layers)
                {
                    if (!layers.TryAdd(layer.Name, layer)
                        && layer.Name.StartsWith("PhantomSystem_", StringComparison.Ordinal))
                    {
                        report.InternalError(
                            $"Merged {pair.Key} controller contains duplicate PhantomSystem layer name "
                            + $"'{layer.Name}'. Animator Layer Control targets are ambiguous.",
                            context);
                    }
                }
                result[pair.Key] = layers;
            }
            return result;
        }

        private static void DisableConvertedActionLayersVirtual(
            PhantomBuildState state,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, VirtualAnimatorController> controllers,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, VirtualLayer>> targets)
        {
            if (!state.System.Slots.Any(slot => slot.ConvertedActionLayers.Count > 0))
            {
                return;
            }

            var targetPlayable = PhantomSourceIntegrationBuilder.ResolveMergeTarget(
                VRCAvatarDescriptor.AnimLayerType.Action);
            if (!controllers.TryGetValue(targetPlayable, out var targetController)
                || !targets.TryGetValue(targetPlayable, out var targetLayers))
            {
                state.Report.Error(
                    $"Cannot disable Converted Action layers because the merged {targetPlayable} controller is missing.");
                return;
            }

            var orderedLayers = targetController.Layers.ToArray();
            foreach (var slot in state.System.Slots)
            {
                foreach (var actionLayer in slot.ConvertedActionLayers)
                {
                    if (!targetLayers.TryGetValue(actionLayer.LayerName, out var layer))
                    {
                        state.Report.Error(
                            $"Could not resolve Converted Action layer '{actionLayer.LayerName}' in the merged "
                            + $"{targetPlayable} controller.",
                            state.System.AuthoringComponent);
                        continue;
                    }

                    if (orderedLayers.Length > 0 && ReferenceEquals(orderedLayers[0], layer))
                    {
                        state.Report.Error(
                            $"Converted Action layer '{actionLayer.LayerName}' became merged {targetPlayable} "
                            + "layer 0 and cannot be weight-controlled.",
                            state.System.AuthoringComponent);
                        continue;
                    }

                    layer.DefaultWeight = 0f;
                }
            }
        }

        private static void ProcessVirtualController(
            VirtualAnimatorController controller,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, VirtualLayer>> targets,
            PhantomBuildReport report,
            UnityEngine.Object context)
        {
            var layers = controller.Layers.ToArray();
            var processedStateMachines = new HashSet<VirtualStateMachine>();
            foreach (var layer in layers)
            {
                if (layer.StateMachine != null && processedStateMachines.Add(layer.StateMachine))
                {
                    ProcessVirtualStateMachine(layer.StateMachine, targets, report, context);
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
                    overrides[pair.Key] = ReplaceVirtualMarkers(pair.Value, targets, report, context);
                }
                layer.SyncedLayerBehaviourOverrides = overrides.ToImmutable();
            }
        }

        private static void ProcessVirtualStateMachine(
            VirtualStateMachine machine,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, VirtualLayer>> targets,
            PhantomBuildReport report,
            UnityEngine.Object context)
        {
            machine.Behaviours = ReplaceVirtualMarkers(machine.Behaviours, targets, report, context);
            foreach (var child in machine.States)
            {
                if (child.State != null)
                {
                    child.State.Behaviours = ReplaceVirtualMarkers(
                        child.State.Behaviours,
                        targets,
                        report,
                        context);
                }
            }
            foreach (var child in machine.StateMachines)
            {
                if (child.StateMachine != null)
                {
                    ProcessVirtualStateMachine(child.StateMachine, targets, report, context);
                }
            }
        }

        private static ImmutableList<StateMachineBehaviour> ReplaceVirtualMarkers(
            IEnumerable<StateMachineBehaviour> behaviours,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, VirtualLayer>> targets,
            PhantomBuildReport report,
            UnityEngine.Object context)
        {
            var replacement = ImmutableList.CreateBuilder<StateMachineBehaviour>();
            foreach (var behaviour in behaviours ?? Enumerable.Empty<StateMachineBehaviour>())
            {
                if (!(behaviour is PhantomAnimatorLayerControlMarker marker))
                {
                    replacement.Add(behaviour);
                    continue;
                }

                var control = CreateVirtualLayerControl(marker, targets, report, context);
                if (control != null)
                {
                    replacement.Add(control);
                }
            }
            return replacement.ToImmutable();
        }

        private static VRCAnimatorLayerControl CreateVirtualLayerControl(
            PhantomAnimatorLayerControlMarker marker,
            IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, Dictionary<string, VirtualLayer>> targets,
            PhantomBuildReport report,
            UnityEngine.Object context)
        {
            if (!targets.TryGetValue(marker.targetPlayable, out var layers)
                || !layers.TryGetValue(marker.targetLayerName ?? string.Empty, out var targetLayer)
                || !TryConvertPlayable(marker.targetPlayable, out var playable))
            {
                report.InternalError(
                    "Could not resolve Phantom Animator Layer Control target before VRCFury processing: "
                    + $"'{marker.targetPlayable}/{marker.targetLayerName}'.",
                    context);
                return null;
            }

            var control = ScriptableObject.CreateInstance<VRCAnimatorLayerControl>();
            control.name = "Phantom Animator Layer Control";
            control.playable = playable;
            control.layer = targetLayer.VirtualLayerIndex;
            control.goalWeight = marker.goalWeight;
            control.blendDuration = marker.blendDuration;
            control.debugString = marker.debugString;
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
                case VRCAvatarDescriptor.AnimLayerType.Additive:
                    result = VRC_AnimatorLayerControl.BlendableLayer.Additive;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static void VerifyNoVirtualMarkers(
            VRCAvatarDescriptor.AnimLayerType playable,
            VirtualAnimatorController controller,
            PhantomBuildReport report,
            UnityEngine.Object context)
        {
            var found = new HashSet<PhantomAnimatorLayerControlMarker>();
            foreach (var layer in controller.Layers)
            {
                CollectVirtualMarkers(layer.StateMachine, found);
                foreach (var behaviours in layer.SyncedLayerBehaviourOverrides.Values)
                {
                    AddMarkers(behaviours, found);
                }
            }
            if (found.Count > 0)
            {
                report.InternalError(
                    $"Merged {playable} controller still contains {found.Count} temporary Phantom Animator "
                    + "Layer Control marker(s) before VRCFury processing.",
                    context);
            }
        }

        private static void CollectVirtualMarkers(
            VirtualStateMachine machine,
            ISet<PhantomAnimatorLayerControlMarker> found)
        {
            if (machine == null)
            {
                return;
            }
            AddMarkers(machine.Behaviours, found);
            foreach (var child in machine.States)
            {
                if (child.State != null)
                {
                    AddMarkers(child.State.Behaviours, found);
                }
            }
            foreach (var child in machine.StateMachines)
            {
                CollectVirtualMarkers(child.StateMachine, found);
            }
        }

        private static Dictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController>
            CollectFinalControllers(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController>();
            AddFinalLayers(descriptor.baseAnimationLayers, result);
            AddFinalLayers(descriptor.specialAnimationLayers, result);
            return result;
        }

        private static void AddFinalLayers(
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

        private static void VerifyNoFinalMarkers(
            VRCAvatarDescriptor.AnimLayerType playable,
            AnimatorController controller,
            PhantomBuildReport report)
        {
            var found = new HashSet<PhantomAnimatorLayerControlMarker>();
            foreach (var layer in controller.layers)
            {
                CollectFinalMarkers(layer.stateMachine, found);
            }
            var layers = controller.layers;
            for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var syncedLayerIndex = layers[layerIndex].syncedLayerIndex;
                if (syncedLayerIndex < 0 || syncedLayerIndex >= layers.Length)
                {
                    continue;
                }
                foreach (var state in EnumerateFinalStates(layers[syncedLayerIndex].stateMachine))
                {
                    AddMarkers(controller.GetStateEffectiveBehaviours(state, layerIndex), found);
                }
            }
            if (found.Count > 0)
            {
                report.InternalError(
                    $"Final {playable} controller still contains {found.Count} temporary Phantom Animator "
                    + "Layer Control marker(s).",
                    controller);
            }
        }

        private static void CollectFinalMarkers(
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
                CollectFinalMarkers(child.stateMachine, found);
            }
        }

        private static void AddMarkers(
            IEnumerable<StateMachineBehaviour> behaviours,
            ISet<PhantomAnimatorLayerControlMarker> found)
        {
            foreach (var marker in behaviours ?? Enumerable.Empty<StateMachineBehaviour>())
            {
                if (marker is PhantomAnimatorLayerControlMarker typed)
                {
                    found.Add(typed);
                }
            }
        }

        private static IEnumerable<AnimatorState> EnumerateFinalStates(AnimatorStateMachine machine)
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
                foreach (var state in EnumerateFinalStates(child.stateMachine))
                {
                    yield return state;
                }
            }
        }
    }
}
