using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Dynamics.Contact.Components;

namespace MPCCT.PhantomSystem.Editor.Tests
{
    public sealed class PhantomValidationAndParameterTests
    {
        [Test]
        public void SlotIdentity_UsesDefaultForWhitespace()
        {
            var identity = PhantomSlotIdentity.Create(new PhantomSlot { id = "  " });

            Assert.AreEqual(PhantomSlot.DefaultId, identity.SlotId);
            Assert.AreEqual("PhantomSystem/Slot1", identity.ParameterPrefix);
        }

        [Test]
        public void SlotIdentity_IsCaseSensitiveAndNormalizesHierarchyCharacters()
        {
            var upper = PhantomSlotIdentity.Create(new PhantomSlot { id = "SlotA" });
            var lower = PhantomSlotIdentity.Create(new PhantomSlot { id = "slota" });
            var invalid = PhantomSlotIdentity.Create(new PhantomSlot { id = "A/B" });
            var normalized = PhantomSlotIdentity.Create(new PhantomSlot { id = "A_B" });

            Assert.AreNotEqual(upper.SlotId, lower.SlotId);
            Assert.AreEqual(invalid.HierarchyName, normalized.HierarchyName);
        }

        [Test]
        public void Validation_NullSlotAndExplicitDefaultCollisionAreReportedWithoutDereference()
        {
            var report = ValidateSlots(null, new PhantomSlot { id = "Slot1" });

            CollectionAssert.Contains(report.Slots[0].Issues.Select(issue => issue.Code), "PHS001");
            CollectionAssert.Contains(report.Slots[0].Issues.Select(issue => issue.Code), "PHS030");
            CollectionAssert.Contains(report.Slots[1].Issues.Select(issue => issue.Code), "PHS030");
        }

        [Test]
        public void Validation_UsesOrdinalIdsButRejectsSafeNameAndPrefixCollisions()
        {
            var report = ValidateSlots(
                new PhantomSlot { id = "SlotA" },
                new PhantomSlot { id = "slota" },
                new PhantomSlot { id = "A/B" },
                new PhantomSlot { id = "A_B" },
                new PhantomSlot { id = "One", parameterPrefix = "Shared" },
                new PhantomSlot { id = "Two", parameterPrefix = "Shared" });

            Assert.IsFalse(report.Slots[0].Issues.Any(issue => issue.Code == "PHS030"));
            Assert.IsFalse(report.Slots[1].Issues.Any(issue => issue.Code == "PHS030"));
            Assert.IsTrue(report.Slots[2].Issues.Any(issue => issue.Code == "PHS031"));
            Assert.IsTrue(report.Slots[3].Issues.Any(issue => issue.Code == "PHS031"));
            Assert.IsTrue(report.Slots[4].Issues.Any(issue => issue.Code == "PHS032"));
            Assert.IsTrue(report.Slots[5].Issues.Any(issue => issue.Code == "PHS032"));
        }

