using nadena.dev.ndmf;
using UnityEngine;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    public static class PreparePhantomAvatarsPass
    {
        public static void Execute(BuildContext ctx)
        {
            try
            {
                CollectPhantomSystemPass.Execute(ctx);
                ValidatePhantomSystemPass.Execute(ctx);
                ClonePhantomAvatarPass.Execute(ctx);
            }
            finally
            {
                // A NDMF sequence guarantees ordering, but unrelated FirstChance passes may still be interleaved.
                // Keep the external prebake session entirely inside this pass so every failure path releases it.
                PhantomPrebakeSession.Release(ctx.AvatarRootObject);
            }
        }
    }

    public static class CollectPhantomSystemPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            state.System = null;
            state.ProjectSettings = PhantomSystemProjectSettings.instance.CreateSnapshot();
            state.BaseParameters.Clear();
            foreach (var pair in PhantomParameterAnalysis.ReadBaseParameters(ctx.AvatarRootObject, ctx))
            {
                state.BaseParameters[pair.Key] = pair.Value;
            }

            var authoringComponents = ctx.AvatarRootObject.GetComponentsInChildren<PhantomAuthoring>(true);
            if (authoringComponents.Length > 1)
            {
                state.Report.Error(
                    $"Avatar '{ctx.AvatarRootObject.name}' contains {authoringComponents.Length} PhantomSystem components. "
                    + "Only one PhantomSystem component is supported per avatar. Merge all slots into one component "
                    + "and remove the additional components before building.",
                    authoringComponents[1]);
                return;
            }

            if (authoringComponents.Length == 0)
            {
                return;
            }

            var authoring = authoringComponents[0];
            var systemState = new PhantomSystemBuildState
            {
                AuthoringComponent = authoring,
                AvatarRoot = ctx.AvatarRootTransform,
                ProjectSettings = state.ProjectSettings
            };

            var slots = authoring.slots ?? new System.Collections.Generic.List<PhantomSlot>();
            for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                var slot = slots[slotIndex];
                var slotId = string.IsNullOrWhiteSpace(slot?.id)
                    ? PhantomSlot.DefaultId
                    : slot.id.Trim();
                systemState.Slots.Add(new PhantomSlotBuildState
                {
                    Slot = slot,
                    SlotId = slotId,
                    SourceAvatar = slot?.phantomAvatar,
                    PrebakedRoot = PhantomPrebakeSession.GetRoot(authoring, slotIndex)
                });
            }

            state.System = systemState;
        }
    }

    public static class ValidatePhantomSystemPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            if (!state.HasWork)
            {
                state.Report.ThrowIfErrors();
                return;
            }

            ValidateCoreParameterNamespaces(state);

            var baseAnimator = ctx.AvatarRootObject.GetComponent<Animator>();
            if (baseAnimator == null)
            {
                state.Report.Error("Base avatar has no Animator component.", ctx.AvatarRootObject);
            }
            else if (!baseAnimator.isHuman)
            {
                state.Report.Error("Base avatar Animator is not humanoid.", baseAnimator);
            }

            var system = state.System;
            var missingPrebakeReported = false;
            if (system.Slots.Count == 0)
            {
                state.Report.Error($"PhantomSystem on '{system.AuthoringComponent.name}' has no slots.", system.AuthoringComponent);
            }

            foreach (var slot in system.Slots)
            {
                if (slot.SourceAvatar == null)
                {
                    state.Report.Error($"Slot '{slot.SlotId}' has no phantom avatar.", system.AuthoringComponent);
                    continue;
                }

                if (slot.SourceAvatar.gameObject == ctx.AvatarRootObject)
                {
                    state.Report.Error($"Slot '{slot.SlotId}' references the avatar currently being built. Assign a separate phantom avatar instead of the base avatar.", slot.SourceAvatar);
                    continue;
                }

                if (slot.SourceAvatar.transform.IsChildOf(ctx.AvatarRootTransform))
                {
                    state.Report.Error($"Slot '{slot.SlotId}' phantom avatar '{slot.SourceAvatar.name}' is inside the base avatar hierarchy. Move the phantom source outside the avatar root before building.", slot.SourceAvatar);
                    continue;
                }

                if (slot.PrebakedRoot == null)
                {
                    if (!missingPrebakeReported)
                    {
                        state.Report.Error(
                            $"Slot '{slot.SlotId}' has no PhantomSystem prebake result. "
                            + "If this was started with Modular Avatar/NDMF Manual Bake, that command does not run "
                            + "the VRChat preprocess hook required by PhantomSystem. "
                            + "Delete the failed manual-bake clone if one was created, select the original avatar's "
                            + "PhantomSystem component, and click 'Bake Avatar with PhantomSystem' in its Inspector. "
                            + "VRChat SDK Build/Upload and Apply on Play continue to prepare phantom sources automatically.",
                            system.AuthoringComponent);
                        missingPrebakeReported = true;
                    }

                    continue;
                }

                var prebakedDescriptor = slot.PrebakedRoot.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                var prebakedAnimator = slot.PrebakedRoot.GetComponent<Animator>();
                if (prebakedDescriptor == null)
                {
                    state.Report.Error($"Prebaked phantom '{slot.SourceAvatar.name}' has no VRCAvatarDescriptor.", slot.PrebakedRoot);
                }
                else
                {
                    slot.ValidSharedParameterNames.Clear();
                    var resolution = PhantomParameterAnalysis.ResolveBuildSharedRules(
                        slot.Slot,
                        state.BaseParameters,
                        prebakedDescriptor);
                    slot.ValidSharedParameterNames.UnionWith(resolution.ValidNames);
                    foreach (var warning in resolution.Warnings)
                    {
                        state.Report.Warning($"Slot '{slot.SlotId}': {warning}", system.AuthoringComponent);
                    }
                }

                if (prebakedAnimator == null || !prebakedAnimator.isHuman)
                {
                    state.Report.Error($"Prebaked phantom '{slot.SourceAvatar.name}' has no humanoid Animator.", slot.PrebakedRoot);
                }
            }

            state.Report.ThrowIfErrors();
        }

        private static void ValidateCoreParameterNamespaces(PhantomBuildState state)
        {
            var owners = new System.Collections.Generic.Dictionary<string, string>(
                System.StringComparer.Ordinal);

            var system = state.System;
            foreach (var slot in system.Slots)
            {
                var activateParameter = PhantomParameterNames.Activate(slot.Slot);
                var owner = slot.SlotId;
                if (owners.TryGetValue(activateParameter, out var existingOwner))
                {
                    state.Report.Error(
                        $"Slot '{owner}' uses the same PhantomSystem core parameter namespace as "
                        + $"'{existingOwner}' ('{activateParameter}'). Assign a unique Slot ID or Parameter Prefix.",
                        system.AuthoringComponent);
                    continue;
                }

                owners.Add(activateParameter, owner);

                var generatedParameters = new System.Collections.Generic.List<string>
                {
                    activateParameter,
                    PhantomParameterNames.Freeze(slot.Slot),
                    PhantomParameterNames.PositionLock(slot.Slot)
                };
                if (slot.Slot.enablePhantomGrabbing)
                {
                    generatedParameters.Add(PhantomParameterNames.PhantomGrabbingContactLeft(slot.Slot));
                    generatedParameters.Add(PhantomParameterNames.PhantomGrabbingContactRight(slot.Slot));
                    generatedParameters.Add(PhantomParameterNames.PhantomGrabbingShowBones(slot.Slot));
                }
                if (slot.Slot.enableScaleControl)
                {
                    generatedParameters.Add(PhantomParameterNames.Scale(slot.Slot));
                    generatedParameters.Add(PhantomParameterNames.Mirror(slot.Slot));
                    generatedParameters.Add(PhantomParameterNames.ScaleReset(slot.Slot));
                }
                if (slot.Slot.enablePhantomView)
                {
                    generatedParameters.Add(PhantomParameterNames.PhantomViewEnabled(slot.Slot));
                    generatedParameters.Add(PhantomParameterNames.PhantomViewStereoStrength(slot.Slot));
                    generatedParameters.Add(PhantomParameterNames.PhantomViewMaskSize(slot.Slot));
                    generatedParameters.Add(PhantomParameterNames.PhantomViewDirectWeight(slot.Slot));
                }
                if (slot.Slot.tryConvertAnimatorTrackingControl && !slot.Slot.removeSourceControls)
                {
                    generatedParameters.AddRange(
                        PhantomTrackingControlGroups.Parameters(slot.Slot));
                    generatedParameters.Add(PhantomParameterNames.TrackingDirectWeight(slot.Slot));
                }

                foreach (var generatedParameter in generatedParameters)
                {
                    if (!state.BaseParameters.ContainsKey(generatedParameter))
                    {
                        continue;
                    }

                    state.Report.Error(
                        $"Slot '{owner}' generated parameter '{generatedParameter}' already exists on the base avatar. "
                        + "Assign a different Slot ID or Parameter Prefix so PhantomSystem controls remain isolated.",
                        system.AuthoringComponent);
                }
            }
        }
    }

    public static class ClonePhantomAvatarPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            if (!state.HasWork)
            {
                return;
            }

            PhantomAvatarCloner.CloneSystem(ctx, state.System);
        }
    }

    public static class ResolvePhantomHumanoidRigPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            if (!state.HasWork)
            {
                return;
            }

            foreach (var slot in state.System.Slots)
            {
                PhantomHierarchyNormalizer.Normalize(state.System, slot, state.Report);
            }

            state.Report.ThrowIfErrors();
        }
    }

    public static class GenerateConstraintRigPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            if (!state.HasWork)
            {
                return;
            }

            foreach (var slot in state.System.Slots)
            {
                PhantomAnimationDriverRigBuilder.Build(slot);
                ConstraintRigBuilder.Build(ctx, slot, state.Report);
                PhantomViewBuilder.Build(ctx, state.System, slot, state.Report);
            }

            state.Report.ThrowIfErrors();
        }
    }

    public static class GenerateAnimatorAssetsPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            if (!state.HasWork)
            {
                return;
            }

            foreach (var slot in state.System.Slots)
            {
                var sourceResult = PhantomSourcePlayableControllerProcessor.Process(
                    ctx,
                    slot,
                    state.System.ProjectSettings,
                    state.Report);
                slot.ProcessedFxController = sourceResult.FxController;
                slot.ProcessedGestureController = sourceResult.GestureController;
                slot.ProcessedActionController = sourceResult.ActionController;
                slot.HasTrackingControlConversion = sourceResult.HasTrackingConversion;
                PhantomAnimatorControllerBuilder.Build(ctx, state.System, slot, state.Report);
            }

            state.Report.ThrowIfErrors();
        }
    }

    public static class InstallMenuAndParameterPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            if (!state.HasWork)
            {
                return;
            }

            foreach (var slot in state.System.Slots)
            {
                PhantomMenuAndParameterBuilder.Install(ctx, state.System, slot, state.Report);
            }

            state.Report.ThrowIfErrors();
        }
    }

    public static class CleanupPrebakedAvatarMetadataPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            if (!state.HasWork)
            {
                return;
            }

            foreach (var slot in state.System.Slots)
            {
                PhantomAvatarCloner.CleanupNestedAvatarComponents(slot);
            }

            state.Report.ThrowIfErrors();
        }
    }

    public static class FinalizeMergeAnimatorsPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            PhantomMergeAnimatorFinalizer.Apply(ctx, state);
            state.Report.ThrowIfErrors();
        }
    }

    public static class ValidatePhantomAnimationBindingsPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            AnimationBindingDiagnostics.InspectFinalAvatar(ctx, state);
            state.Report.ThrowIfErrors();
        }
    }

    public static class RetargetPhantomAnimatorLayerControlsPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            PhantomAnimatorLayerControlRetargeter.Retarget(ctx, state);
            state.Report.ThrowIfErrors();
        }
    }

    public static class CleanupAuthoringComponentsPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            if (state.System?.AuthoringComponent != null)
            {
                Object.DestroyImmediate(state.System.AuthoringComponent);
            }

            state.Report.ThrowIfErrors();
        }
    }

    public static class RenamePhantomArmaturesPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            if (!state.HasWork)
            {
                return;
            }

            PhantomArmatureRenamer.Rename(ctx, state.System);
            state.Report.ThrowIfErrors();
        }
    }
}
