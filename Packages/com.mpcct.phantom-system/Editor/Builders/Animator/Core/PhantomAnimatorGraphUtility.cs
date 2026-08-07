using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Provides shared AnimatorController graph construction helpers.</summary>
    internal static class PhantomAnimatorGraphUtility
    {
        public static AnimatorControllerLayer AddLayer(AnimatorController controller, string name)
        {
            controller.AddLayer(name);
            var layers = controller.layers;
            var layer = layers[layers.Length - 1];
            layer.name = name;
            layer.defaultWeight = 1f;
            layer.stateMachine.name = name;
            controller.layers = layers;
            return layer;
        }

        public static AnimatorState AddState(AnimatorStateMachine machine, AnimationClip motion)
        {
            var state = machine.AddState(motion.name);
            state.motion = motion;
            return state;
        }

        public static void AddAnyStateTransition(
            AnimatorStateMachine machine,
            AnimatorState to,
            params TransitionCondition[] conditions)
        {
            var transition = machine.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.canTransitionToSelf = false;
            foreach (var condition in conditions)
            {
                transition.AddCondition(condition.Mode, condition.Threshold, condition.Parameter);
            }
        }

        public static void AddBoolParameter(
            AnimatorController controller,
            string name,
            bool defaultValue)
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = defaultValue
            });
        }

        public static void AddIntParameter(
            AnimatorController controller,
            string name,
            int defaultValue)
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Int,
                defaultInt = defaultValue
            });
        }

        public static void AddFloatParameter(
            AnimatorController controller,
            string name,
            float defaultValue)
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = defaultValue
            });
        }

        public static void AddTransition(
            AnimatorState from,
            AnimatorState to,
            params TransitionCondition[] conditions)
        {
            AddTransition(from, to, 0f, conditions);
        }

        public static void AddTransition(
            AnimatorState from,
            AnimatorState to,
            float duration,
            params TransitionCondition[] conditions)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            foreach (var condition in conditions)
            {
                transition.AddCondition(condition.Mode, condition.Threshold, condition.Parameter);
            }
        }

        public static void AddExitTransition(
            AnimatorState from,
            AnimatorState to,
            float exitTime)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.hasFixedDuration = true;
            transition.exitTime = exitTime;
            transition.duration = 0f;
        }

        public static TransitionCondition BoolCondition(string parameter, bool expected)
        {
            return new TransitionCondition(
                parameter,
                expected ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0f);
        }

        public static TransitionCondition IntEqualsCondition(string parameter, int value)
        {
            return new TransitionCondition(parameter, AnimatorConditionMode.Equals, value);
        }

        public static TransitionCondition IntNotEqualCondition(string parameter, int value)
        {
            return new TransitionCondition(parameter, AnimatorConditionMode.NotEqual, value);
        }

        public static void AddSetBoolParameterDriver(
            PhantomAnimatorBuildContext context,
            AnimatorState state,
            string parameter,
            bool value,
            bool localOnly = false)
        {
            var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            if (driver == null)
            {
                driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
                driver.name = $"Set {parameter}";
                AppendStateMachineBehaviour(state, driver);
                context.NdmfContext.AssetSaver.SaveAsset(driver);
            }

            driver.localOnly = localOnly;
            if (driver.parameters == null)
            {
                driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>();
            }

            driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type = VRC_AvatarParameterDriver.ChangeType.Set,
                name = parameter,
                value = value ? 1f : 0f
            });
        }

        public static void AddSetFloatParameterDriver(
            PhantomAnimatorBuildContext context,
            AnimatorState state,
            string parameter,
            float value)
        {
            var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            if (driver == null)
            {
                driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
                driver.name = $"Set {parameter}";
                AppendStateMachineBehaviour(state, driver);
                context.NdmfContext.AssetSaver.SaveAsset(driver);
            }

            driver.localOnly = false;
            if (driver.parameters == null)
            {
                driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>();
            }

            driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type = VRC_AvatarParameterDriver.ChangeType.Set,
                name = parameter,
                value = value
            });
        }

        public static void ValidateStateMotions(PhantomAnimatorBuildContext context)
        {
            foreach (var layer in context.Controller.layers)
            {
                ValidateStateMachine(context, layer.name, layer.stateMachine);
            }
        }

        private static void ValidateStateMachine(
            PhantomAnimatorBuildContext context,
            string layerName,
            AnimatorStateMachine machine)
        {
            foreach (var childState in machine.states)
            {
                if (childState.state != null && childState.state.motion == null)
                {
                    context.Report.Error(
                        $"Generated Animator layer '{layerName}' contains state "
                        + $"'{childState.state.name}' without a Motion.",
                        context.Controller);
                }
            }

            foreach (var childMachine in machine.stateMachines)
            {
                if (childMachine.stateMachine != null)
                {
                    ValidateStateMachine(context, layerName, childMachine.stateMachine);
                }
            }
        }

        private static void AppendStateMachineBehaviour(
            AnimatorState state,
            StateMachineBehaviour behaviour)
        {
            var oldBehaviours = state.behaviours;
            var oldLength = oldBehaviours == null ? 0 : oldBehaviours.Length;
            var newBehaviours = new StateMachineBehaviour[oldLength + 1];
            for (var index = 0; index < oldLength; index++)
            {
                newBehaviours[index] = oldBehaviours[index];
            }

            newBehaviours[oldLength] = behaviour;
            state.behaviours = newBehaviours;
        }

        internal readonly struct TransitionCondition
        {
            public readonly string Parameter;
            public readonly AnimatorConditionMode Mode;
            public readonly float Threshold;

            public TransitionCondition(
                string parameter,
                AnimatorConditionMode mode,
                float threshold)
            {
                Parameter = parameter;
                Mode = mode;
                Threshold = threshold;
            }
        }
    }
}
