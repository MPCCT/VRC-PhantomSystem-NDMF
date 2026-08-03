using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>
    /// Builds the Humanoid proxy used to let a frozen phantom body react through PhysBones.
    /// Hips remains an anchored proxy root; PhantomGrabbingHipsRigBuilder owns real Hips motion.
    /// </summary>
    internal static class PhantomGrabbingBodyRigBuilder
    {
        private const float PhantomGrabbingBodyPhysBoneScale = 0.025f;

        private static readonly HumanBodyBones[] IncludedBones =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.UpperChest,
            HumanBodyBones.Neck,
            HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot,
            HumanBodyBones.RightToes
        };

        public static void Build(
            BuildContext context,
            PhantomSlotBuildState slot,
            PhantomBuildReport report)
        {
            if (!slot.CloneBones.TryGetValue(HumanBodyBones.Hips, out var cloneHips))
            {
                report.Error(
                    $"Slot '{slot.SlotId}' enables Phantom Grabbing, but its baked avatar has no Humanoid Hips.",
                    slot.CloneRoot);
                return;
            }

            var rigRoot = ConstraintRigBuilder.EnsureChild(
                slot.SlotRoot.transform,
                "PhantomGrabbingBodyRig");
            var proxyAnchor = ConstraintRigBuilder.EnsureChild(rigRoot, "ProxyAnchor");
            proxyAnchor.SetPositionAndRotation(cloneHips.position, cloneHips.rotation);
            var outputRoot = ConstraintRigBuilder.EnsureChild(rigRoot, "OutputConstraints");

            BuildProxySkeleton(slot, proxyAnchor);
            if (!slot.PhantomGrabbingBodyProxyBones.TryGetValue(
                    HumanBodyBones.Hips,
                    out var proxyHips))
            {
                report.Error(
                    $"Slot '{slot.SlotId}' could not generate the Phantom Grabbing body proxy Hips.",
                    slot.CloneRoot);
                return;
            }

            AddProxyAnchorConstraint(proxyAnchor, cloneHips);

            foreach (var bone in IncludedBones)
            {
                if (bone == HumanBodyBones.Hips
                    || !slot.CloneBones.TryGetValue(bone, out var cloneBone)
                    || !slot.PhantomGrabbingBodyProxyBones.TryGetValue(bone, out var proxyBone))
                {
                    continue;
                }

                // Yes, I know Constraint and PhysBone component affecting the same 
                // game object may cause execution order issues. But this only 
                // affects one frame entering frozen state. So that's fine :)
                var syncHost = AddSyncConstraint(cloneBone, proxyBone, true);
                var outputHost = AddOutputConstraint(outputRoot, bone, proxyBone, cloneBone);
                AddSegmentPhysBone(slot, bone, proxyBone);
                slot.PhantomGrabbingBodySyncConstraintHosts[bone] = syncHost;
                slot.PhantomGrabbingBodyOutputConstraintHosts[bone] = outputHost;
            }

            PhantomGrabbingBoneDisplayBuilder.Build(context, slot, rigRoot, report);
        }

        private static void BuildProxySkeleton(PhantomSlotBuildState slot, Transform proxyRoot)
        {
            var selectedByTransform = new Dictionary<Transform, HumanBodyBones>();
            foreach (var bone in IncludedBones)
            {
                if (slot.CloneBones.TryGetValue(bone, out var transform))
                {
                    selectedByTransform[transform] = bone;
                }
            }

            foreach (var bone in IncludedBones)
            {
                if (!slot.CloneBones.TryGetValue(bone, out var source))
                {
                    continue;
                }

                var parent = FindProxyParent(source.parent, selectedByTransform, slot, proxyRoot);
                var proxy = new GameObject(source.name).transform;
                proxy.SetParent(parent, false);
                proxy.SetPositionAndRotation(source.position, source.rotation);
                proxy.localScale = DivideScale(source.lossyScale, parent.lossyScale);
                slot.PhantomGrabbingBodyProxyBones[bone] = proxy;
            }
        }

        private static Transform FindProxyParent(
            Transform sourceParent,
            IReadOnlyDictionary<Transform, HumanBodyBones> selectedByTransform,
            PhantomSlotBuildState slot,
            Transform fallback)
        {
            for (var current = sourceParent; current != null; current = current.parent)
            {
                if (selectedByTransform.TryGetValue(current, out var parentBone)
                    && slot.PhantomGrabbingBodyProxyBones.TryGetValue(
                        parentBone,
                        out var proxyParent))
                {
                    return proxyParent;
                }
            }

            return fallback;
        }

        private static Transform AddSyncConstraint(
            Transform source,
            Transform target,
            bool active)
        {
            var constraint = target.gameObject.AddComponent<VRCParentConstraint>();
            constraint.Locked = false;
            constraint.IsActive = active;
            constraint.TargetTransform = target;
            constraint.SolveInLocalSpace = false;
            constraint.FreezeToWorld = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;
            constraint.Sources = new VRCConstraintSourceKeyableList
            {
                new VRCConstraintSource { SourceTransform = source, Weight = 1f }
            };
            constraint.TryBakeCurrentOffsets(VRCConstraintBase.BakeOptions.BakeOffsets);
            constraint.Locked = true;
            constraint.enabled = true;
            return target;
        }

        private static void AddSegmentPhysBone(
            PhantomSlotBuildState slot,
            HumanBodyBones bone,
            Transform proxyBone)
        {
            var physBone = proxyBone.gameObject.AddComponent<VRCPhysBone>();
            physBone.rootTransform = proxyBone;
            physBone.ignoreOtherPhysBones = true;

            // Each component owns exactly one proxy segment. Descendant proxy
            // bones have their own PhysBones, so exclude them explicitly and use
            // a virtual endpoint aligned with this bone's outgoing direction.
            foreach (Transform child in proxyBone)
            {
                if (slot.PhantomGrabbingBodyProxyBones.ContainsValue(child))
                {
                    physBone.ignoreTransforms.Add(child);
                }
            }

            var endpoint = CalculateSegmentEndpoint(slot, bone, proxyBone);
            physBone.endpointPosition = endpoint;
            slot.PhantomGrabbingBodySegmentEndpoints[bone] = endpoint;
            physBone.multiChildType = VRCPhysBoneBase.MultiChildType.Ignore;
            physBone.pull = 1f;
            physBone.spring = 0f;
            physBone.stiffness = 0.2f;
            physBone.gravity = 0f;
            physBone.immobileType = VRCPhysBoneBase.ImmobileType.AllMotion;
            physBone.immobile = 1f;
            physBone.allowCollision = VRCPhysBoneBase.AdvancedBool.False;
            physBone.radius = CalculateRadius(slot);
            physBone.allowGrabbing = VRCPhysBoneBase.AdvancedBool.True;
            physBone.allowPosing = VRCPhysBoneBase.AdvancedBool.True;
            physBone.snapToHand = false;
            physBone.grabMovement = 1f;
            physBone.maxStretch = 0f;
            physBone.maxSquish = 0f;
            physBone.stretchMotion = 0f;
            physBone.isAnimated = true;
            physBone.resetWhenDisabled = false;
            physBone.parameter = string.Empty;
            physBone.enabled = false;
            slot.PhantomGrabbingBodyPhysBoneHosts[bone] = proxyBone;
        }

        private static void AddProxyAnchorConstraint(Transform anchor, Transform cloneHips)
        {
            var constraint = anchor.gameObject.AddComponent<VRCParentConstraint>();
            constraint.Locked = false;
            constraint.IsActive = true;
            constraint.SolveInLocalSpace = false;
            constraint.FreezeToWorld = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;
            constraint.Sources = new VRCConstraintSourceKeyableList
            {
                new VRCConstraintSource { SourceTransform = cloneHips, Weight = 1f }
            };
            constraint.TryBakeCurrentOffsets(VRCConstraintBase.BakeOptions.BakeOffsets);
            constraint.Locked = true;
            constraint.enabled = true;
        }

        private static Transform AddOutputConstraint(
            Transform parent,
            HumanBodyBones bone,
            Transform source,
            Transform target)
        {
            var host = ConstraintRigBuilder.EnsureChild(parent, bone + "Output");
            var constraint = host.gameObject.AddComponent<VRCRotationConstraint>();
            constraint.Locked = false;
            constraint.IsActive = false;
            constraint.TargetTransform = target;
            constraint.SolveInLocalSpace = true;
            constraint.Sources = new VRCConstraintSourceKeyableList
            {
                new VRCConstraintSource { SourceTransform = source, Weight = 1f }
            };
            constraint.TryBakeCurrentOffsets(VRCConstraintBase.BakeOptions.BakeOffsets);
            constraint.Locked = true;
            constraint.enabled = true;
            return host;
        }

        private static Vector3 DivideScale(Vector3 value, Vector3 divisor)
        {
            return new Vector3(
                Mathf.Approximately(divisor.x, 0f) ? value.x : value.x / divisor.x,
                Mathf.Approximately(divisor.y, 0f) ? value.y : value.y / divisor.y,
                Mathf.Approximately(divisor.z, 0f) ? value.z : value.z / divisor.z);
        }

        private static Vector3 CalculateSegmentEndpoint(
            PhantomSlotBuildState slot,
            HumanBodyBones bone,
            Transform proxyBone)
        {
            foreach (var pair in slot.PhantomGrabbingBodyProxyBones)
            {
                if (pair.Value.parent == proxyBone
                    && pair.Value.localPosition.sqrMagnitude > 0.000001f)
                {
                    return pair.Value.localPosition;
                }
            }

            if (slot.CloneBones.TryGetValue(bone, out var source)
                && source.parent != null)
            {
                var incoming = source.position - source.parent.position;
                if (incoming.sqrMagnitude > 0.000001f)
                {
                    return source.InverseTransformVector(incoming);
                }
            }

            return Vector3.up * CalculateFallbackEndpointLength(slot);
        }

        private static float CalculateFallbackEndpointLength(PhantomSlotBuildState slot)
        {
            if (slot.CloneBones.TryGetValue(HumanBodyBones.Head, out var head)
                && slot.CloneBones.TryGetValue(HumanBodyBones.Hips, out var hips))
            {
                return Mathf.Clamp(Vector3.Distance(head.position, hips.position) * 0.05f, 0.02f, 0.1f);
            }

            return 0.05f;
        }

        private static float CalculateRadius(PhantomSlotBuildState slot)
        {
            if (slot.CloneBones.TryGetValue(HumanBodyBones.Head, out var head)
                && slot.CloneBones.TryGetValue(HumanBodyBones.LeftFoot, out var foot))
            {
                return Mathf.Clamp(
                    Vector3.Distance(head.position, foot.position)
                    * PhantomGrabbingBodyPhysBoneScale,
                    0.01f,
                    0.10f);
            }

            return 0.1f;
        }
    }
}
