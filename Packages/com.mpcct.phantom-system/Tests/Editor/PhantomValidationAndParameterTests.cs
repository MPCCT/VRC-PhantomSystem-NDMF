using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
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
        public void CloneMappings_RemapContactEvenWhenSourceControlsAreRemoved()
        {
            var cloneRoot = new GameObject("CloneRoot");
            try
            {
                var contact = cloneRoot.AddComponent<VRCContactReceiver>();
                contact.parameter = "ContactValue";
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
                    CloneRoot = cloneRoot,
                    ParameterResolution = Resolve(
                        new Dictionary<string, PhantomParameterDefinition>(),
                        slot)
                };

                var configs = PhantomParameterConfigBuilder.BuildCloneMappings(
                    state,
                    new RuntimeAnimatorController[0]);
                var config = configs.Single(item =>
                    !item.isPrefix && item.nameOrPrefix == "ContactValue");

                Assert.AreEqual(
                    "PhantomSystem/Slot1/Original/ContactValue",
                    config.remapTo);
            }
            finally
            {
                Object.DestroyImmediate(cloneRoot);
            }
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

            Assert.IsTrue(PhantomHumanoidClipBaker.TryResolveHumanoidBinding(
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

            Assert.IsFalse(PhantomHumanoidClipBaker.TryResolveHumanoidBinding(
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
                PhantomHumanoidClipBaker.ResolveEffectiveMirror(
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
            Assert.AreEqual(expected, PhantomHumanoidClipBaker.MirrorHumanoidBone(source));
        }

        [Test]
        public void HumanoidMirror_IsAnInvolutionForEveryBone()
        {
            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                Assert.AreEqual(
                    bone,
                    PhantomHumanoidClipBaker.MirrorHumanoidBone(
                        PhantomHumanoidClipBaker.MirrorHumanoidBone(bone)),
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
