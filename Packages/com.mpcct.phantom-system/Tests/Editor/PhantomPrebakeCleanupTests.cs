using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor.Tests
{
    internal sealed class PhantomPrebakeCleanupTests
    {
        private string testRoot;

        [SetUp]
        public void SetUp()
        {
            var folderName = "PhantomPrebakeCleanupTests_" + Guid.NewGuid().ToString("N");
            testRoot = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            PhantomPrebakeSession.ClearAutomaticCleanupPending();
        }

        [TearDown]
        public void TearDown()
        {
            PhantomPrebakeSession.CleanupAll();
            PhantomPrebakeSession.ClearAutomaticCleanupPending();
            if (AssetDatabase.IsValidFolder(testRoot))
            {
                AssetDatabase.DeleteAsset(testRoot);
            }
        }

        [Test]
        public void Cleanup_DeletesOnlyDirectOwnedPrebakeDirectories()
        {
            AssetDatabase.CreateFolder(testRoot, "PhantomPrebake_A");
            AssetDatabase.CreateFolder(testRoot, "KeepMe");
            var nested = testRoot + "/KeepMe";
            AssetDatabase.CreateFolder(nested, "PhantomPrebake_Nested");

            var result = PhantomPrebakeAssetCleanup.DeleteGeneratedAssets(testRoot);

            Assert.AreEqual(1, result.Candidates);
            Assert.AreEqual(1, result.Removed);
            Assert.AreEqual(0, result.Failed);
            Assert.IsFalse(AssetDatabase.IsValidFolder(testRoot + "/PhantomPrebake_A"));
            Assert.IsTrue(AssetDatabase.IsValidFolder(testRoot + "/KeepMe"));
            Assert.IsTrue(AssetDatabase.IsValidFolder(nested + "/PhantomPrebake_Nested"));
        }

        [Test]
        public void Cleanup_RemovesEmptyPrebakeAndGeneratedContainerFolders()
        {
            AssetDatabase.CreateFolder(testRoot, "Prebake");
            var prebakeRoot = testRoot + "/Prebake";
            AssetDatabase.CreateFolder(prebakeRoot, "PhantomPrebake_A");

            var result = PhantomPrebakeAssetCleanup.DeleteGeneratedAssets(
                prebakeRoot,
                testRoot);

            Assert.AreEqual(1, result.Candidates);
            Assert.AreEqual(1, result.Removed);
            Assert.AreEqual(0, result.Failed);
            Assert.IsFalse(AssetDatabase.IsValidFolder(prebakeRoot));
            Assert.IsFalse(AssetDatabase.IsValidFolder(testRoot));
        }

        [Test]
        public void AutomaticPrebake_RequestsExactlyOnePostprocessCleanup()
        {
            var avatar = new GameObject("Avatar");
            try
            {
                PhantomPrebakeSession.Begin(avatar);
                PhantomPrebakeSession.MarkAutomaticCleanupPending();

                Assert.IsTrue(PhantomPrebakeSession.ConsumeAutomaticCleanupPending());
                Assert.IsFalse(PhantomPrebakeSession.ConsumeAutomaticCleanupPending());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void ManualPrebake_DoesNotRequestPostprocessCleanup()
        {
            var avatar = new GameObject("Avatar");
            try
            {
                PhantomPrebakeSession.Begin(avatar);
                Assert.IsFalse(PhantomPrebakeSession.ConsumeAutomaticCleanupPending());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
            }
        }
    }
}
