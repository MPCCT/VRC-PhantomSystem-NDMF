using System.Linq;
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
                var identity = PhantomSlotIdentity.Create(slot);
                systemState.Slots.Add(new PhantomSlotBuildState
                {
                    Slot = slot,
                    SlotId = identity.SlotId,
                    Identity = identity,
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
                state.Report.AbortIfErrors();
                return;
            }

            ReportValidation(
                state.Report,
                PhantomSourceValidator.ValidateAuthoring(state.System.AuthoringComponent),
                includeNonErrors: false);
            state.Report.AbortIfErrors();

            ReportValidation(
                state.Report,
                PhantomSourceValidator.ValidatePrebakedState(state),
                includeNonErrors: true);
            state.Report.AbortIfErrors();
            ResolveParameters(ctx, state);

            // PreparePhantomAvatarsPass continues with cloning, so parameter resolution
            // errors must terminate this composite pass before it mutates the avatar.
            state.Report.AbortIfErrors();
        }

        private static void ResolveParameters(BuildContext context, PhantomBuildState state)
        {
            var inputs = new System.Collections.Generic.List<PhantomParameterSlotInput>();
            foreach (var slot in state.System.Slots)
            {
                var definitions = PhantomParameterAnalysis.ReadParametersForObject(
                        slot.PrebakedRoot,
                        context)
                    .Values
                    .ToList();
                definitions.AddRange(
                    PhantomParameterAnalysis.ReadDynamicParameterPrefixes(slot.PrebakedRoot));
                inputs.Add(new PhantomParameterSlotInput
                {
                    Slot = slot.Slot,
                    Identity = slot.Identity,
                    SourceParameters = definitions
                });
            }

            var resolution = PhantomParameterResolver.Resolve(state.BaseParameters, inputs);
            foreach (var error in resolution.Errors)
            {
                state.Report.Error(error, state.System.AuthoringComponent);
            }

            for (var index = 0; index < state.System.Slots.Count && index < resolution.Slots.Count; index++)
            {
                var slot = state.System.Slots[index];
                slot.ParameterResolution = resolution.Slots[index];
                slot.ValidSharedParameterNames.Clear();
                slot.ValidSharedParameterNames.UnionWith(resolution.Slots[index].SharedOriginalNames);
            }
        }

        private static void ReportValidation(
            PhantomBuildReport buildReport,
            PhantomSourceValidationReport validation,
            bool includeNonErrors)
        {
            foreach (var issue in validation.GlobalIssues)
            {
                ReportIssue(buildReport, issue, includeNonErrors);
            }

            foreach (var slot in validation.Slots)
            {
                foreach (var issue in slot.Issues)
                {
                    ReportIssue(buildReport, issue, includeNonErrors);
                }
            }
        }

        private static void ReportIssue(
            PhantomBuildReport report,
            PhantomValidationIssue issue,
            bool includeNonErrors)
        {
            if (!includeNonErrors
                && (issue.Severity == PhantomValidationSeverity.Info
                    || issue.Severity == PhantomValidationSeverity.Warning))
            {
                return;
            }

            var message = string.IsNullOrEmpty(issue.Code)
                ? issue.Message
                : $"[{issue.Code}] {issue.Message}";
            switch (issue.Severity)
            {
                case PhantomValidationSeverity.Info:
                    report.Info(message, issue.Context);
                    break;
                case PhantomValidationSeverity.Warning:
                    report.Warning(message, issue.Context);
                    break;
                case PhantomValidationSeverity.InternalError:
                    report.InternalError(message, issue.Context);
                    break;
                default:
                    report.Error(message, issue.Context);
                    break;
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
        }
    }

    public static class FinalizeMergeAnimatorsPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            PhantomMergeAnimatorFinalizer.Apply(ctx, state);
        }
    }

    public static class ValidatePhantomAnimationBindingsPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            AnimationBindingDiagnostics.InspectFinalAvatar(ctx, state);
        }
    }

    public static class RetargetPhantomAnimatorLayerControlsPass
    {
        public static void Execute(BuildContext ctx)
        {
            var state = ctx.GetState<PhantomBuildState>();
            PhantomAnimatorLayerControlRetargeter.Retarget(ctx, state);
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
        }
    }
}
