using System;
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorClipUtility;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorGraphUtility;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Maps converted tracking parameters to generated bone constraint component switches.</summary>
    internal static class PhantomTrackingControlAnimatorModule
    {
        private const string ConstraintEnabled = "m_Enabled";

        public static void Build(PhantomAnimatorBuildContext context)
        {
            var slot = context.Slot.Slot;
            if (slot == null
                || !context.Slot.HasTrackingControlConversion)
            {
                return;
            }

            foreach (var group in PhantomTrackingControlGroups.All)
            {
                AddFloatParameter(
                    context.Controller,
                    PhantomTrackingControlGroups.Parameter(slot, group),
                    1f);
            }
            var directWeightParameter = PhantomParameterNames.TrackingDirectWeight(slot);
            AddFloatParameter(context.Controller, directWeightParameter, 1f);

            var bindings = CollectBindings(context);
            if (bindings.Count == 0)
            {
                return;
            }

            var disabledClip = context.CreateClip("PhantomTrackingDisabled");
            foreach (var binding in bindings)
            {
                SetFloat(
                    disabledClip,
                    binding.Path,
                    binding.ConstraintType,
                    ConstraintEnabled,
                    true);
            }

            var directTree = CreateDirectTree(context, bindings, directWeightParameter);
            var layer = AddLayer(context.Controller, "PhantomTrackingControl");
            var machine = layer.stateMachine;
            var disabled = AddState(machine, disabledClip);
            var enabled = machine.AddState("PhantomTrackingEnabled");
            enabled.motion = directTree;
            machine.defaultState = disabled;

            AddTransition(
                disabled,
                enabled,
                BoolCondition(PhantomParameterNames.Activate(slot), true),
                BoolCondition(PhantomParameterNames.Freeze(slot), false));
            AddTransition(
                enabled,
                disabled,
                BoolCondition(PhantomParameterNames.Activate(slot), false));
            AddTransition(
                enabled,
                disabled,
                BoolCondition(PhantomParameterNames.Freeze(slot), true));
        }

        private static BlendTree CreateDirectTree(
            PhantomAnimatorBuildContext context,
            IReadOnlyList<ConstraintBinding> bindings,
            string directWeightParameter)
        {
            var direct = new BlendTree
            {
                name = "PhantomTrackingDirect",
                blendType = BlendTreeType.Direct,
                useAutomaticThresholds = false
            };
            context.RegisterBlendTree(direct);

            var includedGroups = new HashSet<PhantomTrackingControlGroup>();
            foreach (var binding in bindings)
            {
                includedGroups.Add(binding.Group);
            }

            foreach (var group in PhantomTrackingControlGroups.All)
            {
                if (!includedGroups.Contains(group))
                {
                    continue;
                }

                var parameter = PhantomTrackingControlGroups.Parameter(
                    context.Slot.Slot,
                    group);
                var offClip = context.CreateClip($"PhantomTracking{group}Off");
                var onClip = context.CreateClip($"PhantomTracking{group}On");
                foreach (var binding in bindings)
                {
                    if (binding.Group != group)
                    {
                        continue;
                    }

                    SetFloat(offClip, binding.Path, binding.ConstraintType, ConstraintEnabled, false);
                    SetFloat(onClip, binding.Path, binding.ConstraintType, ConstraintEnabled, true);
                }

                var groupTree = context.CreateBlendTree(
                    $"PhantomTracking{group}Tree",
                    parameter);
                groupTree.AddChild(offClip, 0f);
                groupTree.AddChild(onClip, 1f);
                direct.AddChild(groupTree, 1f);
                var children = direct.children;
                children[children.Length - 1].directBlendParameter = directWeightParameter;
                direct.children = children;
            }

            return direct;
        }

        private static List<ConstraintBinding> CollectBindings(PhantomAnimatorBuildContext context)
        {
            var bindings = new List<ConstraintBinding>();
            foreach (var group in PhantomTrackingControlGroups.All)
            {
                foreach (var bone in PhantomTrackingControlGroups.Bones(group))
                {
                    if (!context.Slot.CloneBoneAvatarPaths.TryGetValue(bone, out var path)
                        || !context.Slot.CloneBoneConstraintTypes.TryGetValue(bone, out var constraintType))
                    {
                        continue;
                    }

                    bindings.Add(new ConstraintBinding(group, path, constraintType));
                }
            }

            return bindings;
        }

        private readonly struct ConstraintBinding
        {
            public readonly PhantomTrackingControlGroup Group;
            public readonly string Path;
            public readonly Type ConstraintType;

            public ConstraintBinding(
                PhantomTrackingControlGroup group,
                string path,
                Type constraintType)
            {
                Group = group;
                Path = path;
                ConstraintType = constraintType;
            }
        }
    }
}
