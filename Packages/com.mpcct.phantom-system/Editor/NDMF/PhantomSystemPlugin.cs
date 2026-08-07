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
                .Run("Prepare Phantom Avatars", PreparePhantomAvatarsPass.Execute)
                .Then
                .Run("Resolve Phantom Humanoid Rig", ResolvePhantomHumanoidRigPass.Execute);

            InPhase(BuildPhase.Generating)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Generate Phantom Constraint Rig", GenerateConstraintRigPass.Execute)
                .Then
                .Run("Generate Phantom Animator Assets", GenerateAnimatorAssetsPass.Execute)
                .Then
                .Run("Install Phantom Menus and Parameters", InstallMenuAndParameterPass.Execute)
                .Then
                .Run("Cleanup Prebaked Avatar Metadata", CleanupPrebakedAvatarMetadataPass.Execute);

            InPhase(BuildPhase.Transforming)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Finalize Phantom Merge Animators", FinalizeMergeAnimatorsPass.Execute);

            var postModularAvatar = InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .BeforePlugin("nadena.dev.modular-avatar.late-transform-stages");

            postModularAvatar.WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
            {
                sequence.Run("Rename Phantom Armatures", RenamePhantomArmaturesPass.Execute)
                    .Then
                    .Run("Cleanup Phantom Authoring Components", CleanupAuthoringComponentsPass.Execute);
            });

            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run("Retarget Phantom Animator Layer Controls", RetargetPhantomAnimatorLayerControlsPass.Execute)
                .Then
                .Run("Validate Phantom Animation Bindings", ValidatePhantomAnimationBindingsPass.Execute);
        }
    }
}
