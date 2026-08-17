using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.fluent;

[assembly: ExportsPlugin(typeof(MPCCT.PhantomSystem.Editor.PhantomSystemPlugin))]

namespace MPCCT.PhantomSystem.Editor
{
    public sealed class PhantomSystemPlugin : Plugin<PhantomSystemPlugin>
    {
        public override string DisplayName => "PhantomSystem";
        public override string QualifiedName => "com.mpcct.phantom-system";

        protected override void Configure()
        {
            InPhase(BuildPhase.FirstChance)
                .Run("Prepare Phantom Avatars", ctx => PhantomBuildPassRunner.Run(ctx, PreparePhantomAvatarsPass.Execute))
                .Then
                .Run("Resolve Phantom Humanoid Rig", ctx => PhantomBuildPassRunner.Run(ctx, ResolvePhantomHumanoidRigPass.Execute));

            InPhase(BuildPhase.Generating)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Generate Phantom Constraint Rig", ctx => PhantomBuildPassRunner.Run(ctx, GenerateConstraintRigPass.Execute))
                .Then
                .Run("Generate Phantom Animator Assets", ctx => PhantomBuildPassRunner.Run(ctx, GenerateAnimatorAssetsPass.Execute))
                .Then
                .Run("Install Phantom Menus and Parameters", ctx => PhantomBuildPassRunner.Run(ctx, InstallMenuAndParameterPass.Execute))
                .Then
                .Run("Cleanup Prebaked Avatar Metadata", ctx => PhantomBuildPassRunner.Run(ctx, CleanupPrebakedAvatarMetadataPass.Execute));

            var preModularAvatar = InPhase(BuildPhase.Transforming)
                .BeforePlugin("nadena.dev.modular-avatar");

            preModularAvatar.WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
            {
                sequence.Run(
                    "Convert Phantom Source Playables",
                    ctx => PhantomBuildPassRunner.Run(ctx, ConvertPhantomSourcePlayablesPass.Execute));
            });

            preModularAvatar
                .Run(
                    "Cleanup Phantom Sampling Animators",
                    ctx => PhantomBuildPassRunner.Run(ctx, CleanupPhantomSamplingAnimatorsPass.Execute))
                .Then
                .Run(
                    "Generate Phantom Tracking Animator Assets",
                    ctx => PhantomBuildPassRunner.Run(ctx, GenerateTrackingAnimatorAssetsPass.Execute))
                .Then
                .Run(
                    "Finalize Phantom Merge Animators",
                    ctx => PhantomBuildPassRunner.Run(ctx, FinalizeMergeAnimatorsPass.Execute));

            var postModularAvatar = InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .BeforePlugin("nadena.dev.modular-avatar.late-transform-stages");

            postModularAvatar.WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
            {
                sequence.Run("Rename Phantom Armatures", ctx => PhantomBuildPassRunner.Run(ctx, RenamePhantomArmaturesPass.Execute))
                    .Then
                    .Run("Retarget Phantom Animator Layer Controls", ctx => PhantomBuildPassRunner.Run(ctx, RetargetPhantomAnimatorLayerControlsPass.Execute))
                    .Then
                    .Run("Cleanup Phantom Authoring Components", ctx => PhantomBuildPassRunner.Run(ctx, CleanupAuthoringComponentsPass.Execute));
            });

            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run("Validate Phantom Animator Layer Controls", ctx => PhantomBuildPassRunner.Run(ctx, ValidatePhantomAnimatorLayerControlsPass.Execute))
                .Then
                .Run("Validate Phantom Animation Bindings", ctx => PhantomBuildPassRunner.Run(ctx, ValidatePhantomAnimationBindingsPass.Execute));
        }
    }
}
