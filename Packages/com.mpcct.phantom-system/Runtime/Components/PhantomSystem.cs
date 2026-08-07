using System;
using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem
{
    [DisallowMultipleComponent]
    [AddComponentMenu("MPCCT/PhantomSystem")]
    public sealed class PhantomSystem : MonoBehaviour, INDMFEditorOnly
    {
        public List<PhantomSlot> slots = new List<PhantomSlot> { new PhantomSlot() };
        public PhantomSystemOptions options = new PhantomSystemOptions();
        [HideInInspector]
        public ModularAvatarMenuInstaller coreMenuInstaller;

        private void Reset()
        {
            if (slots == null || slots.Count == 0)
            {
                slots = new List<PhantomSlot> { new PhantomSlot() };
            }

            if (coreMenuInstaller == null || coreMenuInstaller.gameObject != gameObject)
            {
                coreMenuInstaller = gameObject.AddComponent<ModularAvatarMenuInstaller>();
            }
        }
    }

    [Serializable]
    public sealed class PhantomSlot
    {
        public const string DefaultId = "Slot1";

        public string id = DefaultId;
        public VRCAvatarDescriptor phantomAvatar;
        public Transform spawnPositionOverride;
        public bool includePhantomMenu = true;
        public string parameterPrefix = "";
        public bool renamePhantomParameters = true;
        [HideInInspector]
        public List<string> sharedParameterNames = new List<string>();
        public bool removeSourceControls;
        public bool useRotationConstraint;
        public bool rotationSolveInWorldSpace;
        public bool overridePhysBoneImmobileType;
        public bool tryConvertAnimatorTrackingControl = true;
        public bool enablePhantomGrabbing = true;
        public bool enableScaleControl = true;
        public bool enablePhantomView = true;
    }

    [Serializable]
    public sealed class PhantomSystemOptions
    {
        public bool installPhantomMenu = true;
    }
}
