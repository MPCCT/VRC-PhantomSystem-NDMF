using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    public sealed class PhantomBuildState
    {
        public PhantomSystemBuildState System { get; set; }
        public PhantomBuildReport Report { get; } = new PhantomBuildReport();
        internal Dictionary<string, PhantomParameterDefinition> BaseParameters { get; } =
            new Dictionary<string, PhantomParameterDefinition>(global::System.StringComparer.Ordinal);

        public bool HasWork => System != null;
    }

    public sealed class PhantomSystemBuildState
    {
        public PhantomAuthoring AuthoringComponent;
        public Transform AvatarRoot;
        public GameObject RuntimeRoot;
        public GameObject SlotsRoot;
        public GameObject ViewsRoot;
        public Mesh PhantomViewDisplayMesh;
        public RenderTexture PhantomViewLeftTexture;
        public RenderTexture PhantomViewRightTexture;
        public VRCExpressionsMenu GeneratedSystemMenu;
        public VRCExpressionsMenu GeneratedRootMenu;
        public List<PhantomSlotBuildState> Slots { get; } = new List<PhantomSlotBuildState>();
    }

    public sealed class PhantomSlotBuildState
    {
        public PhantomSlot Slot;
        public string SlotId;
        public VRCAvatarDescriptor SourceAvatar;
        public GameObject PrebakedRoot;
        public GameObject SlotRoot;
        public GameObject CloneRoot;
        public VRCAvatarDescriptor BakedAvatar;
        public Animator CloneAnimator;
        public Transform CloneArmature;
        public Transform BaseAvatarPosition;
        public Transform ArmatureConstraintTarget;
        public Transform PhantomGrabbingHipsConstraintHost;
        public Transform PhantomGrabbingBoneDisplayHost;
        public Transform PhantomViewRoot;
        public Transform PhantomViewCaptureRoot;
        public Transform PhantomViewLeftCamera;
        public Transform PhantomViewRightCamera;
        public Transform PhantomViewDisplayHost;
        public readonly Dictionary<HumanBodyBones, Transform> CloneBones = new Dictionary<HumanBodyBones, Transform>();
        public readonly Dictionary<HumanBodyBones, string> CloneBoneAvatarPaths = new Dictionary<HumanBodyBones, string>();
        public readonly Dictionary<HumanBodyBones, global::System.Type> CloneBoneConstraintTypes =
            new Dictionary<HumanBodyBones, global::System.Type>();
        public readonly Dictionary<HumanBodyBones, Transform> PhantomGrabbingBodyProxyBones =
            new Dictionary<HumanBodyBones, Transform>();
        public readonly Dictionary<HumanBodyBones, Transform> PhantomGrabbingBodyPhysBoneHosts =
            new Dictionary<HumanBodyBones, Transform>();
        public readonly Dictionary<HumanBodyBones, Vector3> PhantomGrabbingBodySegmentEndpoints =
            new Dictionary<HumanBodyBones, Vector3>();
        public readonly Dictionary<HumanBodyBones, Transform> PhantomGrabbingBodySyncConstraintHosts =
            new Dictionary<HumanBodyBones, Transform>();
        public readonly Dictionary<HumanBodyBones, Transform> PhantomGrabbingBodyOutputConstraintHosts =
            new Dictionary<HumanBodyBones, Transform>();
        public AnimatorController GeneratedController;
        public AnimatorController GeneratedTrackingController;
        public AnimatorController GeneratedPhantomViewController;
        public RuntimeAnimatorController ProcessedFxController;
        public bool HasTrackingControlConversion;
        public GameObject OriginalIntegrationHost;
        public VRCExpressionsMenu GeneratedCoreMenu;
        public ModularAvatarMergeAnimator CoreMergeAnimator;
        public ModularAvatarMergeAnimator TrackingMergeAnimator;
        public ModularAvatarMergeAnimator PhantomViewMergeAnimator;
        public ModularAvatarMergeAnimator OriginalMergeAnimator;
        internal readonly HashSet<string> ValidSharedParameterNames =
            new HashSet<string>(System.StringComparer.Ordinal);
    }
}
