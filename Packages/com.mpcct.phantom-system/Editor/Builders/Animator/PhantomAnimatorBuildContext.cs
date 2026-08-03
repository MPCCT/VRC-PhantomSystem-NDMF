using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEditor.Animations;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Shares resolved slot paths and generated assets across animator modules.</summary>
    internal sealed class PhantomAnimatorBuildContext
    {
        private readonly List<AnimationClip> generatedClips = new List<AnimationClip>();
        private readonly List<BlendTree> generatedBlendTrees = new List<BlendTree>();

        public BuildContext NdmfContext { get; }
        public PhantomSystemBuildState System { get; }
        public PhantomSlotBuildState Slot { get; }
        public PhantomBuildReport Report { get; }
        public AnimatorController Controller { get; }

        public string RootPath { get; }
        public string BaseAvatarPositionPath { get; }
        public string ArmaturePath { get; }
        public string PhantomGrabbingHipsPath { get; }
        public string PhantomGrabbingHipsConstraintPath { get; }
        public string PhantomGrabbingBoneDisplayPath { get; }
        public IReadOnlyDictionary<HumanBodyBones, string> PhantomGrabbingBodyPhysBonePaths { get; }
        public IReadOnlyDictionary<HumanBodyBones, string> PhantomGrabbingBodySyncConstraintPaths { get; }
        public IReadOnlyDictionary<HumanBodyBones, string> PhantomGrabbingBodyOutputConstraintPaths { get; }

        public IReadOnlyList<AnimationClip> GeneratedClips => generatedClips;
        public IReadOnlyList<BlendTree> GeneratedBlendTrees => generatedBlendTrees;
        public Object ErrorContext => (Object)Slot.CloneRoot ?? System.AuthoringComponent;

        public PhantomAnimatorBuildContext(
            BuildContext ndmfContext,
            PhantomSystemBuildState system,
            PhantomSlotBuildState slot,
            PhantomBuildReport report,
            AnimatorController controller)
        {
            NdmfContext = ndmfContext;
            System = system;
            Slot = slot;
            Report = report;
            Controller = controller;

            RootPath = TransformPathUtility.GetRelativePath(
                slot.CloneRoot.transform,
                system.AvatarRoot);
            BaseAvatarPositionPath = slot.BaseAvatarPosition == null
                ? null
                : TransformPathUtility.GetRelativePath(slot.BaseAvatarPosition, system.AvatarRoot);
            ArmaturePath = slot.CloneArmature == null
                ? null
                : TransformPathUtility.GetRelativePath(slot.CloneArmature, system.AvatarRoot);

            if (slot.CloneBoneAvatarPaths.TryGetValue(HumanBodyBones.Hips, out var hipsPath))
            {
                PhantomGrabbingHipsPath = hipsPath;
            }

            PhantomGrabbingHipsConstraintPath = slot.PhantomGrabbingHipsConstraintHost == null
                ? null
                : TransformPathUtility.GetRelativePath(
                    slot.PhantomGrabbingHipsConstraintHost,
                    system.AvatarRoot);
            PhantomGrabbingBoneDisplayPath = slot.PhantomGrabbingBoneDisplayHost == null
                ? null
                : TransformPathUtility.GetRelativePath(
                    slot.PhantomGrabbingBoneDisplayHost,
                    system.AvatarRoot);

            PhantomGrabbingBodyPhysBonePaths = ResolvePaths(
                slot.PhantomGrabbingBodyPhysBoneHosts,
                system.AvatarRoot);
            PhantomGrabbingBodySyncConstraintPaths = ResolvePaths(
                slot.PhantomGrabbingBodySyncConstraintHosts,
                system.AvatarRoot);
            PhantomGrabbingBodyOutputConstraintPaths = ResolvePaths(
                slot.PhantomGrabbingBodyOutputConstraintHosts,
                system.AvatarRoot);
        }

        public AnimationClip CreateClip(string name)
        {
            var clip = new AnimationClip
            {
                name = name,
                frameRate = PhantomAnimatorClipUtility.FramesPerSecond
            };
            generatedClips.Add(clip);
            return clip;
        }

        public BlendTree CreateBlendTree(string name, string parameter)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                useAutomaticThresholds = false
            };
            generatedBlendTrees.Add(tree);
            return tree;
        }

        private static IReadOnlyDictionary<HumanBodyBones, string> ResolvePaths(
            IReadOnlyDictionary<HumanBodyBones, Transform> transforms,
            Transform avatarRoot)
        {
            var paths = new Dictionary<HumanBodyBones, string>();
            foreach (var pair in transforms)
            {
                if (pair.Value != null)
                {
                    paths[pair.Key] = TransformPathUtility.GetRelativePath(pair.Value, avatarRoot);
                }
            }

            return paths;
        }
    }
}
