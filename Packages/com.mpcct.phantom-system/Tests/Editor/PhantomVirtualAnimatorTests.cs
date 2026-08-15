using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MPCCT.PhantomSystem.Editor.Tests
{
    internal sealed class PhantomVirtualAnimatorTests
    {
        [TestCase("Slot1", "PhantomActivate", "PhantomSystem_Slot1_PhantomActivate")]
        [TestCase("Slot_2", "PhantomTrackingControl", "PhantomSystem_Slot_2_PhantomTrackingControl")]
        public void GeneratedLayerName_IncludesSlotPrefix(
            string slotHierarchyName,
            string layerName,
            string expected)
        {
            Assert.AreEqual(
                expected,
                PhantomAnimatorGraphUtility.BuildSlotLayerName(slotHierarchyName, layerName));
        }

        [Test]
        public void DriverNeutralPose_WritesOneFrameQuaternionCurvesOnly()
        {
            var clip = new AnimationClip { name = "DriverNeutral" };
            try
            {
                PhantomHumanoidClipBaker.WriteNeutralRotationCurves(
                    clip,
                    new Dictionary<string, Quaternion>
                    {
                        ["Driver/Hips"] = Quaternion.Euler(5f, 10f, 15f),
                        ["Driver/Hips/LeftUpperLeg"] = Quaternion.Euler(-20f, 3f, 8f)
                    });

                var bindings = AnimationUtility.GetCurveBindings(clip);
                Assert.AreEqual(8, bindings.Length);
                Assert.IsTrue(bindings.All(binding => binding.type == typeof(Transform)));
                Assert.IsTrue(bindings.All(binding =>
                    binding.propertyName.StartsWith("m_LocalRotation.")));
                Assert.AreEqual(2, bindings.Select(binding => binding.path).Distinct().Count());
                Assert.IsFalse(bindings.Any(binding =>
                    binding.propertyName.StartsWith("m_LocalPosition.")
                    || binding.propertyName.StartsWith("m_LocalScale.")));

                foreach (var binding in bindings)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    Assert.AreEqual(2, curve.length);
                    Assert.AreEqual(0f, curve.keys[0].time, 0.000001f);
                    Assert.AreEqual(
                        PhantomAnimatorClipUtility.FrameDuration,
                        curve.keys[1].time,
                        0.000001f);
                    Assert.AreEqual(curve.keys[0].value, curve.keys[1].value, 0.000001f);
                }

                Assert.AreEqual(
                    PhantomAnimatorClipUtility.FrameDuration,
                    clip.length,
                    0.000001f);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void DriverNeutralPose_ControllerHasOneDefaultWeightOverrideLayer()
        {
            var clip = new AnimationClip { name = "Neutral" };
            var controller = PhantomDriverNeutralAnimatorBuilder.CreateController(
                "Slot1",
                "Slot_1",
                clip);
            try
            {
                Assert.AreEqual("PhantomSystem_Slot1_DriverNeutral_FX", controller.name);
                Assert.AreEqual(1, controller.layers.Length);
                var layer = controller.layers.Single();
                Assert.AreEqual(
                    "PhantomSystem_Slot_1_DriverNeutralPose",
                    layer.name);
                Assert.AreEqual(1f, layer.defaultWeight);
                Assert.AreEqual(AnimatorLayerBlendingMode.Override, layer.blendingMode);
                Assert.AreSame(clip, layer.stateMachine.defaultState.motion);
                Assert.AreEqual(1, layer.stateMachine.states.Length);
            }
            finally
            {
                DestroyControllerGraph(controller, null);
            }
        }

        [Test]
        public void DriverNeutralPose_ReadsRotationRelativeToConfiguredPoseParent()
        {
            var root = new GameObject("Root");
            try
            {
                root.transform.localRotation = Quaternion.Euler(11f, -7f, 4f);
                var poseParent = CreateChild(root.transform, "PoseParent").transform;
                poseParent.localRotation = Quaternion.Euler(-8f, 17f, 2f);
                var unmappedIntermediate = CreateChild(poseParent, "Intermediate").transform;
                unmappedIntermediate.localRotation = Quaternion.Euler(13f, 6f, -9f);
                var target = CreateChild(unmappedIntermediate, "Target").transform;
                target.localRotation = Quaternion.Euler(-21f, 3f, 14f);

                var expected = Quaternion.Inverse(poseParent.rotation) * target.rotation;
                var actual = PhantomHumanoidClipBaker.ReadRelativeRotation(
                    target,
                    poseParent);

                Assert.Less(Quaternion.Angle(expected, actual), 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DriverNeutralPose_RequiresRetainedGestureOrActionAndDriver()
        {
            var root = new GameObject("Root");
            try
            {
                var slot = new PhantomSlotBuildState
                {
                    Slot = new PhantomSlot(),
                    SourceFxMergeAnimator = AddFxMergeAnimator(root, "Fx")
                };

                Assert.IsFalse(PhantomDriverNeutralAnimatorBuilder.ShouldBuild(slot));

                slot.SourceGestureMergeAnimator = AddFxMergeAnimator(root, "Gesture");
                Assert.IsFalse(PhantomDriverNeutralAnimatorBuilder.ShouldBuild(slot));

                slot.AnimationDriverBones[HumanBodyBones.Hips] =
                    CreateChild(root.transform, "DriverHips").transform;
                Assert.IsTrue(PhantomDriverNeutralAnimatorBuilder.ShouldBuild(slot));

                slot.SourceGestureMergeAnimator = null;
                slot.SourceActionMergeAnimator = AddFxMergeAnimator(root, "Action");
                Assert.IsTrue(PhantomDriverNeutralAnimatorBuilder.ShouldBuild(slot));

                slot.Slot.removeSourceControls = true;
                Assert.IsFalse(PhantomDriverNeutralAnimatorBuilder.ShouldBuild(slot));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(VRCAvatarDescriptor.AnimLayerType.Gesture, AnimatorLayerBlendingMode.Override, false, true)]
        [TestCase(VRCAvatarDescriptor.AnimLayerType.Action, AnimatorLayerBlendingMode.Override, false, true)]
        [TestCase(VRCAvatarDescriptor.AnimLayerType.FX, AnimatorLayerBlendingMode.Override, false, false)]
        [TestCase(VRCAvatarDescriptor.AnimLayerType.Gesture, AnimatorLayerBlendingMode.Additive, false, false)]
        [TestCase(VRCAvatarDescriptor.AnimLayerType.Action, AnimatorLayerBlendingMode.Override, true, false)]
        public void HumanoidNeutralCompletion_OnlyAppliesToOverrideGestureAndActionOutsideDirectTrees(
            VRCAvatarDescriptor.AnimLayerType playable,
            AnimatorLayerBlendingMode blendingMode,
            bool insideDirectBlendTree,
            bool expected)
        {
            Assert.AreEqual(
                expected,
                PhantomPlayableMotionConverter.ShouldCompleteHumanoidRotations(
                    playable,
                    blendingMode,
                    insideDirectBlendTree));
        }

        [Test]
        public void HumanoidNeutralCompletion_WritesEveryAllowedMissingBoneButNotAnimatedBone()
        {
            var clip = new AnimationClip { name = "Completed" };
            try
            {
                var animatedRotation = Quaternion.Euler(12f, -8f, 3f);
                PhantomHumanoidClipBaker.WriteNeutralRotationCurves(
                    clip,
                    new Dictionary<string, Quaternion>
                    {
                        ["Driver/LeftLowerLeg"] = animatedRotation
                    });

                PhantomHumanoidClipBaker.WriteMissingNeutralRotationCurves(
                    clip,
                    new HashSet<HumanBodyBones> { HumanBodyBones.LeftLowerLeg },
                    new HashSet<HumanBodyBones>
                    {
                        HumanBodyBones.LeftLowerLeg,
                        HumanBodyBones.RightLowerLeg,
                        HumanBodyBones.LeftToes
                    },
                    new Dictionary<HumanBodyBones, string>
                    {
                        [HumanBodyBones.LeftLowerLeg] = "Driver/LeftLowerLeg",
                        [HumanBodyBones.RightLowerLeg] = "Driver/RightLowerLeg",
                        [HumanBodyBones.LeftToes] = "Driver/LeftToes"
                    },
                    new Dictionary<HumanBodyBones, Quaternion>
                    {
                        [HumanBodyBones.LeftLowerLeg] = Quaternion.identity,
                        [HumanBodyBones.RightLowerLeg] = Quaternion.Euler(-7f, 4f, 2f),
                        [HumanBodyBones.LeftToes] = Quaternion.Euler(9f, 0f, 0f)
                    });

                var bindings = AnimationUtility.GetCurveBindings(clip);
                Assert.AreEqual(12, bindings.Length);
                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        "Driver/LeftLowerLeg",
                        "Driver/RightLowerLeg",
                        "Driver/LeftToes"
                    },
                    bindings.Select(binding => binding.path).Distinct().ToArray());
                Assert.IsTrue(bindings.All(binding =>
                    binding.propertyName.StartsWith("m_LocalRotation.")));

                var xBinding = EditorCurveBinding.FloatCurve(
                    "Driver/LeftLowerLeg",
                    typeof(Transform),
                    "m_LocalRotation.x");
                var leftCurve = AnimationUtility.GetEditorCurve(clip, xBinding);
                Assert.AreEqual(animatedRotation.normalized.x, leftCurve.keys[0].value, 0.000001f);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void HumanoidNeutralCompletion_DoesNotTurnNonHumanoidClipIntoPose()
        {
            var clip = new AnimationClip { name = "GenericOnly" };
            try
            {
                PhantomHumanoidClipBaker.WriteMissingNeutralRotationCurves(
                    clip,
                    new HashSet<HumanBodyBones>(),
                    new HashSet<HumanBodyBones> { HumanBodyBones.LeftLowerLeg },
                    new Dictionary<HumanBodyBones, string>
                    {
                        [HumanBodyBones.LeftLowerLeg] = "Driver/LeftLowerLeg"
                    },
                    new Dictionary<HumanBodyBones, Quaternion>
                    {
                        [HumanBodyBones.LeftLowerLeg] = Quaternion.identity
                    });

                Assert.IsEmpty(AnimationUtility.GetCurveBindings(clip));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void HumanoidNeutralCompletion_RespectsHumanoidAvatarMask()
        {
            var clone = new GameObject("Clone");
            var mask = new AvatarMask();
            try
            {
                var leftBone = CreateChild(clone.transform, "LeftLowerLeg").transform;
                var rightBone = CreateChild(clone.transform, "RightLowerLeg").transform;
                var driverRoot = CreateChild(clone.transform, "Driver");
                var slot = new PhantomSlotBuildState { CloneRoot = clone };
                slot.CloneBones[HumanBodyBones.LeftLowerLeg] = leftBone;
                slot.CloneBones[HumanBodyBones.RightLowerLeg] = rightBone;
                slot.AnimationDriverBones[HumanBodyBones.LeftLowerLeg] =
                    CreateChild(driverRoot.transform, "LeftLowerLeg").transform;
                slot.AnimationDriverBones[HumanBodyBones.RightLowerLeg] =
                    CreateChild(driverRoot.transform, "RightLowerLeg").transform;

                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        HumanBodyBones.LeftLowerLeg,
                        HumanBodyBones.RightLowerLeg
                    },
                    PhantomAvatarMaskConverter.CollectActiveHumanoidBones(
                        slot,
                        null,
                        null));

                for (var part = AvatarMaskBodyPart.Root;
                     part < AvatarMaskBodyPart.LastBodyPart;
                     part++)
                {
                    mask.SetHumanoidBodyPartActive(part, false);
                }
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, true);

                var bones = PhantomAvatarMaskConverter.CollectActiveHumanoidBones(
                    slot,
                    null,
                    mask);

                CollectionAssert.AreEquivalent(
                    new[] { HumanBodyBones.LeftLowerLeg },
                    bones);
            }
            finally
            {
                Object.DestroyImmediate(mask);
                Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void AnimatorParameterBinding_IsNotClassifiedAsHumanoid()
        {
            const string parameterName = "FaceEmo_Hai_GestureLWProxy";
            var binding = EditorCurveBinding.FloatCurve(
                string.Empty,
                typeof(Animator),
                parameterName);
            var parameters = new HashSet<string> { parameterName };

            Assert.AreEqual(
                PhantomAnimationBindingKind.AnimatorParameter,
                PhantomAnimationBindingClassifier.Classify(binding, parameters));
            Assert.AreEqual(
                PhantomAnimationBindingKind.UnsupportedAnimator,
                PhantomAnimationBindingClassifier.Classify(binding));
        }

        [Test]
        public void HumanoidBinding_TakesPriorityOverMatchingParameterName()
        {
            var muscleName = HumanTrait.MuscleName.First();
            var binding = EditorCurveBinding.FloatCurve(
                string.Empty,
                typeof(Animator),
                muscleName);

            Assert.AreEqual(
                PhantomAnimationBindingKind.ResolvedHumanoid,
                PhantomAnimationBindingClassifier.Classify(
                    binding,
                    new HashSet<string> { muscleName }));
        }

        [Test]
        public void VirtualBlendTreeConversion_PreservesChildSettingsAndConsumesMirror()
        {
            var sourceMotion = VirtualClip.Create("Source");
            var convertedMotion = VirtualClip.Create("Converted");
            var source = VirtualBlendTree.Create("SourceTree");
            source.BlendType = BlendTreeType.Simple1D;
            source.BlendParameter = "Speed";
            source.BlendParameterY = "Direction";
            source.UseAutomaticThresholds = false;
            source.MinThreshold = -1f;
            source.MaxThreshold = 2f;
            source.Children = ImmutableList.Create(new VirtualBlendTree.VirtualChildMotion
            {
                Motion = sourceMotion,
                Threshold = 0.35f,
                Position = new Vector2(1.25f, -0.5f),
                TimeScale = 1.5f,
                CycleOffset = 0.25f,
                DirectBlendParameter = "DirectWeight",
                Mirror = true
            });

            var converted = PhantomPlayableMotionConverter.CreateConvertedBlendTree(
                source,
                new VirtualMotion[] { convertedMotion },
                "ConvertedTree");

            Assert.AreEqual("ConvertedTree", converted.Name);
            Assert.AreEqual(source.BlendType, converted.BlendType);
            Assert.AreEqual(source.BlendParameter, converted.BlendParameter);
            Assert.AreEqual(source.BlendParameterY, converted.BlendParameterY);
            Assert.AreEqual(source.UseAutomaticThresholds, converted.UseAutomaticThresholds);
            Assert.AreEqual(source.MinThreshold, converted.MinThreshold);
            Assert.AreEqual(source.MaxThreshold, converted.MaxThreshold);
            Assert.AreSame(sourceMotion, source.Children.Single().Motion);
            Assert.AreSame(convertedMotion, converted.Children.Single().Motion);
            Assert.AreEqual(source.Children.Single().Threshold, converted.Children.Single().Threshold);
            Assert.AreEqual(source.Children.Single().Position, converted.Children.Single().Position);
            Assert.AreEqual(source.Children.Single().TimeScale, converted.Children.Single().TimeScale);
            Assert.AreEqual(source.Children.Single().CycleOffset, converted.Children.Single().CycleOffset);
            Assert.AreEqual(
                source.Children.Single().DirectBlendParameter,
                converted.Children.Single().DirectBlendParameter);
            Assert.IsTrue(source.Children.Single().Mirror);
            Assert.IsFalse(converted.Children.Single().Mirror);
        }

        [Test]
        public void SourceParameterCollection_IncludesControllerAndPlayAudioOnlyReferences()
        {
            var playAudio = ScriptableObject.CreateInstance<VRCAnimatorPlayAudio>();
            playAudio.ParameterName = "AudioOnly";
            var state = new AnimatorState
            {
                name = "State",
                behaviours = new StateMachineBehaviour[] { playAudio }
            };
            var machine = new AnimatorStateMachine
            {
                name = "Machine",
                states = new[] { new ChildAnimatorState { state = state } },
                defaultState = state
            };
            var controller = new AnimatorController
            {
                name = "Source",
                parameters = new[]
                {
                    new AnimatorControllerParameter
                    {
                        name = "ControllerOnly",
                        type = AnimatorControllerParameterType.Float,
                        defaultFloat = 0.25f
                    }
                },
                layers = new[]
                {
                    new AnimatorControllerLayer
                    {
                        name = "Layer",
                        stateMachine = machine,
                        syncedLayerIndex = -1
                    }
                }
            };
            try
            {
                var definitions = new Dictionary<string, PhantomParameterDefinition>();
                PhantomParameterAnalysis.CollectControllerParameters(controller, definitions);

                var controllerDefinition = definitions["ControllerOnly"];
                var audioDefinition = definitions["AudioOnly"];
                Assert.AreEqual(AnimatorControllerParameterType.Float, controllerDefinition.ParameterType);
                Assert.AreEqual(0.25f, controllerDefinition.DefaultValue);
                Assert.AreEqual(AnimatorControllerParameterType.Int, audioDefinition.ParameterType);
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(machine);
                Object.DestroyImmediate(state);
                Object.DestroyImmediate(playAudio);
            }
        }

        [Test]
        public void PlayAudioUnknownParameter_IsKeptAndRegistered()
        {
            var playAudio = ScriptableObject.CreateInstance<VRCAnimatorPlayAudio>();
            try
            {
                playAudio.ParameterName = "UnknownAudio";
                var slot = new PhantomSlot { id = "Slot1" };
                var state = new PhantomSlotBuildState
                {
                    Slot = slot,
                    SlotId = "Slot1",
                    Identity = PhantomSlotIdentity.Create(slot),
                    ParameterResolution = new PhantomSlotParameterResolution()
                };

                PhantomSourcePlayableControllerProcessor.RemapPlayAudioParameter(
                    playAudio,
                    state);

                Assert.AreEqual("UnknownAudio", playAudio.ParameterName);
                CollectionAssert.Contains(
                    state.UnresolvedSourceParameterReferences["UnknownAudio"],
                    "Animator Play Audio");
            }
            finally
            {
                Object.DestroyImmediate(playAudio);
            }
        }

        [Test]
        public void SharedSourceController_IsConvertedIndependentlyPerSlot()
        {
            var avatar = new GameObject("Avatar");
            var clone1 = CreateChild(avatar.transform, "Clone1");
            var clone2 = CreateChild(avatar.transform, "Clone2");
            CreateChild(clone1.transform, "Bone");
            CreateChild(clone2.transform, "Bone");
            CreateChild(clone1.transform, "Driver");
            CreateChild(clone2.transform, "Driver");
            avatar.AddComponent<Animator>();
            avatar.AddComponent<VRCAvatarDescriptor>();

            var sourceClip = new AnimationClip { name = "SourceClip" };
            AnimationUtility.SetEditorCurve(
                sourceClip,
                EditorCurveBinding.FloatCurve("Bone", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Constant(0f, 1f, 0f));
            var sourceTracking = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
            sourceTracking.trackingHead = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            var sourcePlayAudio = ScriptableObject.CreateInstance<VRCAnimatorPlayAudio>();
            sourcePlayAudio.ParameterName = "AudioIndex";
            var sourceState = new AnimatorState
            {
                name = "State",
                motion = sourceClip,
                behaviours = new StateMachineBehaviour[] { sourceTracking, sourcePlayAudio }
            };
            var sourceMachine = new AnimatorStateMachine { name = "Machine" };
            sourceMachine.states = new[]
            {
                new ChildAnimatorState { state = sourceState }
            };
            sourceMachine.defaultState = sourceState;
            var sourceController = new AnimatorController
            {
                name = "SharedSource",
                layers = new[]
                {
                    new AnimatorControllerLayer
                    {
                        name = "Layer",
                        defaultWeight = 1f,
                        syncedLayerIndex = -1,
                        stateMachine = sourceMachine
                    }
                }
            };
            var merge1 = AddMergeAnimator(avatar, clone1, sourceController);
            var merge2 = AddMergeAnimator(avatar, clone2, sourceController);
            var slot1 = CreateSlotState("Slot1", clone1, merge1, sourceController);
            var slot2 = CreateSlotState("Slot2", clone2, merge2, sourceController);
            var context = new BuildContext(avatar, null);
            try
            {
                Assert.AreSame(clone1, merge1.relativePathRoot.Get(avatar.transform));
                Assert.AreSame(clone2, merge2.relativePathRoot.Get(avatar.transform));

                var animatorServices =
                    context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                var virtual1 = animatorServices.ControllerContext.Controllers[merge1];
                var virtual2 = animatorServices.ControllerContext.Controllers[merge2];

                Assert.AreNotSame(virtual1, virtual2);
                Assert.AreNotSame(
                    virtual1.Layers.Single().StateMachine,
                    virtual2.Layers.Single().StateMachine);
                Assert.AreNotSame(
                    virtual1.Layers.Single().StateMachine.AllStates().Single(),
                    virtual2.Layers.Single().StateMachine.AllStates().Single());
                Assert.AreNotSame(
                    virtual1.Layers.Single().StateMachine.AllStates().Single().Behaviours
                        .OfType<VRCAnimatorPlayAudio>().Single(),
                    virtual2.Layers.Single().StateMachine.AllStates().Single().Behaviours
                        .OfType<VRCAnimatorPlayAudio>().Single());

                var settings = new PhantomSystemProjectSettingsSnapshot(1024, 30f, 0.0005f, 0.25f);
                using (new ObjectRegistryScope(context.ObjectRegistry))
                {
                    PhantomSourcePlayableControllerProcessor.ProcessVirtual(
                        context,
                        slot1,
                        settings,
                        new PhantomBuildReport());
                    PhantomSourcePlayableControllerProcessor.ProcessVirtual(
                        context,
                        slot2,
                        settings,
                        new PhantomBuildReport());
                }

                Assert.That(virtual1.Layers.Single().Name, Does.StartWith("PhantomSystem_Slot1_FX_"));
                Assert.That(virtual2.Layers.Single().Name, Does.StartWith("PhantomSystem_Slot2_FX_"));
                AssertTrackingDriver(virtual1, slot1.Slot);
                AssertTrackingDriver(virtual2, slot2.Slot);
                Assert.AreEqual(
                    "PhantomSystem/Slot1/Original/AudioIndex",
                    virtual1.Layers.Single().StateMachine.AllStates().Single().Behaviours
                        .OfType<VRCAnimatorPlayAudio>().Single().ParameterName);
                Assert.AreEqual(
                    "PhantomSystem/Slot2/Original/AudioIndex",
                    virtual2.Layers.Single().StateMachine.AllStates().Single().Behaviours
                        .OfType<VRCAnimatorPlayAudio>().Single().ParameterName);
                Assert.IsTrue(slot1.HasTrackingControlConversion);
                Assert.IsTrue(slot2.HasTrackingControlConversion);
                Assert.IsInstanceOf<VRCAnimatorTrackingControl>(sourceState.behaviours[0]);
                Assert.AreEqual("AudioIndex", sourcePlayAudio.ParameterName);

                var slot1Binding = ((VirtualClip)virtual1.Layers.Single().StateMachine.AllStates().Single().Motion)
                    .GetFloatCurveBindings().Single();
                var slot2Binding = ((VirtualClip)virtual2.Layers.Single().StateMachine.AllStates().Single().Motion)
                    .GetFloatCurveBindings().Single();
                Assert.IsTrue(PhantomFxBoneAnimationFilter.IsDummyBinding(slot1Binding));
                Assert.IsTrue(PhantomFxBoneAnimationFilter.IsDummyBinding(slot2Binding));
                Assert.AreEqual(1, slot1.ConvertedClipReferences.Count);
                Assert.AreEqual(1, slot2.ConvertedClipReferences.Count);

                context.DeactivateAllExtensionContexts();

                Assert.AreNotSame(sourceController, merge1.animator);
                Assert.AreNotSame(sourceController, merge2.animator);
                Assert.AreNotSame(merge1.animator, merge2.animator);
                Assert.AreEqual(MergeAnimatorPathMode.Absolute, merge1.pathMode);
                Assert.AreEqual(MergeAnimatorPathMode.Absolute, merge2.pathMode);
                Assert.AreEqual(
                    "PhantomSystem/Slot1/Original/AudioIndex",
                    ((AnimatorController)merge1.animator).GetBehaviours<VRCAnimatorPlayAudio>()
                    .Single().ParameterName);
                Assert.AreEqual(
                    "PhantomSystem/Slot2/Original/AudioIndex",
                    ((AnimatorController)merge2.animator).GetBehaviours<VRCAnimatorPlayAudio>()
                    .Single().ParameterName);
                Assert.IsInstanceOf<VRCAnimatorTrackingControl>(sourceState.behaviours[0]);
                Assert.AreEqual("AudioIndex", sourcePlayAudio.ParameterName);

                var buildState = new PhantomBuildState
                {
                    System = new PhantomSystemBuildState()
                };
                buildState.System.Slots.Add(slot1);
                buildState.System.Slots.Add(slot2);
                var committedClip1 = ((AnimatorController)merge1.animator).animationClips.Single();
                var committedClip2 = ((AnimatorController)merge2.animator).animationClips.Single();
                Assert.IsTrue(AnimationBindingDiagnostics.IsConvertedPlayableClip(
                    context.ObjectRegistry,
                    buildState,
                    committedClip1));
                Assert.IsTrue(AnimationBindingDiagnostics.IsConvertedPlayableClip(
                    context.ObjectRegistry,
                    buildState,
                    committedClip2));
            }
            finally
            {
                context.DeactivateAllExtensionContexts();
                DestroyControllerGraph(merge1.animator, sourceController);
                DestroyControllerGraph(merge2.animator, sourceController);
                Object.DestroyImmediate(sourceController);
                Object.DestroyImmediate(sourceMachine);
                Object.DestroyImmediate(sourceState);
                Object.DestroyImmediate(sourceTracking);
                Object.DestroyImmediate(sourcePlayAudio);
                Object.DestroyImmediate(sourceClip);
                Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void VirtualPathMapper_RoundTripsCloneRelativePaths()
        {
            var avatar = new GameObject("Avatar");
            var clone = CreateChild(avatar.transform, "Runtime/Slot1/Clone");
            try
            {
                var mapper = new PhantomVirtualPathMapper(avatar.transform, clone);

                Assert.AreEqual("Runtime_Slot1_Clone", mapper.CloneRootPath);
                Assert.AreEqual(
                    "Armature/Hips",
                    mapper.ToCloneRelative("Runtime_Slot1_Clone/Armature/Hips"));
                Assert.AreEqual(
                    "Runtime_Slot1_Clone/Armature/Hips",
                    mapper.ToAvatarRelative("Armature/Hips"));
                Assert.AreEqual(string.Empty, mapper.ToCloneRelative("Runtime_Slot1_Clone"));
            }
            finally
            {
                Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void AvatarMaskConversion_WritesAvatarRelativePaths()
        {
            var avatar = new GameObject("Avatar");
            var clone = CreateChild(avatar.transform, "Clone");
            CreateChild(clone.transform, "Bone");
            var sourceMask = new AvatarMask();
            AvatarMask converted = null;
            try
            {
                var slot = new PhantomSlotBuildState { CloneRoot = clone };
                converted = PhantomAvatarMaskConverter.Convert(
                    slot,
                    sourceMask,
                    null,
                    "Converted",
                    avatar.transform);

                CollectionAssert.Contains(
                    Enumerable.Range(0, converted.transformCount)
                        .Select(converted.GetTransformPath)
                        .ToArray(),
                    "Clone/Bone");
            }
            finally
            {
                if (converted != null)
                {
                    Object.DestroyImmediate(converted);
                }
                Object.DestroyImmediate(sourceMask);
                Object.DestroyImmediate(avatar);
            }
        }

        [TestCase(VRCAvatarDescriptor.AnimLayerType.FX)]
        [TestCase(VRCAvatarDescriptor.AnimLayerType.Gesture)]
        [TestCase(VRCAvatarDescriptor.AnimLayerType.Action)]
        public void SourcePlayable_MergesIntoFxPlayable(
            VRCAvatarDescriptor.AnimLayerType sourcePlayable)
        {
            Assert.AreEqual(
                VRCAvatarDescriptor.AnimLayerType.FX,
                PhantomSourceIntegrationBuilder.ResolveMergeTarget(sourcePlayable));
        }

        [Test]
        public void LayerControl_PreservesOnlySelfTargetAlreadyInFinalPlayable()
        {
            Assert.IsTrue(PhantomSourcePlayableControllerProcessor.CanPreserveLayerControl(
                VRCAvatarDescriptor.AnimLayerType.FX,
                VRCAvatarDescriptor.AnimLayerType.FX));
            Assert.IsFalse(PhantomSourcePlayableControllerProcessor.CanPreserveLayerControl(
                VRCAvatarDescriptor.AnimLayerType.Gesture,
                VRCAvatarDescriptor.AnimLayerType.Gesture));
            Assert.IsFalse(PhantomSourcePlayableControllerProcessor.CanPreserveLayerControl(
                VRCAvatarDescriptor.AnimLayerType.Gesture,
                VRCAvatarDescriptor.AnimLayerType.FX));
        }

        [TestCase(true, 0.75f)]
        [TestCase(false, 0f)]
        public void ConvertedActionPlayableControl_TargetsFinalFx(
            bool enabled,
            float expectedWeight)
        {
            var marker = PhantomSourcePlayableControllerProcessor
                .CreateConvertedActionLayerControlMarker(
                    new PhantomConvertedActionLayer("ActionTarget", 0.75f),
                    enabled,
                    "ActionDebug");
            try
            {
                Assert.AreEqual(VRCAvatarDescriptor.AnimLayerType.FX, marker.targetPlayable);
                Assert.AreEqual("ActionTarget", marker.targetLayerName);
                Assert.AreEqual(expectedWeight, marker.goalWeight);
                Assert.AreEqual(0f, marker.blendDuration);
                Assert.AreEqual("ActionDebug", marker.debugString);
            }
            finally
            {
                Object.DestroyImmediate(marker);
            }
        }

        [Test]
        public void MergeAnimatorFinalizer_OrdersAllPhantomControllersInsideFx()
        {
            var avatar = new GameObject("Avatar");
            var runtimeRoot = CreateChild(avatar.transform, "PhantomRuntime");
            var externalHost = CreateChild(avatar.transform, "External");
            var external = externalHost.AddComponent<ModularAvatarMergeAnimator>();
            external.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            external.layerPriority = 10;
            var state = new PhantomBuildState
            {
                System = new PhantomSystemBuildState { RuntimeRoot = runtimeRoot }
            };
            var context = new BuildContext(avatar, null);
            try
            {
                for (var index = 0; index < 2; index++)
                {
                    var host = CreateChild(runtimeRoot.transform, $"Slot{index + 1}");
                    var slot = new PhantomSlotBuildState
                    {
                        SlotId = $"Slot{index + 1}",
                        DriverNeutralMergeAnimator = AddFxMergeAnimator(host, "Neutral"),
                        SourceGestureMergeAnimator = AddFxMergeAnimator(host, "Gesture"),
                        SourceActionMergeAnimator = AddFxMergeAnimator(host, "Action"),
                        SourceFxMergeAnimator = AddFxMergeAnimator(host, "SourceFx"),
                        CoreMergeAnimator = AddFxMergeAnimator(host, "Core"),
                        TrackingMergeAnimator = AddFxMergeAnimator(host, "Tracking"),
                        PhantomViewMergeAnimator = AddFxMergeAnimator(host, "View")
                    };
                    state.System.Slots.Add(slot);
                }

                PhantomMergeAnimatorFinalizer.Apply(context, state);

                CollectionAssert.AreEqual(
                    Enumerable.Range(11, 14).ToArray(),
                    new[]
                    {
                        state.System.Slots[0].DriverNeutralMergeAnimator.layerPriority,
                        state.System.Slots[1].DriverNeutralMergeAnimator.layerPriority,
                        state.System.Slots[0].SourceGestureMergeAnimator.layerPriority,
                        state.System.Slots[1].SourceGestureMergeAnimator.layerPriority,
                        state.System.Slots[0].SourceActionMergeAnimator.layerPriority,
                        state.System.Slots[1].SourceActionMergeAnimator.layerPriority,
                        state.System.Slots[0].SourceFxMergeAnimator.layerPriority,
                        state.System.Slots[1].SourceFxMergeAnimator.layerPriority,
                        state.System.Slots[0].CoreMergeAnimator.layerPriority,
                        state.System.Slots[1].CoreMergeAnimator.layerPriority,
                        state.System.Slots[0].TrackingMergeAnimator.layerPriority,
                        state.System.Slots[1].TrackingMergeAnimator.layerPriority,
                        state.System.Slots[0].PhantomViewMergeAnimator.layerPriority,
                        state.System.Slots[1].PhantomViewMergeAnimator.layerPriority
                    });
                Assert.IsTrue(state.System.Slots
                    .SelectMany(slot => new[]
                    {
                        slot.DriverNeutralMergeAnimator,
                        slot.SourceGestureMergeAnimator,
                        slot.SourceActionMergeAnimator,
                        slot.SourceFxMergeAnimator,
                        slot.CoreMergeAnimator,
                        slot.TrackingMergeAnimator,
                        slot.PhantomViewMergeAnimator
                    })
                    .All(merge => merge.layerType == VRCAvatarDescriptor.AnimLayerType.FX));
                Assert.IsFalse(state.Report.HasErrors);
            }
            finally
            {
                Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void MergeAnimatorFinalizer_ReportsPriorityOverflow()
        {
            var avatar = new GameObject("Avatar");
            var runtimeRoot = CreateChild(avatar.transform, "PhantomRuntime");
            var external = CreateChild(avatar.transform, "External")
                .AddComponent<ModularAvatarMergeAnimator>();
            external.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            external.layerPriority = int.MaxValue;
            var slot = new PhantomSlotBuildState
            {
                SlotId = "Slot1",
                DriverNeutralMergeAnimator = AddFxMergeAnimator(runtimeRoot, "Neutral")
            };
            var state = new PhantomBuildState
            {
                System = new PhantomSystemBuildState { RuntimeRoot = runtimeRoot }
            };
            state.System.Slots.Add(slot);
            var context = new BuildContext(avatar, null);
            try
            {
                PhantomMergeAnimatorFinalizer.Apply(context, state);

                Assert.AreEqual(int.MaxValue, slot.DriverNeutralMergeAnimator.layerPriority);
                Assert.IsTrue(state.Report.HasErrors);
                Assert.That(state.Report.Errors.Single(), Does.Contain("int.MaxValue"));
            }
            finally
            {
                Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void LayerControlRetargeter_ResolvesFxMarkersAndDisablesActionLayers()
        {
            var avatar = new GameObject("Avatar");
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            var baseState = new AnimatorState { name = "BaseState" };
            var gestureMarker = ScriptableObject.CreateInstance<PhantomAnimatorLayerControlMarker>();
            gestureMarker.targetPlayable = VRCAvatarDescriptor.AnimLayerType.FX;
            gestureMarker.targetLayerName = "GestureTarget";
            gestureMarker.goalWeight = 0.25f;
            var actionMarker = ScriptableObject.CreateInstance<PhantomAnimatorLayerControlMarker>();
            actionMarker.targetPlayable = VRCAvatarDescriptor.AnimLayerType.FX;
            actionMarker.targetLayerName = "ActionTarget";
            actionMarker.goalWeight = 1f;
            baseState.behaviours = new StateMachineBehaviour[] { gestureMarker, actionMarker };
            var baseMachine = CreateStateMachine("BaseMachine", baseState);
            var gestureMachine = CreateStateMachine("GestureMachine", new AnimatorState { name = "GestureState" });
            var actionMachine = CreateStateMachine("ActionMachine", new AnimatorState { name = "ActionState" });
            var fxController = new AnimatorController
            {
                name = "FinalFx",
                layers = new[]
                {
                    CreateControllerLayer("Base", baseMachine, 1f),
                    CreateControllerLayer("GestureTarget", gestureMachine, 1f),
                    CreateControllerLayer("ActionTarget", actionMachine, 1f)
                }
            };
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isDefault = false,
                    animatorController = fxController
                }
            };
            var slot = new PhantomSlotBuildState { SlotId = "Slot1" };
            slot.ConvertedActionLayers.Add(new PhantomConvertedActionLayer("ActionTarget", 1f));
            var state = new PhantomBuildState { System = new PhantomSystemBuildState() };
            state.System.Slots.Add(slot);
            var context = new BuildContext(avatar, null);
            try
            {
                PhantomAnimatorLayerControlRetargeter.Retarget(context, state);

                var controls = baseState.behaviours.OfType<VRCAnimatorLayerControl>().ToArray();
                Assert.AreEqual(2, controls.Length);
                Assert.IsTrue(controls.All(control =>
                    control.playable == VRC_AnimatorLayerControl.BlendableLayer.FX));
                Assert.AreEqual(1, controls.Single(control => Mathf.Approximately(control.goalWeight, 0.25f)).layer);
                Assert.AreEqual(2, controls.Single(control => Mathf.Approximately(control.goalWeight, 1f)).layer);
                Assert.IsFalse(baseState.behaviours.Any(behaviour =>
                    behaviour is PhantomAnimatorLayerControlMarker));
                Assert.AreEqual(0f, fxController.layers[2].defaultWeight);
                Assert.IsFalse(state.Report.HasErrors);
            }
            finally
            {
                DestroyControllerGraph(fxController, null);
                if (gestureMarker != null)
                {
                    Object.DestroyImmediate(gestureMarker);
                }
                if (actionMarker != null)
                {
                    Object.DestroyImmediate(actionMarker);
                }
                Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void FxBoneFilter_RemovesPoseCurvesAndPreservesNonPoseCurves()
        {
            var clone = new GameObject("Clone");
            var armature = CreateChild(clone.transform, "Armature");
            var hips = CreateChild(armature.transform, "Hips");
            var face = CreateChild(clone.transform, "Face");
            var clip = new AnimationClip { name = "MixedFx" };
            try
            {
                var slot = new PhantomSlotBuildState
                {
                    CloneRoot = clone,
                    CloneArmature = armature.transform
                };
                slot.CloneBones[HumanBodyBones.Hips] = hips.transform;
                var bonePaths = PhantomFxBoneAnimationFilter.CollectBonePaths(slot);
                var muscleBinding = EditorCurveBinding.FloatCurve(
                    string.Empty,
                    typeof(Animator),
                    HumanTrait.MuscleName.First());
                var parameterBinding = EditorCurveBinding.FloatCurve(
                    string.Empty,
                    typeof(Animator),
                    "FaceParameter");
                var boneBinding = EditorCurveBinding.FloatCurve(
                    "Armature/Hips",
                    typeof(Transform),
                    "m_LocalRotation.x");
                var nonBoneTransformBinding = EditorCurveBinding.FloatCurve(
                    "Face",
                    typeof(Transform),
                    "m_LocalScale.x");
                var blendShapeBinding = EditorCurveBinding.FloatCurve(
                    "Face",
                    typeof(SkinnedMeshRenderer),
                    "blendShape.Smile");
                AnimationUtility.SetEditorCurve(
                    clip,
                    muscleBinding,
                    AnimationCurve.Constant(0f, 2f, 0.5f));
                AnimationUtility.SetEditorCurve(
                    clip,
                    parameterBinding,
                    AnimationCurve.Constant(0f, 1f, 1f));
                AnimationUtility.SetEditorCurve(
                    clip,
                    boneBinding,
                    AnimationCurve.Constant(0f, 2f, 0.25f));
                AnimationUtility.SetEditorCurve(
                    clip,
                    nonBoneTransformBinding,
                    AnimationCurve.Constant(0f, 1f, 1.1f));
                AnimationUtility.SetEditorCurve(
                    clip,
                    blendShapeBinding,
                    AnimationCurve.Constant(0f, 1f, 100f));

                var result = PhantomFxBoneAnimationFilter.Filter(
                    clip,
                    bonePaths,
                    new HashSet<string> { "FaceParameter" });

                Assert.IsTrue(result.Changed);
                Assert.AreEqual(1, result.RemovedAnimatorCurves);
                Assert.AreEqual(1, result.RemovedTransformCurves);
                Assert.AreEqual(2f, result.OriginalLength);
                Assert.IsNull(AnimationUtility.GetEditorCurve(clip, muscleBinding));
                Assert.IsNull(AnimationUtility.GetEditorCurve(clip, boneBinding));
                Assert.IsNotNull(AnimationUtility.GetEditorCurve(clip, parameterBinding));
                Assert.IsNotNull(AnimationUtility.GetEditorCurve(clip, nonBoneTransformBinding));
                Assert.IsNotNull(AnimationUtility.GetEditorCurve(clip, blendShapeBinding));

                var dummyBinding = AnimationUtility.GetCurveBindings(clip)
                    .Single(PhantomFxBoneAnimationFilter.IsDummyBinding);
                var dummyCurve = AnimationUtility.GetEditorCurve(clip, dummyBinding);
                Assert.AreEqual(2f, dummyCurve.keys.Single().time);
                Assert.AreEqual(2f, clip.length);
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void FxBoneFilter_AllPoseCurvesBecomeDurationDummy()
        {
            var clip = new AnimationClip { name = "PoseOnly" };
            try
            {
                var rootBinding = EditorCurveBinding.FloatCurve(
                    string.Empty,
                    typeof(Transform),
                    "m_LocalPosition.x");
                AnimationUtility.SetEditorCurve(
                    clip,
                    rootBinding,
                    AnimationCurve.Constant(0f, 3f, 1f));

                var result = PhantomFxBoneAnimationFilter.Filter(
                    clip,
                    new HashSet<string>(),
                    new HashSet<string>());

                Assert.IsTrue(result.Changed);
                Assert.AreEqual(0, result.RemovedAnimatorCurves);
                Assert.AreEqual(1, result.RemovedTransformCurves);
                var remaining = AnimationUtility.GetCurveBindings(clip);
                Assert.AreEqual(1, remaining.Length);
                Assert.IsTrue(PhantomFxBoneAnimationFilter.IsDummyBinding(remaining[0]));
                Assert.AreEqual(
                    3f,
                    AnimationUtility.GetEditorCurve(clip, remaining[0]).keys.Single().time);
                Assert.AreEqual(3f, clip.length);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void FxBoneFilter_NoPoseCurvesLeavesClipUnchanged()
        {
            var clip = new AnimationClip { name = "BlendShapeOnly" };
            try
            {
                var binding = EditorCurveBinding.FloatCurve(
                    "Face",
                    typeof(SkinnedMeshRenderer),
                    "blendShape.Smile");
                AnimationUtility.SetEditorCurve(
                    clip,
                    binding,
                    AnimationCurve.Constant(0f, 1f, 100f));

                var result = PhantomFxBoneAnimationFilter.Filter(
                    clip,
                    new HashSet<string>(),
                    new HashSet<string>());

                Assert.IsFalse(result.Changed);
                CollectionAssert.AreEqual(
                    new[] { binding },
                    AnimationUtility.GetCurveBindings(clip));
                Assert.IsFalse(AnimationUtility.GetCurveBindings(clip)
                    .Any(PhantomFxBoneAnimationFilter.IsDummyBinding));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void FxBoneFilter_ZeroLengthPoseStillGetsDummyKey()
        {
            var clip = new AnimationClip { name = "ZeroLengthPose" };
            try
            {
                var binding = EditorCurveBinding.FloatCurve(
                    string.Empty,
                    typeof(Transform),
                    "m_LocalRotation.x");
                AnimationUtility.SetEditorCurve(
                    clip,
                    binding,
                    AnimationCurve.Constant(0f, 0f, 0f));

                var result = PhantomFxBoneAnimationFilter.Filter(
                    clip,
                    new HashSet<string>(),
                    new HashSet<string>());

                Assert.IsTrue(result.Changed);
                var remaining = AnimationUtility.GetCurveBindings(clip).Single();
                Assert.IsTrue(PhantomFxBoneAnimationFilter.IsDummyBinding(remaining));
                Assert.AreEqual(
                    0f,
                    AnimationUtility.GetEditorCurve(clip, remaining).keys.Single().time);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        private static PhantomSlotBuildState CreateSlotState(
            string id,
            GameObject cloneRoot,
            ModularAvatarMergeAnimator mergeAnimator,
            AnimatorController sourceController)
        {
            var slot = new PhantomSlot { id = id, tryConvertAnimatorTrackingControl = true };
            var state = new PhantomSlotBuildState
            {
                Slot = slot,
                SlotId = id,
                Identity = PhantomSlotIdentity.Create(slot),
                CloneRoot = cloneRoot,
                SourceFxMergeAnimator = mergeAnimator
            };
            state.SourcePlayableRegistrations[VRCAvatarDescriptor.AnimLayerType.FX] =
                new PhantomSourcePlayableRegistration
                {
                    Playable = VRCAvatarDescriptor.AnimLayerType.FX,
                    Source = new PhantomSourcePlayableLayer(
                        VRCAvatarDescriptor.AnimLayerType.FX,
                        sourceController,
                        null,
                        false),
                    BaseController = sourceController,
                    MergeAnimator = mergeAnimator
                };
            state.CloneToAnimationDriverPaths["Bone"] = "Driver";
            state.ParameterResolution = new PhantomSlotParameterResolution();
            state.ParameterResolution.FinalNames["AudioIndex"] =
                $"PhantomSystem/{id}/Original/AudioIndex";
            return state;
        }

        private static ModularAvatarMergeAnimator AddMergeAnimator(
            GameObject avatar,
            GameObject cloneRoot,
            RuntimeAnimatorController controller)
        {
            var host = CreateChild(avatar.transform, "Merge_" + cloneRoot.name);
            var merge = host.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative;
            merge.relativePathRoot = new AvatarObjectReference();
            merge.relativePathRoot.Set(cloneRoot);
            return merge;
        }

        private static ModularAvatarMergeAnimator AddFxMergeAnimator(
            GameObject parent,
            string name)
        {
            var host = CreateChild(parent.transform, name);
            var merge = host.AddComponent<ModularAvatarMergeAnimator>();
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            return merge;
        }

        private static AnimatorStateMachine CreateStateMachine(
            string name,
            AnimatorState state)
        {
            var machine = new AnimatorStateMachine { name = name };
            machine.states = new[] { new ChildAnimatorState { state = state } };
            machine.defaultState = state;
            return machine;
        }

        private static AnimatorControllerLayer CreateControllerLayer(
            string name,
            AnimatorStateMachine machine,
            float defaultWeight)
        {
            return new AnimatorControllerLayer
            {
                name = name,
                stateMachine = machine,
                defaultWeight = defaultWeight,
                syncedLayerIndex = -1
            };
        }

        private static void AssertTrackingDriver(
            VirtualAnimatorController controller,
            PhantomSlot slot)
        {
            var state = controller.Layers.Single().StateMachine.AllStates().Single();
            var driver = state.Behaviours.OfType<VRCAvatarParameterDriver>().Single();
            Assert.IsFalse(state.Behaviours.Any(behaviour =>
                behaviour is VRCAnimatorTrackingControl));
            CollectionAssert.Contains(
                driver.parameters.Select(parameter => parameter.name),
                PhantomTrackingControlGroups.Parameter(slot, PhantomTrackingControlGroup.Head));
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name.Replace('/', '_'));
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void DestroyControllerGraph(
            RuntimeAnimatorController controller,
            RuntimeAnimatorController excluded)
        {
            if (controller == null || controller == excluded)
            {
                return;
            }

            var destroyed = new HashSet<Object>();
            if (controller is AnimatorController animatorController)
            {
                foreach (var clip in animatorController.animationClips)
                {
                    if (clip != null && destroyed.Add(clip))
                    {
                        Object.DestroyImmediate(clip);
                    }
                }
                foreach (var layer in animatorController.layers)
                {
                    DestroyStateMachine(layer.stateMachine, destroyed);
                }
            }
            if (destroyed.Add(controller))
            {
                Object.DestroyImmediate(controller);
            }
        }

        private static void DestroyStateMachine(
            AnimatorStateMachine machine,
            ISet<Object> destroyed)
        {
            if (machine == null || !destroyed.Add(machine))
            {
                return;
            }
            foreach (var child in machine.states)
            {
                if (child.state == null || !destroyed.Add(child.state))
                {
                    continue;
                }
                foreach (var behaviour in child.state.behaviours)
                {
                    if (behaviour != null && destroyed.Add(behaviour))
                    {
                        Object.DestroyImmediate(behaviour);
                    }
                }
                Object.DestroyImmediate(child.state);
            }
            foreach (var child in machine.stateMachines)
            {
                DestroyStateMachine(child.stateMachine, destroyed);
            }
            Object.DestroyImmediate(machine);
        }
    }
}
