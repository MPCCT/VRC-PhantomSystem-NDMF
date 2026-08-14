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

                Assert.AreEqual(
                    "Clone1/Driver",
                    ((VirtualClip)virtual1.Layers.Single().StateMachine.AllStates().Single().Motion)
                    .GetFloatCurveBindings().Single().path);
                Assert.AreEqual(
                    "Clone2/Driver",
                    ((VirtualClip)virtual2.Layers.Single().StateMachine.AllStates().Single().Motion)
                    .GetFloatCurveBindings().Single().path);
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
