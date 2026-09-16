using System.Reflection;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MPCCT.PhantomSystem.Editor.Tests
{
    internal sealed class PhantomAvatarClonerTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Clone_PreservesPrebakedRenderersThroughMaMeshSettings(
            bool existingSettings, bool nullAnchor)
        {
            var avatar = new GameObject("Avatar", typeof(VRCAvatarDescriptor));
            var prebaked = new GameObject("Prebaked", typeof(VRCAvatarDescriptor));
            try
            {
                var hostAnchor = Child(avatar, "HostAnchor");
                var hostBone = Child(avatar, "HostBone");
                var hostBounds = new Bounds(Vector3.one * 5, Vector3.one * 10);
                var hostSettings = avatar.AddComponent<ModularAvatarMeshSettings>();
                hostSettings.InheritProbeAnchor = ModularAvatarMeshSettings.InheritMode.Set;
                hostSettings.InheritBounds = ModularAvatarMeshSettings.InheritMode.Set;
                hostSettings.ProbeAnchor = new AvatarObjectReference(hostAnchor);
                hostSettings.RootBone = new AvatarObjectReference(hostBone);
                hostSettings.Bounds = hostBounds;
                var hostRenderer = Child(avatar, "HostMesh").AddComponent<SkinnedMeshRenderer>();

                var sourceAnchor = Child(prebaked, "Anchor").transform;
                var sourceBone = Child(prebaked, "Bone").transform;
                var sourceBounds = new Bounds(new Vector3(1, 2, 3), new Vector3(2, 4, 6));
                var sourceRenderer = Child(prebaked, "Skin").AddComponent<SkinnedMeshRenderer>();
                sourceRenderer.probeAnchor = nullAnchor ? null : sourceAnchor;
                sourceRenderer.rootBone = sourceBone;
                sourceRenderer.bones = new[] { sourceBone };
                sourceRenderer.localBounds = sourceBounds;
                var sourceStaticRenderer = Child(prebaked, "Static").AddComponent<MeshRenderer>();
                sourceStaticRenderer.probeAnchor = nullAnchor ? null : sourceAnchor;
                sourceStaticRenderer.gameObject.SetActive(false);

                ModularAvatarMeshSettings sourceSettings = null;
                if (existingSettings)
                {
                    sourceSettings = prebaked.AddComponent<ModularAvatarMeshSettings>();
                    sourceSettings.InheritProbeAnchor = ModularAvatarMeshSettings.InheritMode.Set;
                    sourceSettings.InheritBounds = ModularAvatarMeshSettings.InheritMode.Set;
                    sourceSettings.ProbeAnchor = new AvatarObjectReference(sourceAnchor.gameObject);
                    sourceSettings.RootBone = new AvatarObjectReference(sourceBone.gameObject);
                }

                var context = new BuildContext(avatar, null);
                var slot = new PhantomSlotBuildState { SlotId = "Slot1", PrebakedRoot = prebaked };
                var system = new PhantomSystemBuildState();
                system.Slots.Add(slot);
                PhantomAvatarCloner.CloneSystem(context, system);
                PhantomAvatarCloner.CleanupNestedAvatarComponents(slot);

                var clone = slot.CloneRoot;
                var cloneSettings = clone.GetComponents<ModularAvatarMeshSettings>();
                Assert.AreEqual(1, cloneSettings.Length);
                Assert.AreEqual(ModularAvatarMeshSettings.InheritMode.DontSet,
                    cloneSettings[0].InheritProbeAnchor);
                Assert.AreEqual(ModularAvatarMeshSettings.InheritMode.DontSet,
                    cloneSettings[0].InheritBounds);

                RunMaMeshSettings(context);

                var cloneRenderer = clone.transform.Find("Skin").GetComponent<SkinnedMeshRenderer>();
                var cloneStaticRenderer = clone.transform.Find("Static").GetComponent<MeshRenderer>();
                var expectedAnchor = nullAnchor ? null : clone.transform.Find("Anchor");
                Assert.AreEqual(expectedAnchor, cloneRenderer.probeAnchor);
                Assert.AreEqual(expectedAnchor, cloneStaticRenderer.probeAnchor);
                Assert.AreEqual(clone.transform.Find("Bone"), cloneRenderer.rootBone);
                Assert.AreEqual(sourceBounds, cloneRenderer.localBounds);
                Assert.IsFalse(clone.activeSelf);
                Assert.IsFalse(cloneStaticRenderer.gameObject.activeSelf);

                // Control: the host's own renderers still inherit its settings.
                Assert.AreEqual(hostAnchor.transform, hostRenderer.probeAnchor);
                Assert.AreEqual(hostBone.transform, hostRenderer.rootBone);
                Assert.AreEqual(hostBounds, hostRenderer.localBounds);
                Assert.AreEqual(ModularAvatarMeshSettings.InheritMode.Set, hostSettings.InheritBounds);

                // The source prebake is reusable and must not be changed by cloning.
                Assert.AreEqual(nullAnchor ? null : sourceAnchor, sourceRenderer.probeAnchor);
                Assert.AreEqual(sourceBone, sourceRenderer.rootBone);
                Assert.AreEqual(sourceBounds, sourceRenderer.localBounds);
                Assert.AreEqual(existingSettings ? 1 : 0,
                    prebaked.GetComponents<ModularAvatarMeshSettings>().Length);
                if (sourceSettings != null)
                {
                    Assert.AreEqual(ModularAvatarMeshSettings.InheritMode.Set, sourceSettings.InheritProbeAnchor);
                    Assert.AreEqual(ModularAvatarMeshSettings.InheritMode.Set, sourceSettings.InheritBounds);
                }
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(prebaked);
            }
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static void RunMaMeshSettings(BuildContext context)
        {
            // Exercise MA's actual pass, which is internal, without depending on its editor API.
            var assembly = Assembly.Load("nadena.dev.modular-avatar.core.editor");
            var contextType = assembly.GetType("nadena.dev.modular_avatar.core.editor.BuildContext", true);
            var passType = assembly.GetType("nadena.dev.modular_avatar.core.editor.MeshSettingsPass", true);
            var maContext = System.Activator.CreateInstance(contextType, new object[] { context });
            var pass = System.Activator.CreateInstance(passType, new[] { maContext });
            var run = passType.GetMethod("OnPreprocessAvatar", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(run, "MA Mesh Settings pass entry point changed.");
            run.Invoke(pass, null);
        }
    }
}
