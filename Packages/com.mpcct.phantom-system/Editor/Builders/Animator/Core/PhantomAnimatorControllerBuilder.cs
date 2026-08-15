using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Coordinates the animator modules generated for a slot.</summary>
    public static class PhantomAnimatorControllerBuilder
    {
        public static void Build(
            BuildContext ndmfContext,
            PhantomSystemBuildState system,
            PhantomSlotBuildState slot,
            PhantomBuildReport report)
        {
            if (slot.CloneRoot == null)
            {
                return;
            }

            var controller = new AnimatorController
            {
                name = $"PhantomSystem_{slot.SlotId}_FX",
                layers = new AnimatorControllerLayer[0]
            };
            slot.GeneratedController = controller;

            var context = new PhantomAnimatorBuildContext(
                ndmfContext,
                system,
                slot,
                report,
                controller);

            CoreAnimatorModule.Build(context);
            if (slot.Slot.enablePhantomGrabbing)
            {
                PhantomGrabbingHipsAnimatorModule.Build(context);
                PhantomGrabbingBodyAnimatorModule.Build(context);
                PhantomGrabbingBoneDisplayAnimatorModule.Build(context);
            }
            if (slot.Slot.enableScaleControl)
            {
                ScaleControlAnimatorModule.Build(context);
            }
            if (slot.Slot.enablePhantomView)
            {
                PhantomViewAnimatorModule.BuildVisibility(context);
            }

            PhantomAnimatorGraphUtility.ValidateStateMotions(context);
            SaveGeneratedAssets(context);

            BuildPhantomViewController(ndmfContext, system, slot, report);
        }

        internal static void BuildTracking(
            BuildContext ndmfContext,
            PhantomSystemBuildState system,
            PhantomSlotBuildState slot,
            PhantomBuildReport report)
        {
            BuildTrackingController(ndmfContext, system, slot, report);
        }

        private static void BuildPhantomViewController(
            BuildContext ndmfContext,
            PhantomSystemBuildState system,
            PhantomSlotBuildState slot,
            PhantomBuildReport report)
        {
            if (!slot.Slot.enablePhantomView)
            {
                return;
            }

            var controller = new AnimatorController
            {
                name = $"PhantomSystem_{slot.SlotId}_PhantomView_FX",
                layers = new AnimatorControllerLayer[0]
            };
            var context = new PhantomAnimatorBuildContext(
                ndmfContext,
                system,
                slot,
                report,
                controller);

            PhantomViewAnimatorModule.BuildControls(context);
            if (controller.layers.Length == 0)
            {
                return;
            }

            slot.GeneratedPhantomViewController = controller;
            PhantomAnimatorGraphUtility.ValidateStateMotions(context);
            SaveGeneratedAssets(context);
        }

        private static void BuildTrackingController(
            BuildContext ndmfContext,
            PhantomSystemBuildState system,
            PhantomSlotBuildState slot,
            PhantomBuildReport report)
        {
            if (!slot.HasTrackingControlConversion)
            {
                return;
            }

            var controller = new AnimatorController
            {
                name = $"PhantomSystem_{slot.SlotId}_Tracking_FX",
                layers = new AnimatorControllerLayer[0]
            };
            var context = new PhantomAnimatorBuildContext(
                ndmfContext,
                system,
                slot,
                report,
                controller);

            PhantomTrackingControlAnimatorModule.Build(context);
            if (controller.layers.Length == 0)
            {
                return;
            }

            slot.GeneratedTrackingController = controller;
            PhantomAnimatorGraphUtility.ValidateStateMotions(context);
            SaveGeneratedAssets(context);
        }

        private static void SaveGeneratedAssets(PhantomAnimatorBuildContext context)
        {
            context.NdmfContext.AssetSaver.SaveAsset(context.Controller);
            foreach (var clip in context.GeneratedClips)
            {
                context.NdmfContext.AssetSaver.SaveAsset(clip);
            }
            foreach (var blendTree in context.GeneratedBlendTrees)
            {
                context.NdmfContext.AssetSaver.SaveAsset(blendTree);
            }
        }
    }

    /// <summary>
    /// Installs a lower-priority FX layer which gives the animation driver an explicit
    /// zero-muscle Humanoid rotation pose when Gesture or Action omits a bone binding.
    /// </summary>
    internal static class PhantomDriverNeutralAnimatorBuilder
    {
        private const string LayerName = "DriverNeutralPose";

        public static void Install(
            BuildContext context,
            PhantomSystemBuildState system,
            PhantomSlotBuildState slot,
            GameObject host,
            PhantomBuildReport report)
        {
            if (!ShouldBuild(slot))
            {
                return;
            }

            if (context == null || system?.AvatarRoot == null || host == null)
            {
                report.InternalError(
                    $"Slot '{slot.SlotId}' could not create its Driver neutral animator because the build context is incomplete.");
                return;
            }

            if (slot.CloneAnimator == null
                || slot.CloneAnimator.avatar == null
                || !slot.CloneAnimator.isHuman)
            {
                report.Error(
                    $"Slot '{slot.SlotId}' could not sample its Driver neutral pose because its cloned Animator is not a valid Humanoid.",
                    slot.CloneRoot);
                return;
            }

            if (!TryResolveOutputPaths(system, slot, report, out var outputPaths))
            {
                return;
            }

            var clip = new AnimationClip
            {
                name = $"PhantomSystem_{slot.SlotId}_DriverNeutralPose",
                frameRate = PhantomAnimatorClipUtility.FramesPerSecond
            };

            try
            {
                PhantomHumanoidClipBaker.WriteNeutralPoseRotations(
                    clip,
                    slot.CloneRoot,
                    outputPaths,
                    slot.AnimationDriverPoseParentClonePaths);
            }
            catch (System.Exception exception)
            {
                Object.DestroyImmediate(clip);
                report.Error(
                    $"Slot '{slot.SlotId}' could not sample its Humanoid Driver neutral pose: {exception.Message}",
                    slot.CloneRoot);
                return;
            }

            var controller = CreateController(slot.SlotId, slot.HierarchyName, clip);
            var buildContext = new PhantomAnimatorBuildContext(
                context,
                system,
                slot,
                report,
                controller);
            PhantomAnimatorGraphUtility.ValidateStateMotions(buildContext);

            context.AssetSaver.SaveAsset(controller);
            context.AssetSaver.SaveAsset(clip);
            slot.GeneratedDriverNeutralController = controller;

            var mergeAnimator = host.AddComponent<ModularAvatarMergeAnimator>();
            mergeAnimator.animator = controller;
            mergeAnimator.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            mergeAnimator.pathMode = MergeAnimatorPathMode.Absolute;
            mergeAnimator.matchAvatarWriteDefaults = true;
            slot.DriverNeutralMergeAnimator = mergeAnimator;
        }

        internal static AnimatorController CreateController(
            string slotId,
            string slotHierarchyName,
            AnimationClip clip)
        {
            if (clip == null)
            {
                throw new System.ArgumentNullException(nameof(clip));
            }

            var controller = new AnimatorController
            {
                name = $"PhantomSystem_{slotId}_DriverNeutral_FX",
                layers = new AnimatorControllerLayer[0]
            };
            var layer = PhantomAnimatorGraphUtility.AddLayer(
                controller,
                PhantomAnimatorGraphUtility.BuildSlotLayerName(
                    slotHierarchyName,
                    LayerName));
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            PhantomAnimatorGraphUtility.AddState(layer.stateMachine, clip);
            return controller;
        }

        internal static bool ShouldBuild(PhantomSlotBuildState slot)
        {
            return slot?.Slot != null
                   && !slot.Slot.removeSourceControls
                   && slot.AnimationDriverBones.Count > 0
                   && (slot.SourceGestureMergeAnimator != null
                       || slot.SourceActionMergeAnimator != null);
        }

        private static bool TryResolveOutputPaths(
            PhantomSystemBuildState system,
            PhantomSlotBuildState slot,
            PhantomBuildReport report,
            out IReadOnlyDictionary<HumanBodyBones, string> outputPaths)
        {
            var resolved = new Dictionary<HumanBodyBones, string>();
            foreach (var pair in slot.AnimationDriverBones.OrderBy(pair => (int)pair.Key))
            {
                if (pair.Value == null)
                {
                    report.Error(
                        $"Slot '{slot.SlotId}' has no Driver transform for Humanoid bone '{pair.Key}'.",
                        slot.CloneRoot);
                    outputPaths = null;
                    return false;
                }

                var path = TransformPathUtility.GetRelativePath(pair.Value, system.AvatarRoot);
                if (path == null)
                {
                    report.Error(
                        $"Slot '{slot.SlotId}' could not resolve the avatar-relative Driver path for Humanoid bone '{pair.Key}'.",
                        pair.Value);
                    outputPaths = null;
                    return false;
                }

                if (!slot.AnimationDriverPoseParentClonePaths.ContainsKey(pair.Key))
                {
                    report.Error(
                        $"Slot '{slot.SlotId}' could not resolve the sampling pose parent for Humanoid bone '{pair.Key}'.",
                        pair.Value);
                    outputPaths = null;
                    return false;
                }

                resolved[pair.Key] = path;
            }

            outputPaths = resolved;
            return true;
        }
    }
}
