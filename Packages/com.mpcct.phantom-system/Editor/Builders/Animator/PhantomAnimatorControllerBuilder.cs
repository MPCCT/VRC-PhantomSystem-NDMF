using nadena.dev.ndmf;
using UnityEditor.Animations;

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
            PhantomTrackingControlAnimatorModule.Build(context);

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
}
