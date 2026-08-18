using System;
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorClipUtility;
using static MPCCT.PhantomSystem.Editor.PhantomAnimatorGraphUtility;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Maps converted tracking parameters to base/animation constraint source weights.</summary>
    internal static class PhantomTrackingControlAnimatorModule
    {
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
                AddCoreParameter(
                    context.Controller,
                    slot,
                    PhantomTrackingControlGroups.Parameter(slot, group));
            }
            var directWeightParameter = PhantomParameterNames.TrackingDirectWeight(slot);
            AddCoreParameter(context.Controller, slot, directWeightParameter);

            var bindings = CollectBindings(context);
            if (bindings.Count == 0)
            {
                return;
            }

            var enabledTree = CreateEnabledDirectTree(context, bindings, directWeightParameter);
            var layer = AddLayer(context, "PhantomTrackingControl");
            var machine = layer.stateMachine;
            var enabled = machine.AddState("PhantomTrackingEnabled");
            enabled.motion = enabledTree;
            enabled.writeDefaultValues = true;
            machine.defaultState = enabled;
        }

        private static BlendTree CreateEnabledDirectTree(
            PhantomAnimatorBuildContext context,
            IReadOnlyList<ConstraintBinding> bindings,
            string directWeightParameter)
        {
            var direct = CreateDirectTree(context, "PhantomTrackingEnabledDirect");

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

                    SetFloat(
                        offClip,
                        binding.Path,
                        binding.ConstraintType,
                        SourceWeight(0),
                        0f);
                    SetFloat(
                        offClip,
                        binding.Path,
                        binding.ConstraintType,
                        SourceWeight(1),
                        1f);
                    SetFloat(
                        onClip,
                        binding.Path,
                        binding.ConstraintType,
                        SourceWeight(0),
                        1f);
                    SetFloat(
                        onClip,
                        binding.Path,
                        binding.ConstraintType,
                        SourceWeight(1),
                        0f);
                }

                var groupTree = context.CreateBlendTree(
                    $"PhantomTracking{group}Tree",
                    parameter);
                groupTree.AddChild(offClip, 0f);
                groupTree.AddChild(onClip, 1f);
                AddDirectChild(direct, groupTree, directWeightParameter);
            }

            return direct;
        }

        private static BlendTree CreateDirectTree(
            PhantomAnimatorBuildContext context,
            string name)
        {
            var direct = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Direct,
                useAutomaticThresholds = false
            };
            context.RegisterBlendTree(direct);
            return direct;
        }

        private static void AddDirectChild(
            BlendTree direct,
            Motion motion,
            string directWeightParameter)
        {
            direct.AddChild(motion, 1f);
            var children = direct.children;
            children[children.Length - 1].directBlendParameter = directWeightParameter;
            direct.children = children;
        }

        private static List<ConstraintBinding> CollectBindings(PhantomAnimatorBuildContext context)
        {
            var bindings = new List<ConstraintBinding>();
            foreach (var group in PhantomTrackingControlGroups.All)
            {
                foreach (var bone in PhantomTrackingControlGroups.Bones(group))
                {
                    if (!context.Slot.CloneBoneAvatarPaths.TryGetValue(bone, out var path)
                        || !context.Slot.CloneBoneConstraintTypes.TryGetValue(bone, out var constraintType)
                        || !context.Slot.AnimationDriverBones.ContainsKey(bone))
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