        [Test]
        public void AddSlot_ExplicitDefaultsMatchNewPhantomSlot()
        {
            var root = new GameObject("Authoring");
            UnityEditor.Editor inspector = null;
            try
            {
                var authoring = root.AddComponent<PhantomSystem>();
                authoring.slots.Clear();
                inspector = UnityEditor.Editor.CreateEditor(authoring);
                var addSlot = inspector.GetType().GetMethod(
                    "AddSlot",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                Assert.IsNotNull(addSlot);
                addSlot.Invoke(inspector, null);
                inspector.serializedObject.ApplyModifiedProperties();

                var expected = new PhantomSlot();
                var actual = authoring.slots.Single();
                Assert.AreEqual(expected.id, actual.id);
                Assert.AreEqual(expected.phantomAvatar, actual.phantomAvatar);
                Assert.AreEqual(expected.spawnPositionOverride, actual.spawnPositionOverride);
                Assert.AreEqual(expected.includePhantomMenu, actual.includePhantomMenu);
                Assert.AreEqual(expected.parameterPrefix, actual.parameterPrefix);
                Assert.AreEqual(expected.renamePhantomParameters, actual.renamePhantomParameters);
                Assert.AreEqual(expected.removeSourceControls, actual.removeSourceControls);
                Assert.AreEqual(expected.useRotationConstraint, actual.useRotationConstraint);
                Assert.AreEqual(expected.rotationSolveInWorldSpace, actual.rotationSolveInWorldSpace);
                Assert.AreEqual(expected.overridePhysBoneImmobileType, actual.overridePhysBoneImmobileType);
                Assert.AreEqual(expected.tryConvertAnimatorTrackingControl, actual.tryConvertAnimatorTrackingControl);
                Assert.AreEqual(expected.enablePhantomGrabbing, actual.enablePhantomGrabbing);
                Assert.AreEqual(expected.enableScaleControl, actual.enableScaleControl);
                Assert.AreEqual(expected.enablePhantomView, actual.enablePhantomView);
                Assert.AreEqual(
                    expected.phantomViewNearClipPlane,
                    actual.phantomViewNearClipPlane);
                Assert.IsNotNull(actual.sharedParameterNames);
                Assert.IsEmpty(actual.sharedParameterNames);
            }
            finally
            {
                if (inspector != null)
                {
                    Object.DestroyImmediate(inspector);
                }
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void InspectorAnalysis_ReplacesAndDisposesPreviewContexts()
        {
            var root = new GameObject("Authoring");
            UnityEditor.Editor inspector = null;
            ComputeContext firstContext = null;
            ComputeContext secondContext = null;
            try
            {
                var authoring = root.AddComponent<PhantomSystem>();
                inspector = UnityEditor.Editor.CreateEditor(authoring);
                var editorType = inspector.GetType();
                var refresh = editorType.GetMethod(
                    "RefreshAnalysis",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var contextField = editorType.GetField(
                    "analysisContext",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var subscriptionField = editorType.GetField(
                    "analysisInvalidationSubscription",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var refreshPendingField = editorType.GetField(
                    "refreshPending",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                Assert.IsNotNull(refresh);
                Assert.IsNotNull(contextField);
                Assert.IsNotNull(subscriptionField);
                Assert.IsNotNull(refreshPendingField);

                refresh.Invoke(inspector, null);
                firstContext = contextField.GetValue(inspector) as ComputeContext;
                Assert.IsNotNull(firstContext);
                Assert.IsFalse(firstContext.IsInvalidated);
                Assert.IsNotNull(subscriptionField.GetValue(inspector));

                firstContext.Invalidate();
                Assert.DoesNotThrow(ComputeContext.FlushInvalidates);
                Assert.IsNull(subscriptionField.GetValue(inspector));
                Assert.IsTrue((bool)refreshPendingField.GetValue(inspector));

                refresh.Invoke(inspector, null);
                secondContext = contextField.GetValue(inspector) as ComputeContext;
                Assert.IsNotNull(secondContext);
                Assert.AreNotSame(firstContext, secondContext);
                Assert.IsTrue(firstContext.IsInvalidated);
                Assert.IsFalse(secondContext.IsInvalidated);

                Object.DestroyImmediate(inspector);
                inspector = null;
                Assert.IsTrue(secondContext.IsInvalidated);
            }
            finally
            {
                if (inspector != null)
                {
                    Object.DestroyImmediate(inspector);
                }
                firstContext?.Invalidate();
                secondContext?.Invalidate();
                ComputeContext.FlushInvalidates();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PhantomViewNearClip_NormalizesInvalidValues()
        {
            Assert.AreEqual(
                PhantomSlot.DefaultPhantomViewNearClipPlane,
                PhantomViewBuilder.NormalizeNearClipPlane(float.NaN));
            Assert.AreEqual(
                PhantomSlot.DefaultPhantomViewNearClipPlane,
                PhantomViewBuilder.NormalizeNearClipPlane(0f));
            Assert.AreEqual(
                PhantomSlot.MinimumPhantomViewNearClipPlane,
                PhantomViewBuilder.NormalizeNearClipPlane(0.0001f));
            Assert.AreEqual(
                PhantomSlot.MaximumPhantomViewNearClipPlane,
                PhantomViewBuilder.NormalizeNearClipPlane(100f));
        }

        [Test]
        public void PhantomViewControls_UseHierarchyScaleForStereoAndAnimatorScaleForNearClip()
        {
            var avatar = new GameObject("Avatar");
            var slotRoot = new GameObject("Slot1");
            var cloneRoot = new GameObject("Clone");
            var captureRoot = new GameObject("CaptureRoot");
            var leftCamera = new GameObject("LeftCamera");
            var rightCamera = new GameObject("RightCamera");
            var display = new GameObject("Display");
            AnimatorController controller = null;
            PhantomAnimatorBuildContext context = null;
            try
            {
                slotRoot.transform.SetParent(avatar.transform, false);
                cloneRoot.transform.SetParent(slotRoot.transform, false);
                captureRoot.transform.SetParent(slotRoot.transform, false);
                leftCamera.transform.SetParent(captureRoot.transform, false);
                rightCamera.transform.SetParent(captureRoot.transform, false);
                display.transform.SetParent(avatar.transform, false);

                var slot = new PhantomSlot
                {
                    id = "Slot1",
                    enableScaleControl = true,
                    enablePhantomView = true,
                    phantomViewNearClipPlane = 0.1f
                };
                var slotState = new PhantomSlotBuildState
                {
                    Slot = slot,
                    SlotId = "Slot1",
                    Identity = PhantomSlotIdentity.Create(slot),
                    SlotRoot = slotRoot,
                    CloneRoot = cloneRoot,
                    PhantomViewLeftCamera = leftCamera.transform,
                    PhantomViewRightCamera = rightCamera.transform,
                    PhantomViewDisplayHost = display.transform
                };
                var system = new PhantomSystemBuildState
                {
                    AvatarRoot = avatar.transform
                };
                system.Slots.Add(slotState);
                controller = new AnimatorController
                {
                    layers = new AnimatorControllerLayer[0]
                };
                context = new PhantomAnimatorBuildContext(
                    null,
                    system,
                    slotState,
                    new PhantomBuildReport(),
                    controller);

                PhantomViewAnimatorModule.BuildControls(context);

                var controlsLayer = controller.layers.Single(layer =>
                    layer.name.EndsWith("_PhantomViewControls", System.StringComparison.Ordinal));
                var directTree = controlsLayer.stateMachine.defaultState.motion as BlendTree;
                Assert.IsNotNull(directTree);
                Assert.AreEqual(BlendTreeType.Direct, directTree.blendType);
                Assert.AreEqual(3, directTree.children.Length);
                Assert.IsTrue(directTree.children.All(child =>
                    child.directBlendParameter
                    == PhantomParameterNames.PhantomViewDirectWeight(slot)));

                var stereoTree = directTree.children
                    .Select(child => child.motion)
                    .OfType<BlendTree>()
                    .Single(tree => tree.name == "PhantomViewStereoStrengthTree");
                Assert.AreEqual(BlendTreeType.Simple1D, stereoTree.blendType);
                Assert.AreEqual(
                    PhantomParameterNames.PhantomViewStereoStrength(slot),
                    stereoTree.blendParameter);
                Assert.IsFalse(context.GeneratedBlendTrees.Any(tree =>
                    tree.name == "PhantomViewStereoStrengthScaleTree"));

                var leftPositionBinding = new EditorCurveBinding
                {
                    path = context.PhantomViewLeftCameraPath,
                    type = typeof(Transform),
                    propertyName = "m_LocalPosition.x"
                };
                var maximumStereoCurve = AnimationUtility.GetEditorCurve(
                    (AnimationClip)stereoTree.children[1].motion,
                    leftPositionBinding);
                Assert.AreEqual(-0.05f, maximumStereoCurve.Evaluate(0f), 0.000001f);

                var nearClipTree = directTree.children
                    .Select(child => child.motion)
                    .OfType<BlendTree>()
                    .Single(tree => tree.name == "PhantomViewNearClipScaleTree");
                Assert.AreEqual(BlendTreeType.Simple1D, nearClipTree.blendType);
                Assert.AreEqual(PhantomParameterNames.Scale(slot), nearClipTree.blendParameter);

                var nearClipBinding = new EditorCurveBinding
                {
                    path = context.PhantomViewLeftCameraPath,
                    type = typeof(Camera),
                    propertyName = "near clip plane"
                };
                var minimumCurve = AnimationUtility.GetEditorCurve(
                    (AnimationClip)nearClipTree.children[0].motion,
                    nearClipBinding);
                var maximumCurve = AnimationUtility.GetEditorCurve(
                    (AnimationClip)nearClipTree.children[1].motion,
                    nearClipBinding);
                Assert.AreEqual(0.1f * ScaleControlAnimatorModule.MinimumScale,
                    minimumCurve.Evaluate(0f), 0.000001f);
                Assert.AreEqual(0.1f * ScaleControlAnimatorModule.MaximumScale,
                    maximumCurve.Evaluate(0f), 0.000001f);
            }
            finally
            {
                if (context != null)
                {
                    foreach (var clip in context.GeneratedClips)
                    {
                        Object.DestroyImmediate(clip);
                    }
                    foreach (var tree in context.GeneratedBlendTrees)
                    {
                        Object.DestroyImmediate(tree);
                    }
                }
                if (controller != null)
                {
                    Object.DestroyImmediate(controller);
                }
                Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void CoreParameterCatalog_PreservesExposedAndControllerTypes()
        {
            var slot = new PhantomSlot
            {
                id = "Slot1",
                enableScaleControl = true,
                enablePhantomGrabbing = true,
                enablePhantomView = true,
                tryConvertAnimatorTrackingControl = true
            };
            var entries = PhantomCoreParameterCatalog.ForSlot(slot);
            var mirror = entries.Single(entry =>
                entry.Parameter.Name == PhantomParameterNames.Mirror(slot));
            var viewEnabled = entries.Single(entry =>
                entry.Parameter.Name == PhantomParameterNames.PhantomViewEnabled(slot));

            Assert.AreEqual(AnimatorControllerParameterType.Bool, mirror.Parameter.ParameterType);
            Assert.AreEqual(AnimatorControllerParameterType.Float, mirror.ControllerParameterType);
            Assert.IsTrue(mirror.Parameter.WantSynced);
            Assert.IsFalse(mirror.Parameter.IsAnimatorOnly);
            Assert.IsFalse(viewEnabled.Parameter.WantSynced);
            Assert.IsFalse(viewEnabled.Parameter.IsAnimatorOnly);
        }

        [Test]
        public void ScaleControl_UsesPositiveScaleAndSeparateMirrorTreesInsideDirectBlendTree()
        {
            var avatar = new GameObject("Avatar");
            var slotRoot = new GameObject("Slot1");
            var mirrorRoot = new GameObject("MirrorRoot");
            var cloneRoot = new GameObject("Clone");
            AnimatorController controller = null;
            PhantomAnimatorBuildContext context = null;
            try
            {
                slotRoot.transform.SetParent(avatar.transform, false);
                mirrorRoot.transform.SetParent(slotRoot.transform, false);
                cloneRoot.transform.SetParent(mirrorRoot.transform, false);

                var slot = new PhantomSlot
                {
                    id = "Slot1",
                    enableScaleControl = true
                };
                var slotState = new PhantomSlotBuildState
                {
                    Slot = slot,
                    SlotId = "Slot1",
                    Identity = PhantomSlotIdentity.Create(slot),
                    SlotRoot = slotRoot,
                    MirrorRoot = mirrorRoot,
                    CloneRoot = cloneRoot
                };
                var system = new PhantomSystemBuildState
                {
                    AvatarRoot = avatar.transform
                };
                system.Slots.Add(slotState);
                controller = new AnimatorController
                {
                    layers = new AnimatorControllerLayer[0]
                };
                context = new PhantomAnimatorBuildContext(
                    null,
                    system,
                    slotState,
                    new PhantomBuildReport(),
                    controller);

                ScaleControlAnimatorModule.Build(context);

                var layer = controller.layers.Single(candidate =>
                    candidate.name.EndsWith("_PhantomScaleControl", System.StringComparison.Ordinal));
                var direct = layer.stateMachine.defaultState.motion as BlendTree;
                Assert.IsNotNull(direct);
                Assert.AreEqual(BlendTreeType.Direct, direct.blendType);
                Assert.AreEqual(2, direct.children.Length);
                Assert.IsTrue(direct.children.All(child =>
                    child.directBlendParameter == PhantomParameterNames.ScaleDirectWeight(slot)));
                Assert.IsTrue(layer.stateMachine.defaultState.writeDefaultValues);

                var scaleTree = direct.children
                    .Select(child => child.motion)
                    .OfType<BlendTree>()
                    .Single(tree => tree.name == "PhantomScaleTree");
                var mirrorTree = direct.children
                    .Select(child => child.motion)
                    .OfType<BlendTree>()
                    .Single(tree => tree.name == "PhantomMirrorTree");
                Assert.AreEqual(PhantomParameterNames.Scale(slot), scaleTree.blendParameter);
                Assert.AreEqual(PhantomParameterNames.Mirror(slot), mirrorTree.blendParameter);
                Assert.AreEqual(
                    AnimatorControllerParameterType.Float,
                    controller.parameters.Single(parameter =>
                        parameter.name == PhantomParameterNames.Mirror(slot)).type);

                var scaleBinding = EditorCurveBinding.FloatCurve(
                    context.SlotPath,
                    typeof(Transform),
                    "m_LocalScale.x");
                Assert.AreEqual(
                    ScaleControlAnimatorModule.MinimumScale,
                    AnimationUtility.GetEditorCurve(
                        (AnimationClip)scaleTree.children[0].motion,
                        scaleBinding).Evaluate(0f),
                    0.000001f);
                Assert.AreEqual(
                    ScaleControlAnimatorModule.MaximumScale,
                    AnimationUtility.GetEditorCurve(
                        (AnimationClip)scaleTree.children[1].motion,
                        scaleBinding).Evaluate(0f),
                    0.000001f);

                var mirrorBinding = EditorCurveBinding.FloatCurve(
                    context.MirrorPath,
                    typeof(Transform),
                    "m_LocalScale.x");
                Assert.AreEqual(
                    1f,
                    AnimationUtility.GetEditorCurve(
                        (AnimationClip)mirrorTree.children[0].motion,
                        mirrorBinding).Evaluate(0f),
                    0.000001f);
                Assert.AreEqual(
                    -1f,
                    AnimationUtility.GetEditorCurve(
                        (AnimationClip)mirrorTree.children[1].motion,
                        mirrorBinding).Evaluate(0f),
                    0.000001f);
            }
            finally
            {
                if (context != null)
                {
                    foreach (var clip in context.GeneratedClips)
                    {
                        Object.DestroyImmediate(clip);
                    }
                    foreach (var tree in context.GeneratedBlendTrees)
                    {
                        Object.DestroyImmediate(tree);
                    }
                }
                if (controller != null)
                {
                    Object.DestroyImmediate(controller);
                }
                Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void ParameterCompatibility_RequiresMatchingBehavior()
        {
            var left = Definition("Value", AnimatorControllerParameterType.Bool, true, 0f, false);
            var right = Definition("Value", AnimatorControllerParameterType.Bool, true, 0f, false);

            Assert.IsTrue(PhantomParameterCompatibility.AreCompatible(left, right, out _));

            right.WantSynced = false;
            Assert.IsFalse(PhantomParameterCompatibility.AreCompatible(left, right, out var reason));
            StringAssert.Contains("sync", reason);
        }

        [Test]
        public void ParameterCompatibility_RejectsKnownDefaultAndSavedDifferences()
        {
            var left = Definition("Value", AnimatorControllerParameterType.Float, true, 0f, false);
            var right = Definition("Value", AnimatorControllerParameterType.Float, true, 1f, false);
            Assert.IsFalse(PhantomParameterCompatibility.AreCompatible(left, right, out var defaultReason));
            StringAssert.Contains("default", defaultReason);

            right.DefaultValue = left.DefaultValue;
            right.Saved = true;
            Assert.IsFalse(PhantomParameterCompatibility.AreCompatible(left, right, out var savedReason));
            StringAssert.Contains("saved", savedReason);
        }

        [Test]
        public void ParameterCompatibility_RejectsTypeAnimatorOnlyAndHiddenDifferences()
        {
            var left = Definition("Value", AnimatorControllerParameterType.Bool, true, 0f, false);
            var right = Definition("Value", AnimatorControllerParameterType.Int, true, 0f, false);
            Assert.IsFalse(PhantomParameterCompatibility.AreCompatible(left, right, out var typeReason));
            StringAssert.Contains("type", typeReason);

            right.ParameterType = left.ParameterType;
            right.IsAnimatorOnly = true;
            Assert.IsFalse(PhantomParameterCompatibility.AreCompatible(left, right, out var animatorReason));
            StringAssert.Contains("animator-only", animatorReason);

            right.IsAnimatorOnly = false;
            right.IsHidden = true;
            Assert.IsFalse(PhantomParameterCompatibility.AreCompatible(left, right, out var hiddenReason));
            StringAssert.Contains("hidden", hiddenReason);
        }

        [Test]
        public void ParameterResolver_SharesCompatibleOriginalName()
        {
            var source = Definition("Shared", AnimatorControllerParameterType.Bool, true, 0f, false);
            var resolution = Resolve(
                new Dictionary<string, PhantomParameterDefinition>
                {
                    ["Shared"] = Definition("Shared", AnimatorControllerParameterType.Bool, true, 0f, false)
                },
                new PhantomSlot { id = "Slot1", renamePhantomParameters = false },
                source);

            Assert.AreEqual("Shared", resolution.FinalNames["Shared"]);
            Assert.Contains("Shared", resolution.SharedOriginalNames.ToList());
            Assert.AreEqual(0, resolution.SourceParameterCost - resolution.SharedParameterSavings);
        }

        [Test]
        public void ParameterResolver_RenamesIncompatibleOriginalNameDeterministically()
        {
            var slot = new PhantomSlot { id = "Slot1", renamePhantomParameters = false };
            var fallback = PhantomSlotIdentity.Create(slot).OriginalParameterName("Shared");
            var baseParameters = new Dictionary<string, PhantomParameterDefinition>
            {
                ["Shared"] = Definition("Shared", AnimatorControllerParameterType.Int, true, 0f, false),
                [fallback] = Definition(fallback, AnimatorControllerParameterType.Int, true, 0f, false),
                [fallback + "~2"] = Definition(fallback + "~2", AnimatorControllerParameterType.Int, true, 0f, false)
            };

            var resolution = Resolve(
                baseParameters,
                slot,
                Definition("Shared", AnimatorControllerParameterType.Bool, true, 0f, false));

            Assert.AreEqual(fallback + "~3", resolution.FinalNames["Shared"]);
            Assert.AreEqual(1, resolution.AutomaticRenames.Count);
        }

        [Test]
        public void ParameterResolver_NamespacesByDefaultAndKeepsValidatedExplicitShare()
        {
            var namespaced = new PhantomSlot { id = "Slot1", renamePhantomParameters = true };
            var isolated = Resolve(
                new Dictionary<string, PhantomParameterDefinition>(),
                namespaced,
                Definition("Value", AnimatorControllerParameterType.Bool, true, 0f, false));
            Assert.AreEqual(
                "PhantomSystem/Slot1/Original/Value",
                isolated.FinalNames["Value"]);

            namespaced.sharedParameterNames.Add("Value");
            var shared = Resolve(
                new Dictionary<string, PhantomParameterDefinition>
                {
                    ["Value"] = Definition("Value", AnimatorControllerParameterType.Bool, true, 0f, false)
                },
                namespaced,
                Definition("Value", AnimatorControllerParameterType.Bool, true, 0f, false));
            Assert.AreEqual("Value", shared.FinalNames["Value"]);
            CollectionAssert.Contains(shared.SharedOriginalNames, "Value");
        }

        [Test]
        public void ParameterConfigBuilder_ConsumesResolvedSlotPlan()
        {
            var slot = new PhantomSlot { id = "Slot1", renamePhantomParameters = true };
            var source = Definition(
                "SourceToggle",
                AnimatorControllerParameterType.Bool,
                true,
                1f,
                true);
            var plan = PhantomParameterPlanner.Create(
                new Dictionary<string, PhantomParameterDefinition>(),
                new[]
                {
                    new PhantomParameterSlotInput
                    {
                        Slot = slot,
                        Identity = PhantomSlotIdentity.Create(slot),
                        SourceParameters = new[] { source }
                    }
                });
            var state = new PhantomSlotBuildState
            {
                Slot = slot,
                SlotId = "Slot1",
                Identity = PhantomSlotIdentity.Create(slot),
                ParameterPlan = plan.Slots[0]
            };

            var config = PhantomParameterConfigBuilder.Build(state).Single();

            Assert.AreEqual("SourceToggle", config.nameOrPrefix);
            Assert.AreEqual(
                "PhantomSystem/Slot1/Original/SourceToggle",
                config.remapTo);
            Assert.AreEqual(1f, config.defaultValue);
            Assert.IsTrue(config.saved);
            Assert.IsFalse(config.localOnly);
        }

        [Test]
        public void ParameterResolver_KeepsNamesAlreadyInTheSlotNamespace()
        {
            var slot = new PhantomSlot { id = "Slot1", renamePhantomParameters = true };
            const string name = "PhantomSystem/Slot1/AlreadyResolved";

            var resolution = Resolve(
                new Dictionary<string, PhantomParameterDefinition>(),
                slot,
                Definition(name, AnimatorControllerParameterType.Bool, false, 0f, false));

            Assert.AreEqual(name, resolution.FinalNames[name]);
        }

        [Test]
        public void ParameterResolver_SharesCompatibleNamesAcrossSlotsInStableOrder()
        {
            var first = new PhantomSlot { id = "One", renamePhantomParameters = false };
            var second = new PhantomSlot { id = "Two", renamePhantomParameters = false };
            var definition = Definition("Shared", AnimatorControllerParameterType.Bool, true, 0f, false);
            var result = PhantomParameterResolver.Resolve(
                new Dictionary<string, PhantomParameterDefinition>(),
                new[]
                {
                    new PhantomParameterSlotInput
                    {
                        Slot = first,
                        Identity = PhantomSlotIdentity.Create(first),
                        SourceParameters = new[] { definition }
                    },
                    new PhantomParameterSlotInput
                    {
                        Slot = second,
                        Identity = PhantomSlotIdentity.Create(second),
                        SourceParameters = new[]
                        {
                            Definition("Shared", AnimatorControllerParameterType.Bool, true, 0f, false)
                        }
                    }
                });

            Assert.AreEqual("Shared", result.Slots[0].FinalNames["Shared"]);
            Assert.AreEqual("Shared", result.Slots[1].FinalNames["Shared"]);
            CollectionAssert.Contains(result.Slots[1].SharedOriginalNames, "Shared");
        }

        [Test]
        public void ParameterResolver_CountsGrabbingAndScaleBits()
        {
            var slot = new PhantomSlot
            {
                id = "Slot1",
                enablePhantomGrabbing = true,
                enableScaleControl = true,
                enablePhantomView = true,
                removeSourceControls = true
            };

            var result = PhantomParameterResolver.Resolve(
                new Dictionary<string, PhantomParameterDefinition>(),
                new[]
                {
                    new PhantomParameterSlotInput
                    {
                        Slot = slot,
                        Identity = PhantomSlotIdentity.Create(slot)
                    }
                });

            Assert.AreEqual(13, result.Slots[0].GeneratedParameterCost);
        }

        [TestCase(false, false, false, 3)]
        [TestCase(false, false, true, 3)]
        [TestCase(true, false, false, 4)]
        [TestCase(false, true, false, 12)]
        [TestCase(true, true, true, 13)]
        public void ParameterResolver_CoreBitCostUsesOnlySyncedFeatures(
            bool grabbing,
            bool scale,
            bool view,
            int expected)
        {
            var slot = new PhantomSlot
            {
                id = "Slot1",
                enablePhantomGrabbing = grabbing,
                enableScaleControl = scale,
                enablePhantomView = view,
                removeSourceControls = true
            };

            var result = PhantomParameterResolver.Resolve(
                new Dictionary<string, PhantomParameterDefinition>(),
                new[] { Input(slot) });

            Assert.AreEqual(expected, result.Slots[0].GeneratedParameterCost);
        }

        [Test]
        public void ParameterResolver_ReusesCompatibleBaseCoreAndRejectsIncompatibleBaseCore()
        {
            var slot = new PhantomSlot
            {
                id = "Slot1",
                enablePhantomGrabbing = false,
                enableScaleControl = false,
                enablePhantomView = false,
                removeSourceControls = true
            };
            var activate = PhantomParameterNames.Activate(slot);
            var compatibleBase = new Dictionary<string, PhantomParameterDefinition>
            {
                [activate] = Definition(activate, AnimatorControllerParameterType.Bool, true, 0f, false)
            };
            var compatible = PhantomParameterResolver.Resolve(compatibleBase, new[] { Input(slot) });
            Assert.IsEmpty(compatible.Errors);
            Assert.AreEqual(2, compatible.Slots[0].GeneratedParameterCost);

            compatibleBase[activate] = Definition(
                activate,
                AnimatorControllerParameterType.Int,
                true,
                0f,
                false);
            var incompatible = PhantomParameterResolver.Resolve(compatibleBase, new[] { Input(slot) });
            Assert.IsNotEmpty(incompatible.Errors);
            StringAssert.Contains("base avatar", incompatible.Errors.Single());
        }

        [Test]
        public void ParameterResolver_RejectsDuplicateCoreNamespace()
        {
            var first = new PhantomSlot { id = "One", parameterPrefix = "Same" };
            var second = new PhantomSlot { id = "Two", parameterPrefix = "Same" };
            var result = PhantomParameterResolver.Resolve(
                new Dictionary<string, PhantomParameterDefinition>(),
                new[]
                {
                    Input(first),
                    Input(second)
                });

            Assert.IsNotEmpty(result.Errors);
            StringAssert.Contains("same core parameter", result.Errors[0]);
        }

        [Test]
        public void ParameterResolver_RenamesConflictingPhysBonePrefixAndDerivedNames()
        {
            var slot = new PhantomSlot { id = "Slot1", renamePhantomParameters = false };
            var prefix = new PhantomParameterDefinition
            {
                Name = "PB",
                IsPhysBonePrefix = true,
                IsAnimatorOnly = true
            };
            var baseParameters = new Dictionary<string, PhantomParameterDefinition>
            {
                ["PB_IsGrabbed"] = Definition(
                    "PB_IsGrabbed",
                    AnimatorControllerParameterType.Bool,
                    false,
                    0f,
                    false)
            };

            var resolution = Resolve(baseParameters, slot, prefix);

            Assert.AreEqual("PhantomSystem/Slot1/Original/PB", resolution.FinalNames["PB"]);
            Assert.AreEqual(
                "PhantomSystem/Slot1/Original/PB_IsGrabbed",
                resolution.FinalNames["PB_IsGrabbed"]);
        }

        [Test]
        public void ParameterResolver_RenamesConflictingRaycastPrefixAndDerivedNames()
        {
            var slot = new PhantomSlot { id = "Slot1", renamePhantomParameters = false };
            var prefix = new PhantomParameterDefinition
            {
                Name = "Ray",
                IsRaycastPrefix = true,
                IsAnimatorOnly = true
            };
            var baseParameters = new Dictionary<string, PhantomParameterDefinition>
            {
                ["Ray_Hit"] = Definition(
                    "Ray_Hit",
                    AnimatorControllerParameterType.Int,
                    false,
                    0f,
                    false)
            };

            var resolution = Resolve(baseParameters, slot, prefix);

            Assert.AreEqual("PhantomSystem/Slot1/Original/Ray", resolution.FinalNames["Ray"]);
            Assert.AreEqual(
                "PhantomSystem/Slot1/Original/Ray_Hit",
                resolution.FinalNames["Ray_Hit"]);
            Assert.AreEqual(
                "PhantomSystem/Slot1/Original/Ray_Ratio",
                resolution.FinalNames["Ray_Ratio"]);
            Assert.AreEqual(
                "PhantomSystem/Slot1/Original/Ray_Distance",
                resolution.FinalNames["Ray_Distance"]);
            Assert.AreEqual(
                "PhantomSystem/Slot1/Original/Ray_IsGrabbed",
                resolution.FinalNames["Ray_IsGrabbed"]);
        }

        [Test]
        public void SourceComponentMapping_OnlyChangesContactsCapturedBeforeRigGeneration()
        {
            var cloneRoot = new GameObject("CloneRoot");
            try
            {
                var sourceContact = cloneRoot.AddComponent<VRCContactReceiver>();
                sourceContact.parameter = "ContactValue";
                var slot = new PhantomSlot
                {
                    id = "Slot1",
                    renamePhantomParameters = true,
                    removeSourceControls = true
                };
                var state = new PhantomSlotBuildState
                {
                    Slot = slot,
                    SlotId = "Slot1",
                    Identity = PhantomSlotIdentity.Create(slot),
                    CloneRoot = cloneRoot
                };
                state.ParameterPlan = CreateParameterPlan(slot, "ContactValue");

                PhantomSourceComponentParameterMapper.Capture(state);
                var generatedContact = cloneRoot.AddComponent<VRCContactReceiver>();
                generatedContact.parameter =
                    PhantomParameterNames.PhantomGrabbingContactLeft(slot);
                PhantomSourceComponentParameterMapper.Apply(state);

                Assert.AreEqual(
                    "PhantomSystem/Slot1/Original/ContactValue",
                    sourceContact.parameter);
                Assert.AreEqual(
                    "PhantomSystem/Slot1/PhantomGrabbing/ContactLeft",
                    generatedContact.parameter);
            }
            finally
            {
                Object.DestroyImmediate(cloneRoot);
            }
        }

        [Test]
        public void StrictSourceMapping_KeepsUnknownReservedAndNamespacedNames()
        {
            var slot = new PhantomSlot { id = "Slot1" };
            var state = new PhantomSlotBuildState
            {
                Slot = slot,
                SlotId = "Slot1",
                Identity = PhantomSlotIdentity.Create(slot)
            };
            state.ParameterPlan = CreateParameterPlan(slot, "Known");

            Assert.IsTrue(PhantomSourceParameterMapping.TryResolve(
                state,
                "Known",
                "test",
                out var known));
            Assert.AreEqual("PhantomSystem/Slot1/Original/Known", known);

            Assert.IsTrue(PhantomSourceParameterMapping.TryResolve(
                state,
                "IsLocal",
                "test",
                out var reserved));
            Assert.AreEqual("IsLocal", reserved);

            var coreName = PhantomParameterNames.PhantomGrabbingContactLeft(slot);
            Assert.IsTrue(PhantomSourceParameterMapping.TryResolve(
                state,
                coreName,
                "test",
                out var namespaced));
            Assert.AreEqual(coreName, namespaced);

            Assert.IsFalse(PhantomSourceParameterMapping.TryResolve(
                state,
                "Unknown",
                "Contact Receiver",
                out var unknown));
            Assert.AreEqual("Unknown", unknown);
            CollectionAssert.Contains(
                state.UnresolvedSourceParameterReferences["Unknown"],
                "Contact Receiver");
        }

        [Test]
        public void UnknownSourceReferences_AreReportedOncePerSlot()
        {
            var slot = new PhantomSlot { id = "Slot1" };
            var state = new PhantomSlotBuildState
            {
                Slot = slot,
                SlotId = "Slot1",
                Identity = PhantomSlotIdentity.Create(slot)
            };
            PhantomSourceParameterMapping.TryResolve(
                state,
                "UnknownContact",
                "Contact Receiver",
                out _);
            PhantomSourceParameterMapping.TryResolve(
                state,
                "UnknownAudio",
                "Animator Play Audio",
                out _);
            var report = new PhantomBuildReport();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "2 unresolved source parameter name\\(s\\).*UnknownAudio.*UnknownContact"));

            PhantomSourceParameterMapping.ReportUnresolved(state, report);
            PhantomSourceParameterMapping.ReportUnresolved(state, report);

            Assert.IsTrue(state.UnresolvedSourceParametersReported);
        }

        [Test]
        public void ParameterResolver_WhenSourceControlsRemoved_KeepsRetainedComponentsOnly()
        {
            var slot = new PhantomSlot
            {
                id = "Slot1",
                renamePhantomParameters = true,
                removeSourceControls = true
            };
            var result = PhantomParameterResolver.Resolve(
                new Dictionary<string, PhantomParameterDefinition>(),
                new[]
                {
                    new PhantomParameterSlotInput
                    {
                        Slot = slot,
                        Identity = PhantomSlotIdentity.Create(slot),
                        SourceParameters = new[]
                        {
                            Definition("ControllerOnly", AnimatorControllerParameterType.Bool, false, 0f, false),
                            Definition("ContactValue", AnimatorControllerParameterType.Bool, false, 0f, false)
                        },
                        RetainedSourceParameterNames = new HashSet<string> { "ContactValue" }
                    }
                }).Slots.Single();

            Assert.IsFalse(result.FinalNames.ContainsKey("ControllerOnly"));
            Assert.AreEqual(
                "PhantomSystem/Slot1/Original/ContactValue",
                result.FinalNames["ContactValue"]);
        }

        [Test]
        public void HumanoidBindingClassifier_DistinguishesAllBindingKinds()
        {
            var unsupported = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "m_Enabled");
            var muscle = EditorCurveBinding.FloatCurve(
                string.Empty,
                typeof(Animator),
                HumanTrait.MuscleName[0]);
            var root = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootT.x");
            var ordinary = EditorCurveBinding.FloatCurve("Body", typeof(Transform), "m_LocalPosition.x");

            Assert.AreEqual(
                PhantomAnimationBindingKind.UnsupportedAnimator,
                PhantomAnimationBindingClassifier.Classify(unsupported));
            Assert.AreEqual(
                PhantomAnimationBindingKind.ResolvedHumanoid,
                PhantomAnimationBindingClassifier.Classify(muscle));
            Assert.AreEqual(
                PhantomAnimationBindingKind.RootTransform,
                PhantomAnimationBindingClassifier.Classify(root));
            Assert.AreEqual(
                PhantomAnimationBindingKind.NonHumanoid,
                PhantomAnimationBindingClassifier.Classify(ordinary));
        }

        [TestCase("ChestTDOF.x", HumanBodyBones.Chest)]
        [TestCase("UpperChestTDOF.y", HumanBodyBones.UpperChest)]
        [TestCase("NeckTDOF.z", HumanBodyBones.Neck)]
        [TestCase("LeftUpperLegTDOF.x", HumanBodyBones.LeftUpperLeg)]
        [TestCase("RightUpperLegTDOF.y", HumanBodyBones.RightUpperLeg)]
        [TestCase("LeftShoulderTDOF.z", HumanBodyBones.LeftShoulder)]
        [TestCase("RightShoulderTDOF.x", HumanBodyBones.RightShoulder)]
        [TestCase("LeftHandTDOF.y", HumanBodyBones.LeftHand)]
        [TestCase("RightHandTDOF.z", HumanBodyBones.RightHand)]
        [TestCase("SpineTDOF.x", HumanBodyBones.Spine)]
        [TestCase("LeftLowerLegTDOF.x", HumanBodyBones.LeftLowerLeg)]
        [TestCase("RightLowerLegTDOF.y", HumanBodyBones.RightLowerLeg)]
        [TestCase("LeftFootTDOF.z", HumanBodyBones.LeftFoot)]
        [TestCase("RightFootTDOF.x", HumanBodyBones.RightFoot)]
        [TestCase("LeftToesTDOF.y", HumanBodyBones.LeftToes)]
        [TestCase("RightToesTDOF.z", HumanBodyBones.RightToes)]
        public void HumanoidBindingClassifier_ResolvesTranslationDof(
            string propertyName,
            HumanBodyBones expectedBone)
        {
            var binding = EditorCurveBinding.FloatCurve(
                string.Empty,
                typeof(Animator),
                propertyName);

            Assert.IsTrue(PhantomHumanoidBindingUtility.TryResolveHumanoidBinding(
                binding,
                out var actualBone,
                out var forcePosition));
            Assert.AreEqual(expectedBone, actualBone);
            Assert.IsTrue(forcePosition);
            Assert.AreEqual(
                PhantomAnimationBindingKind.ResolvedHumanoid,
                PhantomAnimationBindingClassifier.Classify(binding));
        }

        [TestCase("ChestTDOF.w")]
        [TestCase("ChestTDOF.xy")]
        [TestCase("UnknownTDOF.x")]
        [TestCase("chestTDOF.x")]
        [TestCase("LastBoneTDOF.x")]
        [TestCase("1TDOF.x")]
        public void HumanoidBindingClassifier_RejectsUnknownTranslationDof(string propertyName)
        {
            var binding = EditorCurveBinding.FloatCurve(
                string.Empty,
                typeof(Animator),
                propertyName);

            Assert.IsFalse(PhantomHumanoidBindingUtility.TryResolveHumanoidBinding(
                binding,
                out _,
                out _));
            Assert.AreEqual(
                PhantomAnimationBindingKind.UnsupportedAnimator,
                PhantomAnimationBindingClassifier.Classify(binding));
        }

        [TestCase(false, false, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        public void MirrorContexts_CombineUsingExclusiveOr(
            bool inheritedMirror,
            bool localMirror,
            bool expected)
        {
            Assert.AreEqual(
                expected,
                PhantomPlayableMotionConverter.CombineMirror(
                    inheritedMirror,
                    localMirror));
            Assert.AreEqual(
                expected,
                PhantomHumanoidBindingUtility.ResolveEffectiveMirror(
                    inheritedMirror,
                    localMirror));
        }

        [TestCase(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg)]
        [TestCase(HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftLowerLeg)]
        [TestCase(HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot)]
        [TestCase(HumanBodyBones.RightToes, HumanBodyBones.LeftToes)]
        [TestCase(HumanBodyBones.LeftHand, HumanBodyBones.RightHand)]
        [TestCase(HumanBodyBones.RightIndexDistal, HumanBodyBones.LeftIndexDistal)]
        [TestCase(HumanBodyBones.Hips, HumanBodyBones.Hips)]
        [TestCase(HumanBodyBones.Chest, HumanBodyBones.Chest)]
        public void HumanoidMirror_MapsBoneToOppositeSide(
            HumanBodyBones source,
            HumanBodyBones expected)
        {
            Assert.AreEqual(expected, PhantomHumanoidBindingUtility.MirrorHumanoidBone(source));
        }

        [Test]
        public void HumanoidMirror_IsAnInvolutionForEveryBone()
        {
            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                Assert.AreEqual(
                    bone,
                    PhantomHumanoidBindingUtility.MirrorHumanoidBone(
                        PhantomHumanoidBindingUtility.MirrorHumanoidBone(bone)),
                    bone.ToString());
            }
        }

        [Test]
        public void MissingBoneSummary_AggregatesBonesAndClipsPerSlot()
        {
            var slot = new PhantomSlotBuildState { SlotId = "Slot1" };
            slot.MissingHumanoidBoneClips[HumanBodyBones.LeftIndexProximal] =
                new HashSet<string>(System.StringComparer.Ordinal)
                {
                    "FX/Point",
                    "Gesture/Point"
                };
            slot.MissingHumanoidBoneClips[HumanBodyBones.RightIndexProximal] =
                new HashSet<string>(System.StringComparer.Ordinal)
                {
                    "FX/Point",
                    "Action/Pose"
                };

            var summary = PhantomSourcePlayableControllerProcessor.BuildMissingBoneSummary(slot);

            StringAssert.Contains("2 unavailable optional humanoid bone(s)", summary);
            StringAssert.Contains("3 converted clip(s)", summary);
            StringAssert.Contains("LeftIndexProximal (2 clip(s))", summary);
            StringAssert.Contains("RightIndexProximal (2 clip(s))", summary);
        }

        [Test]
        public void BuildReport_AbortsOnlyOnce()
        {
            var report = new PhantomBuildReport();
            report.Error("expected test failure");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "\\[NDMF\\] Error Reported:.*expected test failure"));
            Assert.Throws<PhantomBuildAbortException>(() => report.AbortIfErrors());
            Assert.IsTrue(report.IsAborted);
            Assert.IsFalse(report.BeginPass());
            Assert.DoesNotThrow(() => report.AbortIfErrors());
        }

        [Test]
        public void BuildReport_InternalErrorPreservesOriginalException()
        {
            var report = new PhantomBuildReport();
            var exception = new System.InvalidOperationException("original failure");

            report.InternalError("unexpected pass failure", exception: exception);

            Assert.AreSame(exception, report.Issues.Single().Exception);
            Assert.AreEqual(
                PhantomValidationSeverity.InternalError,
                report.Issues.Single().Severity);
            StringAssert.Contains("original failure", report.Issues.Single().DiagnosticMessage);
        }

        [Test]
        public void BuildPassRunner_DoesNotExecuteLaterPassAfterAbort()
        {
            var report = new PhantomBuildReport();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "\\[NDMF\\] Error Reported:.*fatal configuration"));
            Assert.Throws<PhantomBuildAbortException>(() =>
                PhantomBuildPassRunner.Run(
                    report,
                    () => report.Error("fatal configuration"),
                    passName: "FailingPass"));

            var executed = false;
            Assert.DoesNotThrow(() =>
                PhantomBuildPassRunner.Run(
                    report,
                    () => executed = true,
                    passName: "LaterPass"));
            Assert.IsFalse(executed);
        }

        [Test]
        public void BuildPassRunner_RecordsUnexpectedExceptionOnce()
        {
            var report = new PhantomBuildReport();
            var exception = new System.InvalidOperationException("unexpected original");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "\\[NDMF\\] Error Reported:.*UnexpectedPass"));

            Assert.Throws<PhantomBuildAbortException>(() =>
                PhantomBuildPassRunner.Run(
                    report,
                    () => throw exception,
                    passName: "UnexpectedPass"));

            Assert.AreEqual(1, report.Issues.Count);
            Assert.AreSame(exception, report.Issues.Single().Exception);
        }

        [Test]
        public void ConvertedClipDetection_UsesProvenanceInsteadOfName()
        {
            var root = new GameObject("Avatar");
            var convertedSource = new AnimationClip { name = "ConvertedSource" };
            var finalClip = new AnimationClip { name = "RenamedByAnotherPass" };
            var userClip = new AnimationClip { name = "PhantomSystem_Slot1_FX_UserClip" };
            try
            {
                var registry = new ObjectRegistry(root.transform);
                var registryApi = (IObjectRegistry)registry;
                var state = new PhantomBuildState { System = new PhantomSystemBuildState() };
                var slot = new PhantomSlotBuildState();
                state.System.Slots.Add(slot);

                var reference = registryApi.GetReference(convertedSource);
                slot.ConvertedClipReferences[reference] = new PhantomConvertedClipMetadata();
                registryApi.RegisterReplacedObject(reference, finalClip);

                Assert.IsTrue(AnimationBindingDiagnostics.IsConvertedPlayableClip(
                    registry,
                    state,
                    finalClip));
                Assert.IsFalse(AnimationBindingDiagnostics.IsConvertedPlayableClip(
                    registry,
                    state,
                    userClip));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(convertedSource);
                Object.DestroyImmediate(finalClip);
                Object.DestroyImmediate(userClip);
            }
        }

        [Test]
        public void PrebakedValidation_ReportsMissingPrebakeWithStableCode()
        {
            var root = new GameObject("Authoring");
            try
            {
                var authoring = root.AddComponent<PhantomSystem>();
                var state = new PhantomBuildState
                {
                    System = new PhantomSystemBuildState
                    {
                        AuthoringComponent = authoring
                    }
                };
                state.System.Slots.Add(new PhantomSlotBuildState
                {
                    Slot = new PhantomSlot(),
                    SlotId = PhantomSlot.DefaultId
                });

                var validation = PhantomSourceValidator.ValidatePrebakedState(state);

                Assert.AreEqual("PHS301", validation.Slots[0].Issues.Single().Code);
                Assert.AreEqual(0, validation.Slots[0].Issues.Single().SlotIndex);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SlotStatus_ShowsWarningCountAndParameterCostTogether()
        {
            var result = new PhantomSlotValidationResult();
            result.Issues.Add(new PhantomValidationIssue
            {
                Severity = PhantomValidationSeverity.Warning,
                Message = "compatibility cannot be verified"
            });

            Assert.AreEqual(
                "1 warning · 13 bits",
                PhantomSystemEditor.FormatSlotStatus(result, 13));
        }

        private static PhantomSlotParameterResolution Resolve(
            IReadOnlyDictionary<string, PhantomParameterDefinition> baseParameters,
            PhantomSlot slot,
            params PhantomParameterDefinition[] source)
        {
            return PhantomParameterResolver.Resolve(
                baseParameters,
                new[]
                {
                    new PhantomParameterSlotInput
                    {
                        Slot = slot,
                        Identity = PhantomSlotIdentity.Create(slot),
                        SourceParameters = source
                    }
                }).Slots[0];
        }

        private static PhantomSlotParameterPlan CreateParameterPlan(
            PhantomSlot slot,
            params string[] names)
        {
            return PhantomParameterPlanner.Create(
                new Dictionary<string, PhantomParameterDefinition>(),
                new[]
                {
                    new PhantomParameterSlotInput
                    {
                        Slot = slot,
                        Identity = PhantomSlotIdentity.Create(slot),
                        SourceParameters = names.Select(name => new PhantomParameterDefinition
                        {
                            Name = name,
                            ParameterType = AnimatorControllerParameterType.Bool,
                            IsAnimatorOnly = true
                        }).ToArray(),
                        RetainedSourceParameterNames = new HashSet<string>(names)
                    }
                }).Slots[0];
        }

        private static PhantomSourceValidationReport ValidateSlots(params PhantomSlot[] slots)
        {
            var root = new GameObject("Authoring");
            try
            {
                var authoring = root.AddComponent<PhantomSystem>();
                authoring.slots = slots.ToList();
                return PhantomSourceValidator.ValidateAuthoring(authoring);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static PhantomParameterSlotInput Input(PhantomSlot slot)
        {
            return new PhantomParameterSlotInput
            {
                Slot = slot,
                Identity = PhantomSlotIdentity.Create(slot)
            };
        }

        private static PhantomParameterDefinition Definition(
            string name,
            AnimatorControllerParameterType type,
            bool synced,
            float defaultValue,
            bool saved)
        {
            return new PhantomParameterDefinition
            {
                Name = name,
                ParameterType = type,
                WantSynced = synced,
                DefaultValue = defaultValue,
                Saved = saved
            };
        }
    }
}
