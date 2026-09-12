using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEditor;
using nadena.dev.ndmf.ui;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor.Tests
{
    internal sealed class PhantomLocalizationTests
    {
        private string previousLanguage;

        [SetUp]
        public void SetUp() => previousLanguage = LanguagePrefs.Language;

        [TearDown]
        public void TearDown() => LanguagePrefs.Language = previousLanguage;

        [Test]
        public void Catalogs_HaveMatchingKeysAndFormatArguments()
        {
            var english = PhantomLocalization.GetTable("en-US");
            Assert.Greater(english.Count, 100);
            foreach (var locale in PhantomLocalization.Locales)
            {
                var table = PhantomLocalization.GetTable(locale);
                CollectionAssert.AreEquivalent(english.Keys, table.Keys, locale);
                foreach (var pair in table)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Value), locale + ":" + pair.Key);
                    CollectionAssert.AreEquivalent(Arguments(english[pair.Key]), Arguments(pair.Value),
                        locale + ":" + pair.Key);
                    Assert.DoesNotThrow(() => string.Format(pair.Value, Enumerable.Range(0, 10).Cast<object>().ToArray()),
                        locale + ":" + pair.Key);
                }
            }
        }

        [TestCase("en-US", "Freeze", "Slots", "No phantom avatar is assigned.")]
        [TestCase("zh-Hans", "冻结", "分身槽位", "尚未指定分身源 Avatar。")]
        [TestCase("ja-JP", "フリーズ", "スロット", "分身の元 Avatar が指定されていません。")]
        [TestCase("fr-FR", "Freeze", "Slots", "No phantom avatar is assigned.")]
        public void LanguageChange_UpdatesUiAndGeneratedMenusWithoutChangingParameters(
            string locale, string expectedFreeze, string expectedSlots, string expectedDiagnostic)
        {
            var root = new GameObject("Avatar");
            root.AddComponent<Animator>();
            root.AddComponent<VRCAvatarDescriptor>();
            var authoring = root.AddComponent<PhantomAuthoring>();
            var slotConfig = new PhantomSlot { id = "CustomSlot" };
            var slot = new PhantomSlotBuildState
            {
                Slot = slotConfig,
                SlotId = slotConfig.id,
                Identity = PhantomSlotIdentity.Create(slotConfig)
            };
            var system = new PhantomSystemBuildState { AuthoringComponent = authoring };
            system.Slots.Add(slot);
            var host = new GameObject("Host");
            host.transform.SetParent(root.transform, false);
            var context = new BuildContext(root, null);
            var generated = new HashSet<VRCExpressionsMenu>();
            try
            {
                LanguagePrefs.Language = "en-US";
                var before = PhantomCoreParameterCatalog.ForSlot(slotConfig).Select(x => x.Parameter.Name).ToArray();
                LanguagePrefs.Language = locale;
                Assert.AreEqual(expectedSlots, PhantomLocalization.S("slots.title"));

                var menu = PhantomCoreMenuBuilder.Install(context, system, slot, host);
                CollectMenus(system.GeneratedRootMenu, generated);
                var freeze = menu.controls.Single(control =>
                    control.parameter?.name == PhantomParameterNames.Freeze(slotConfig));
                Assert.AreEqual(expectedFreeze, freeze.name);
                Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.Toggle, freeze.type);
                Assert.AreEqual("PhantomSystem/CustomSlot/Freeze", freeze.parameter.name);
                CollectionAssert.AreEqual(before,
                    PhantomCoreParameterCatalog.ForSlot(slotConfig).Select(x => x.Parameter.Name).ToArray());
                Assert.AreEqual("CustomSlot", slotConfig.id);

                var validation = PhantomSourceValidator.ValidateAuthoring(authoring);
                var issue = validation.Slots[0].Issues.Single(entry => entry.Code == "PHS010");
                Assert.AreEqual(expectedDiagnostic, issue.Message);
            }
            finally
            {
                context.DeactivateAllExtensionContexts();
                foreach (var menu in generated) Object.DestroyImmediate(menu);
                Object.DestroyImmediate(root);
            }
        }

        [TestCase("en-US", "type mismatch", "PhantomSystem build warning")]
        [TestCase("zh-Hans", "类型不匹配", "PhantomSystem 构建警告")]
        [TestCase("ja-JP", "型が一致しません", "PhantomSystem のビルド警告")]
        [TestCase("fr-FR", "type mismatch", "PhantomSystem build warning")]
        public void CachedNdmfWarning_UpdatesNestedReasonAndVisibleLabels(
            string locale, string expectedReason, string expectedTitle)
        {
            LanguagePrefs.Language = "en-US";
            var left = new PhantomParameterDefinition { ParameterType = AnimatorControllerParameterType.Bool };
            var right = new PhantomParameterDefinition { ParameterType = AnimatorControllerParameterType.Int };
            Assert.IsFalse(PhantomParameterCompatibility.AreCompatible(left, right, out var reason));
            var diagnostic = PhantomLocalization.D("diagnostic.report.coded", "PHS201",
                PhantomLocalization.D("diagnostic.validation.PHS201",
                    "Outfit", "PhantomSystem/CustomSlot/Original/Outfit", reason));
            var report = new PhantomBuildReport();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[NDMF\] Error Reported:.*PHS201"));
            var captured = ErrorReport.CaptureErrors(() => report.Warning(diagnostic));
            var error = (PhantomBuildError)captured.Single().TheError;
            var view = error.CreateVisualElement(null);
            Assert.IsFalse(report.HasErrors);
            Assert.IsFalse(report.IsAborted);
            Assert.IsTrue(report.BeginPass());
            Assert.DoesNotThrow(() => report.AbortIfErrors());
            Assert.AreEqual(ErrorSeverity.NonFatal, error.Severity);

            // Change the language after the report and UI were created.
            LanguagePrefs.Language = locale;
            StringAssert.Contains(expectedReason, error.FormatDetails());
            StringAssert.Contains("[PHS201]", error.FormatDetails());
            StringAssert.Contains("PhantomSystem/CustomSlot/Original/Outfit", error.FormatDetails());
            StringAssert.Contains("Bool", error.FormatDetails());
            StringAssert.Contains("Int", error.FormatDetails());
            Assert.AreEqual(expectedTitle, view.Q<Label>("title").text);
            Assert.AreEqual(error.FormatDetails(), view.Q<Label>("description").text);
        }

        [TestCase("en-US", "No phantom avatar is assigned.")]
        [TestCase("zh-Hans", "尚未指定分身源 Avatar。")]
        [TestCase("ja-JP", "分身の元 Avatar が指定されていません。")]
        [TestCase("fr-FR", "No phantom avatar is assigned.")]
        public void CachedInspectorValidation_UpdatesWithoutChangingCodesOrDuplicateDetection(
            string locale, string expectedDiagnostic)
        {
            LanguagePrefs.Language = "en-US";
            var root = new GameObject("Avatar");
            try
            {
                var authoring = root.AddComponent<PhantomAuthoring>();
                authoring.slots = new List<PhantomSlot>
                {
                    new PhantomSlot { id = "SameSlot" },
                    new PhantomSlot { id = "SameSlot" }
                };
                var cached = PhantomSourceValidator.ValidateAuthoring(authoring);
                LanguagePrefs.Language = locale;
                var issue = cached.Slots[0].Issues.Single(entry => entry.Code == "PHS010");
                Assert.AreEqual(expectedDiagnostic, issue.Message);
                Assert.AreEqual(PhantomValidationSeverity.ConfigurationError, issue.Severity);
                Assert.AreSame(authoring, issue.Context);
                Assert.AreEqual(0, issue.SlotIndex);
                Assert.IsFalse(cached.GlobalIssues.Any(entry => entry.Code == "PHS200"));
                var current = PhantomSourceValidator.ValidateAuthoring(authoring);
                Assert.IsFalse(current.GlobalIssues.Any(entry => entry.Code == "PHS200"));
                Assert.IsTrue(current.Slots.All(slot => slot.Issues.Any(entry => entry.Code == "PHS030")));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void InternalError_LocalizesAfterCaptureAndKeepsOriginalExceptionAndAbort()
        {
            LanguagePrefs.Language = "en-US";
            var report = new PhantomBuildReport();
            var original = new InvalidOperationException("raw exception sentinel");
            report.InternalError(PhantomLocalization.D("diagnostic.build.passFailed", "ExamplePass"),
                exception: original);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[NDMF\] Error Reported:.*ExamplePass"));
            var captured = ErrorReport.CaptureErrors(() =>
                Assert.Throws<PhantomBuildAbortException>(() => report.AbortIfErrors()));
            var error = (PhantomBuildError)captured.Single().TheError;
            LanguagePrefs.Language = "zh-Hans";
            Assert.AreEqual(ErrorSeverity.Error, error.Severity);
            Assert.AreEqual("PhantomSystem 构建失败", error.FormatTitle());
            StringAssert.Contains("内部构建错误", error.FormatDetails());
            StringAssert.Contains("ExamplePass", error.FormatDetails());
            StringAssert.Contains("raw exception sentinel", error.FormatDetails());
            Assert.AreSame(original, report.Issues.Single().Exception);
            Assert.IsTrue(report.IsAborted);
            Assert.IsFalse(report.BeginPass());
            Assert.IsEmpty(ErrorReport.CaptureErrors(() => report.AbortIfErrors()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NdmfReportView_HandlesClosedScene(bool createViewAfterClosingScene)
        {
            LanguagePrefs.Language = "en-US";
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewPreviewScene();
            BuildContext context = null;
            VisualElement view = null;
            try
            {
                var root = new GameObject("DiagnosticAvatar");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.AddComponent<Animator>();
                root.AddComponent<VRCAvatarDescriptor>();
                var child = new GameObject("ReferencedChild");
                child.transform.SetParent(root.transform, false);
                LogAssert.Expect(LogType.Log, "Starting processing for avatar: DiagnosticAvatar");
                context = new BuildContext(root, null, isClone: false);
                var reference = ((IObjectRegistry)context.ObjectRegistry).GetReference(child);
                var error = new PhantomBuildError(new PhantomBuildIssue(
                    PhantomValidationSeverity.Warning,
                    PhantomLocalization.D("diagnostic.validation.PHS010"),
                    child, null));
                error.AddReference(reference);
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                    PhantomLocalization.AssetDirectory + "en-US.json");
                Assert.IsNotNull(asset);
                error.AddReference(((IObjectRegistry)context.ObjectRegistry).GetReference(asset));

                if (!createViewAfterClosingScene)
                {
                    view = error.CreateVisualElement(context.ErrorReport);
                    Assert.AreEqual(2, view.Query<ObjectSelector>().ToList().Count,
                        "Live scene references and assets should still be selectable.");
                }

                context.DeactivateAllExtensionContexts();
                EditorSceneManager.ClosePreviewScene(scene);
                Assert.IsFalse(scene.IsValid());
                if (createViewAfterClosingScene)
                {
                    Assert.DoesNotThrow(() => view = error.CreateVisualElement(context.ErrorReport));
                }

                // NDMF invokes callbacks on already-created views when language changes.
                LanguagePrefs.Language = "zh-Hans";
                LogAssert.NoUnexpectedReceived();
                Assert.AreEqual("PhantomSystem 构建警告", view.Q<Label>("title").text);
                StringAssert.Contains("尚未指定分身源 Avatar。", view.Q<Label>("description").text);
                StringAssert.Contains("ReferencedChild", view.Q<Label>("description").text);
                Assert.AreEqual(1, view.Query<ObjectSelector>().ToList().Count,
                    "Persistent asset references must remain selectable after the scene closes.");
                Assert.AreEqual(2, error.References.Length,
                    "Only the view should filter expired links; the report must retain its references.");
                GC.KeepAlive(view);
            }
            finally
            {
                context?.DeactivateAllExtensionContexts();
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            }
        }

        private static string[] Arguments(string value) =>
            Regex.Matches(value, @"\{(\d+)(?:[^}]*)\}").Cast<Match>()
                .Select(match => match.Groups[1].Value).OrderBy(x => x).ToArray();

        private static void CollectMenus(VRCExpressionsMenu menu, ISet<VRCExpressionsMenu> result)
        {
            if (menu == null || !result.Add(menu)) return;
            foreach (var control in menu.controls) CollectMenus(control.subMenu, result);
        }
    }
}
