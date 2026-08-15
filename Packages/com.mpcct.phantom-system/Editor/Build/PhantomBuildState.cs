using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf.animator;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using NdmfObjectReference = nadena.dev.ndmf.ObjectReference;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    public sealed class PhantomBuildState
    {
        public PhantomSystemBuildState System { get; set; }
        public PhantomBuildReport Report { get; } = new PhantomBuildReport();
        internal PhantomSystemProjectSettingsSnapshot ProjectSettings { get; set; }
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
        internal PhantomSystemProjectSettingsSnapshot ProjectSettings;
        public VRCExpressionsMenu GeneratedSystemMenu;
        public VRCExpressionsMenu GeneratedRootMenu;
        public List<PhantomSlotBuildState> Slots { get; } = new List<PhantomSlotBuildState>();
    }

    public sealed class PhantomSlotBuildState
    {
        public PhantomSlot Slot;
        public string SlotId;
        internal PhantomSlotIdentity Identity;
        internal string HierarchyName => Identity?.HierarchyName ?? SlotId;
        public VRCAvatarDescriptor SourceAvatar;
        public GameObject PrebakedRoot;
        public GameObject SlotRoot;
        public GameObject CloneRoot;
        public VRCAvatarDescriptor BakedAvatar;
        public Animator CloneAnimator;
        public Transform CloneArmature;
        public Transform AnimationDriverRoot;
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
        public readonly Dictionary<HumanBodyBones, Transform> AnimationDriverBones =
            new Dictionary<HumanBodyBones, Transform>();
        public readonly Dictionary<string, string> CloneToAnimationDriverPaths =
            new Dictionary<string, string>(System.StringComparer.Ordinal);
        public readonly Dictionary<string, string> AnimationDriverToClonePaths =
            new Dictionary<string, string>(System.StringComparer.Ordinal);
        public readonly Dictionary<HumanBodyBones, string> AnimationDriverPoseParentClonePaths =
            new Dictionary<HumanBodyBones, string>();
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
        internal AnimatorController GeneratedDriverNeutralController;
        public AnimatorController GeneratedTrackingController;
        public AnimatorController GeneratedPhantomViewController;
        public RuntimeAnimatorController ProcessedFxController;
        public RuntimeAnimatorController ProcessedGestureController;
        public RuntimeAnimatorController ProcessedActionController;
        public bool HasTrackingControlConversion;
        public GameObject SourceIntegrationHost;
        public VRCExpressionsMenu GeneratedCoreMenu;
        public ModularAvatarMergeAnimator CoreMergeAnimator;
        public ModularAvatarMergeAnimator TrackingMergeAnimator;
        public ModularAvatarMergeAnimator PhantomViewMergeAnimator;
        internal ModularAvatarMergeAnimator DriverNeutralMergeAnimator;
        public ModularAvatarMergeAnimator SourceFxMergeAnimator;
        public ModularAvatarMergeAnimator SourceGestureMergeAnimator;
        public ModularAvatarMergeAnimator SourceActionMergeAnimator;
        internal readonly Dictionary<VRCAvatarDescriptor.AnimLayerType, PhantomSourcePlayableRegistration>
            SourcePlayableRegistrations =
                new Dictionary<VRCAvatarDescriptor.AnimLayerType, PhantomSourcePlayableRegistration>();
        internal readonly List<PhantomConvertedActionLayer> ConvertedActionLayers =
            new List<PhantomConvertedActionLayer>();
        internal PhantomSlotParameterResolution ParameterResolution;
        internal readonly Dictionary<NdmfObjectReference, PhantomConvertedClipMetadata> ConvertedClipReferences =
            new Dictionary<NdmfObjectReference, PhantomConvertedClipMetadata>();
        internal readonly HashSet<VirtualClip> WarnedUnsupportedAnimatorClips =
            new HashSet<VirtualClip>();
        internal readonly Dictionary<HumanBodyBones, HashSet<string>> MissingHumanoidBoneClips =
            new Dictionary<HumanBodyBones, HashSet<string>>();
        internal readonly List<PhantomSourceComponentParameterReference> SourceComponentParameters =
            new List<PhantomSourceComponentParameterReference>();
        internal readonly Dictionary<string, HashSet<string>> UnresolvedSourceParameterReferences =
            new Dictionary<string, HashSet<string>>(System.StringComparer.Ordinal);
        internal bool UnresolvedSourceParametersReported;
    }

    internal enum PhantomSourceComponentParameterKind
    {
        Contact,
        PhysBonePrefix,
        RaycastPrefix
    }

    internal sealed class PhantomSourceComponentParameterReference
    {
        public Component Component;
        public string OriginalName;
        public PhantomSourceComponentParameterKind Kind;
    }

    internal sealed class PhantomConvertedClipMetadata
    {
        public string SlotId;
        public string Playable;
        public string SourceClipName;
    }

    internal sealed class PhantomSourcePlayableRegistration
    {
        public VRCAvatarDescriptor.AnimLayerType Playable;
        public PhantomSourcePlayableLayer Source;
        public AnimatorController BaseController;
        public ModularAvatarMergeAnimator MergeAnimator;
    }

    internal readonly struct PhantomConvertedActionLayer
    {
        public readonly string LayerName;
        public readonly float EnabledWeight;

        public PhantomConvertedActionLayer(string layerName, float enabledWeight)
        {
            LayerName = layerName;
            EnabledWeight = enabledWeight;
        }
    }
}
