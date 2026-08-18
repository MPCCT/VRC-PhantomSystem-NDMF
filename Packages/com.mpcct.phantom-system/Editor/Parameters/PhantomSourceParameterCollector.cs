using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Collects parameter declarations and references from one avatar hierarchy.</summary>
    internal static class PhantomSourceParameterCollector
    {
        [ThreadStatic]
        private static int providerSuppressionDepth;

        public static bool IsProviderSuppressed => providerSuppressionDepth > 0;

        public static Dictionary<string, PhantomParameterDefinition> ReadBaseParameters(
            GameObject avatarRoot,
            BuildContext context)
        {
            return ReadParameters(avatarRoot, context, true);
        }

        public static Dictionary<string, PhantomParameterDefinition> ReadParametersForObject(
            GameObject root,
            BuildContext context)
        {
            return ReadParameters(root, context, true);
        }

        public static List<PhantomParameterDefinition> ReadDynamicParameterPrefixes(GameObject root)
        {
            if (root == null)
            {
                return new List<PhantomParameterDefinition>();
            }

            var result = root.GetComponentsInChildren<VRCPhysBone>(true)
                .Select(physBone => physBone.parameter)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Select(name => new PhantomParameterDefinition
                {
                    Name = name,
                    IsPhysBonePrefix = true,
                    IsAnimatorOnly = true,
                    WantSynced = false
                })
                .ToList();

            result.AddRange(root.GetComponentsInChildren<VRCRaycast>(true)
                .Select(raycast => raycast.Parameter)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Select(name => new PhantomParameterDefinition
                {
                    Name = name,
                    IsRaycastPrefix = true,
                    IsAnimatorOnly = true,
                    WantSynced = false
                }));

            return result
                .GroupBy(parameter => parameter.Name, StringComparer.Ordinal)
                .Select(group => group.Aggregate((combined, next) =>
                {
                    combined.IsPhysBonePrefix |= next.IsPhysBonePrefix;
                    combined.IsRaycastPrefix |= next.IsRaycastPrefix;
                    return combined;
                }))
                .ToList();
        }

        public static PhantomSourceParameterCollection Collect(
            GameObject root,
            BuildContext context)
        {
            var collection = new PhantomSourceParameterCollection();
            if (root == null)
            {
                return collection;
            }

            var definitions = ReadParameters(root, context, true);
            var retainedDefinitions =
                new Dictionary<string, PhantomParameterDefinition>(StringComparer.Ordinal);
            foreach (var prefix in ReadDynamicParameterPrefixes(root))
            {
                MergeDefinition(definitions, prefix);
            }

            foreach (var contact in root.GetComponentsInChildren<VRCContactReceiver>(true))
            {
                AddRetainedReference(
                    definitions,
                    retainedDefinitions,
                    collection.RetainedSourceParameterNames,
                    contact.parameter,
                    AnimatorControllerParameterType.Bool,
                    contact);
            }

            foreach (var physBone in root.GetComponentsInChildren<VRCPhysBone>(true))
            {
                AddRetainedPrefix(
                    definitions,
                    retainedDefinitions,
                    collection.RetainedSourceParameterNames,
                    physBone.parameter,
                    true,
                    false,
                    physBone);
            }

            foreach (var raycast in root.GetComponentsInChildren<VRCRaycast>(true))
            {
                AddRetainedPrefix(
                    definitions,
                    retainedDefinitions,
                    collection.RetainedSourceParameterNames,
                    raycast.Parameter,
                    false,
                    true,
                    raycast);
            }

            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null)
            {
                foreach (var playable in new[]
                         {
                             VRCAvatarDescriptor.AnimLayerType.FX,
                             VRCAvatarDescriptor.AnimLayerType.Gesture,
                             VRCAvatarDescriptor.AnimLayerType.Action
                         })
                {
                    if (PhantomSourcePlayableControllerUtility.TryGetLayer(
                            descriptor,
                            playable,
                            out var layer)
                        && !layer.IsDefault
                        && TryGetBaseController(layer.Controller, out var controller))
                    {
                        CollectControllerParameters(controller, definitions);
                    }
                }

                CollectMenuParameters(descriptor.expressionsMenu, definitions);
            }

            collection.Definitions = definitions.Values
                .Where(parameter => parameter != null
                                    && !string.IsNullOrWhiteSpace(parameter.Name)
                                    && !PhantomParameterPolicy.IsVrcReserved(parameter.Name))
                .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
                .ToList();
            collection.RetainedDefinitions = retainedDefinitions.Values
                .Where(parameter => !PhantomParameterPolicy.IsVrcReserved(parameter.Name))
                .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
                .ToList();
            return collection;
        }

        public static Dictionary<string, PhantomParameterDefinition> ReadDescriptorParameters(
            VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<string, PhantomParameterDefinition>(StringComparer.Ordinal);
            var parameters = descriptor != null ? descriptor.expressionParameters?.parameters : null;
            if (parameters == null)
            {
                return result;
            }

            foreach (var parameter in parameters)
            {
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.name))
                {
                    continue;
                }

                result[parameter.name] = new PhantomParameterDefinition
                {
                    Name = parameter.name,
                    ParameterType = ConvertType(parameter.valueType),
                    IsAnimatorOnly = false,
                    IsHidden = false,
                    WantSynced = parameter.networkSynced,
                    DefaultValue = parameter.defaultValue,
                    Saved = parameter.saved,
                    SourceComponent = descriptor
                };
            }

            return result;
        }

        private static Dictionary<string, PhantomParameterDefinition> ReadParameters(
            GameObject root,
            BuildContext context,
            bool suppressPhantomProvider)
        {
            var result = new Dictionary<string, PhantomParameterDefinition>(StringComparer.Ordinal);
            if (root == null)
            {
                return result;
            }

            if (suppressPhantomProvider)
            {
                providerSuppressionDepth++;
            }

            try
            {
                var info = context != null
                    ? ParameterInfo.ForContext(context)
                    : ParameterInfo.ForPreview(ComputeContext.NullContext);
                foreach (var parameter in info.GetParametersForObject(root))
                {
                    if (parameter == null
                        || parameter.Namespace != ParameterNamespace.Animator
                        || string.IsNullOrWhiteSpace(parameter.EffectiveName))
                    {
                        continue;
                    }

                    result[parameter.EffectiveName] = new PhantomParameterDefinition
                    {
                        Name = parameter.EffectiveName,
                        ParameterType = parameter.ParameterType,
                        IsAnimatorOnly = parameter.IsAnimatorOnly,
                        IsHidden = parameter.IsHidden,
                        WantSynced = parameter.WantSynced,
                        DefaultValue = parameter.DefaultValue,
                        SourceComponent = parameter.Source,
                        SourcePlugin = parameter.Plugin
                    };
                }

                var descriptorDefinitions = ReadDescriptorParameters(
                    root.GetComponent<VRCAvatarDescriptor>());
                foreach (var pair in descriptorDefinitions)
                {
                    if (!result.TryGetValue(pair.Key, out var definition))
                    {
                        continue;
                    }

                    definition.Saved = pair.Value.Saved;
                    definition.DefaultValue = pair.Value.DefaultValue;
                }
            }
            finally
            {
                if (suppressPhantomProvider)
                {
                    providerSuppressionDepth--;
                }
            }

            return result;
        }

        internal static void CollectControllerParameters(
            AnimatorController controller,
            IDictionary<string, PhantomParameterDefinition> definitions)
        {
            if (controller == null)
            {
                return;
            }

            foreach (var parameter in controller.parameters)
            {
                MergeDefinition(definitions, new PhantomParameterDefinition
                {
                    Name = parameter.name,
                    ParameterType = parameter.type,
                    IsAnimatorOnly = true,
                    WantSynced = false,
                    DefaultValue = DefaultValue(parameter)
                });
            }

            // This also covers behaviours stored as synced-layer overrides, which are
            // not necessarily reachable from the source state machine's behaviour array.
            CollectBehaviourParameters(
                controller.GetBehaviours<StateMachineBehaviour>(),
                definitions);

            var visitedStateMachines = new HashSet<AnimatorStateMachine>();
            var visitedMotions = new HashSet<Motion>();
            foreach (var layer in controller.layers)
            {
                CollectStateMachineParameterReferences(
                    layer.stateMachine,
                    definitions,
                    visitedStateMachines,
                    visitedMotions);
            }
        }

        private static void CollectStateMachineParameterReferences(
            AnimatorStateMachine machine,
            IDictionary<string, PhantomParameterDefinition> definitions,
            ISet<AnimatorStateMachine> visitedStateMachines,
            ISet<Motion> visitedMotions)
        {
            if (machine == null || !visitedStateMachines.Add(machine))
            {
                return;
            }

            CollectTransitionParameters(machine.anyStateTransitions, definitions);
            CollectTransitionParameters(machine.entryTransitions, definitions);
            CollectBehaviourParameters(machine.behaviours, definitions);
            foreach (var child in machine.states)
            {
                var state = child.state;
                if (state == null)
                {
                    continue;
                }

                CollectTransitionParameters(state.transitions, definitions);
                CollectMotionParameters(state.motion, definitions, visitedMotions);
                CollectBehaviourParameters(state.behaviours, definitions);
                AddControllerReference(definitions, state.mirrorParameter, AnimatorControllerParameterType.Bool);
                AddControllerReference(definitions, state.speedParameter, AnimatorControllerParameterType.Float);
                AddControllerReference(definitions, state.timeParameter, AnimatorControllerParameterType.Float);
                AddControllerReference(definitions, state.cycleOffsetParameter, AnimatorControllerParameterType.Float);
            }

            foreach (var child in machine.stateMachines)
            {
                CollectStateMachineParameterReferences(
                    child.stateMachine,
                    definitions,
                    visitedStateMachines,
                    visitedMotions);
            }
        }

        private static void CollectTransitionParameters<TTransition>(
            IEnumerable<TTransition> transitions,
            IDictionary<string, PhantomParameterDefinition> definitions)
            where TTransition : AnimatorTransitionBase
        {
            foreach (var transition in transitions ?? Enumerable.Empty<TTransition>())
            {
                if (transition == null)
                {
                    continue;
                }

                foreach (var condition in transition.conditions)
                {
                    AddControllerReference(definitions, condition.parameter, null);
                }
            }
        }

        private static void CollectMotionParameters(
            Motion motion,
            IDictionary<string, PhantomParameterDefinition> definitions,
            ISet<Motion> visitedMotions)
        {
            if (!(motion is BlendTree tree) || !visitedMotions.Add(motion))
            {
                return;
            }

            AddControllerReference(definitions, tree.blendParameter, AnimatorControllerParameterType.Float);
            AddControllerReference(definitions, tree.blendParameterY, AnimatorControllerParameterType.Float);
            foreach (var child in tree.children)
            {
                AddControllerReference(
                    definitions,
                    child.directBlendParameter,
                    AnimatorControllerParameterType.Float);
                CollectMotionParameters(child.motion, definitions, visitedMotions);
            }
        }

        private static void CollectBehaviourParameters(
            IEnumerable<StateMachineBehaviour> behaviours,
            IDictionary<string, PhantomParameterDefinition> definitions)
        {
            foreach (var behaviour in behaviours ?? Enumerable.Empty<StateMachineBehaviour>())
            {
                if (behaviour is VRCAnimatorPlayAudio playAudio)
                {
                    AddControllerReference(
                        definitions,
                        playAudio.ParameterName,
                        AnimatorControllerParameterType.Int);
                }

                if (!(behaviour is VRC_AvatarParameterDriver driver) || driver.parameters == null)
                {
                    continue;
                }

                foreach (var parameter in driver.parameters)
                {
                    AddControllerReference(definitions, parameter.name, null);
                    AddControllerReference(definitions, parameter.source, null);
                }
            }
        }

        private static void CollectMenuParameters(
            VRCExpressionsMenu menu,
            IDictionary<string, PhantomParameterDefinition> definitions)
        {
            var visited = new HashSet<VRCExpressionsMenu>();
            CollectMenuParameters(menu, definitions, visited);
        }

        private static void CollectMenuParameters(
            VRCExpressionsMenu menu,
            IDictionary<string, PhantomParameterDefinition> definitions,
            ISet<VRCExpressionsMenu> visited)
        {
            if (menu == null || !visited.Add(menu) || menu.controls == null)
            {
                return;
            }

            foreach (var control in menu.controls)
            {
                if (control == null)
                {
                    continue;
                }

                AnimatorControllerParameterType? mainType =
                    control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet
                        ? AnimatorControllerParameterType.Float
                        : (AnimatorControllerParameterType?)null;
                AddControllerReference(definitions, control.parameter?.name, mainType);
                if (control.subParameters != null)
                {
                    foreach (var parameter in control.subParameters)
                    {
                        AddControllerReference(
                            definitions,
                            parameter?.name,
                            AnimatorControllerParameterType.Float);
                    }
                }

                CollectMenuParameters(control.subMenu, definitions, visited);
            }
        }

        private static void AddRetainedReference(
            IDictionary<string, PhantomParameterDefinition> definitions,
            IDictionary<string, PhantomParameterDefinition> retainedDefinitions,
            ISet<string> retainedNames,
            string name,
            AnimatorControllerParameterType type,
            Component source)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            retainedNames.Add(name);
            var definition = new PhantomParameterDefinition
            {
                Name = name,
                ParameterType = type,
                IsAnimatorOnly = true,
                WantSynced = false,
                SourceComponent = source
            };
            MergeDefinition(definitions, definition);
            MergeDefinition(retainedDefinitions, CloneDefinition(definition));
        }

        private static void AddRetainedPrefix(
            IDictionary<string, PhantomParameterDefinition> definitions,
            IDictionary<string, PhantomParameterDefinition> retainedDefinitions,
            ISet<string> retainedNames,
            string name,
            bool isPhysBonePrefix,
            bool isRaycastPrefix,
            Component source)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            retainedNames.Add(name);
            var definition = new PhantomParameterDefinition
            {
                Name = name,
                IsAnimatorOnly = true,
                WantSynced = false,
                IsPhysBonePrefix = isPhysBonePrefix,
                IsRaycastPrefix = isRaycastPrefix,
                SourceComponent = source
            };
            MergeDefinition(definitions, definition);
            MergeDefinition(retainedDefinitions, CloneDefinition(definition));
        }

        private static void AddControllerReference(
            IDictionary<string, PhantomParameterDefinition> definitions,
            string name,
            AnimatorControllerParameterType? type)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            MergeDefinition(definitions, new PhantomParameterDefinition
            {
                Name = name,
                ParameterType = type,
                IsAnimatorOnly = true,
                WantSynced = false
            });
        }

        private static void MergeDefinition(
            IDictionary<string, PhantomParameterDefinition> definitions,
            PhantomParameterDefinition incoming)
        {
            if (incoming == null || string.IsNullOrWhiteSpace(incoming.Name))
            {
                return;
            }

            if (!definitions.TryGetValue(incoming.Name, out var existing))
            {
                definitions[incoming.Name] = incoming;
                return;
            }

            if (!existing.ParameterType.HasValue && incoming.ParameterType.HasValue)
            {
                existing.ParameterType = incoming.ParameterType;
            }
            existing.IsPhysBonePrefix |= incoming.IsPhysBonePrefix;
            existing.IsRaycastPrefix |= incoming.IsRaycastPrefix;
            if (existing.SourceComponent == null)
            {
                existing.SourceComponent = incoming.SourceComponent;
            }
        }

        private static PhantomParameterDefinition CloneDefinition(
            PhantomParameterDefinition source)
        {
            return new PhantomParameterDefinition
            {
                Name = source.Name,
                ParameterType = source.ParameterType,
                IsAnimatorOnly = source.IsAnimatorOnly,
                IsHidden = source.IsHidden,
                IsPhysBonePrefix = source.IsPhysBonePrefix,
                IsRaycastPrefix = source.IsRaycastPrefix,
                WantSynced = source.WantSynced,
                DefaultValue = source.DefaultValue,
                Saved = source.Saved,
                SourceComponent = source.SourceComponent,
                SourcePlugin = source.SourcePlugin
            };
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

        private static float DefaultValue(AnimatorControllerParameter parameter)
        {
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Bool:
                case AnimatorControllerParameterType.Trigger:
                    return parameter.defaultBool ? 1f : 0f;
                case AnimatorControllerParameterType.Int:
                    return parameter.defaultInt;
                default:
                    return parameter.defaultFloat;
            }
        }

        private static AnimatorControllerParameterType ConvertType(VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    return AnimatorControllerParameterType.Bool;
                case VRCExpressionParameters.ValueType.Int:
                    return AnimatorControllerParameterType.Int;
                case VRCExpressionParameters.ValueType.Float:
                    return AnimatorControllerParameterType.Float;
                default:
                    return AnimatorControllerParameterType.Trigger;
            }
        }
    }
}
