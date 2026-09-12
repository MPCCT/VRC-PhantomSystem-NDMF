using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace MPCCT.PhantomSystem.Editor.Tests
{
    internal sealed class PhantomFxOwnershipTests
    {
        [Test]
        public void FxFilter_PreservesSkinnedExtraBonesIntermediateParentsAndVisibleScale()
        {
            var root = new GameObject("Clone");
            var clip = new AnimationClip();
            try
            {
                var armature = Child(root.transform, "Armature");
                var hips = Child(armature, "Hips");
                var twist = Child(hips, "Twist");
                var hand = Child(twist, "Hand");
                var tail = Child(hips, "Tail");
                var eye = Child(hips, "Eye");
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.rootBone = armature;
                renderer.bones = new[] { hips, twist, hand, tail, eye };
                var slot = new PhantomSlotBuildState { CloneRoot = root, CloneArmature = armature };
                slot.CloneBones[HumanBodyBones.Hips] = hips;
                slot.CloneBones[HumanBodyBones.LeftHand] = hand;
                slot.CloneBones[HumanBodyBones.LeftEye] = eye;
                slot.CloneBoneConstraintTypes[HumanBodyBones.Hips] = typeof(VRCParentConstraint);
                slot.CloneBoneConstraintTypes[HumanBodyBones.LeftHand] = typeof(VRCRotationConstraint);
                // Eye is mapped by Humanoid but has no corresponding base bone/constraint.
                var channels = PhantomFxBoneAnimationFilter.CollectTransformChannels(slot);
                var retained = new List<EditorCurveBinding>();
                foreach (var target in new[] { tail, twist, eye })
                {
                    foreach (var property in new[] { "m_LocalPosition.x", "m_LocalRotation.x", "m_LocalScale.x" })
                    {
                        retained.Add(Binding(root, target, property));
                    }
                }
                retained.Add(Binding(root, hand, "m_LocalPosition.x"));
                foreach (var target in new[] { armature, hips, hand })
                {
                    retained.Add(Binding(root, target, "m_LocalScale.x"));
                }
                var removed = new[]
                {
                    Binding(root, hips, "m_LocalPosition.x"),
                    Binding(root, hips, "m_LocalRotation.x"),
                    Binding(root, hand, "localEulerAnglesRaw.y"),
                    Binding(root, armature, "m_LocalPosition.x")
                };
                foreach (var binding in retained.Concat(removed))
                {
                    AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 2f, 0.7f));
                }

                var result = PhantomFxBoneAnimationFilter.Filter(clip, channels, new HashSet<string>());

                Assert.AreEqual(removed.Length, result.RemovedTransformCurves);
                foreach (var binding in retained)
                {
                    Assert.IsNotNull(AnimationUtility.GetEditorCurve(clip, binding), binding.path + ":" + binding.propertyName);
                }
                foreach (var binding in removed)
                {
                    Assert.IsNull(AnimationUtility.GetEditorCurve(clip, binding), binding.path + ":" + binding.propertyName);
                }
                Assert.AreEqual(2f, clip.length);
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FxFilter_ProtectsGeneratedDriverTransforms()
        {
            var root = new GameObject("Clone");
            try
            {
                var driver = Child(root.transform, "Driver");
                var bone = Child(driver, "Hips");
                var slot = new PhantomSlotBuildState { CloneRoot = root, AnimationDriverRoot = driver };
                slot.AnimationDriverBones[HumanBodyBones.Hips] = bone;
                var channels = PhantomFxBoneAnimationFilter.CollectTransformChannels(slot);
                foreach (var target in new[] { driver, bone })
                {
                    foreach (var property in new[] { "m_LocalPosition.x", "m_LocalRotation.x", "m_LocalScale.x" })
                    {
                        Assert.IsTrue(PhantomFxBoneAnimationFilter.ShouldRemove(
                            Binding(root, target, property), channels, new HashSet<string>()));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FxMask_PreservesExtraBoneWithHumanoidBodyDisabledButRespectsTransformExclusion()
        {
            var root = new GameObject("Clone");
            var sourceMask = new AvatarMask();
            AvatarMask converted = null;
            try
            {
                var hips = Child(root.transform, "Hips");
                Child(hips, "Tail");
                Child(hips, "Excluded");
                var slot = new PhantomSlotBuildState { CloneRoot = root };
                slot.CloneBones[HumanBodyBones.Hips] = hips;
                sourceMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, false);
                sourceMask.transformCount = 2;
                sourceMask.SetTransformPath(0, "");
                sourceMask.SetTransformActive(0, true);
                sourceMask.SetTransformPath(1, "Hips/Excluded");
                sourceMask.SetTransformActive(1, false);

                converted = PhantomAvatarMaskConverter.Convert(
                    slot, sourceMask, null, "FX", applyHumanoidBodyMask: false);

                var states = Enumerable.Range(0, converted.transformCount).ToDictionary(
                    converted.GetTransformPath, converted.GetTransformActive);
                Assert.IsTrue(states["Hips/Tail"]);
                Assert.IsFalse(states["Hips/Excluded"]);
            }
            finally
            {
                Object.DestroyImmediate(converted);
                Object.DestroyImmediate(sourceMask);
                Object.DestroyImmediate(root);
            }
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static EditorCurveBinding Binding(GameObject root, Transform target, string property)
        {
            return EditorCurveBinding.FloatCurve(
                AnimationUtility.CalculateTransformPath(target, root.transform), typeof(Transform), property);
        }
    }
}
