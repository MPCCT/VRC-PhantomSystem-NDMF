using L = MPCCT.PhantomSystem.Editor.PhantomLocalization;
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using PhantomAuthoring = MPCCT.PhantomSystem.PhantomSystem;

namespace MPCCT.PhantomSystem.Editor
{
    internal enum PhantomValidationSeverity
    {
        Info,
        Warning,
        ConfigurationError,
        InternalError
    }

    internal enum PhantomCompatibilityStatus
    {
        NotScanned,
        Compatible,
        Warnings
    }

    internal sealed class PhantomValidationIssue
    {
        public string Code;
        public PhantomValidationSeverity Severity;
        internal PhantomDiagnostic Diagnostic;
        public string Message { get => Diagnostic?.ToString(); set => Diagnostic = value; }
        public UnityEngine.Object Context;
        public int SlotIndex = -1;
        public UnityEngine.Object[] SelectionTargets;
    }

    internal sealed class PhantomSlotValidationResult
    {
        public List<PhantomValidationIssue> Issues { get; } = new List<PhantomValidationIssue>();
        public PhantomCompatibilityStatus CompatibilityStatus { get; set; } =
            PhantomCompatibilityStatus.NotScanned;
        public int NdmfEditorOnlyComponentCount { get; set; }
        public int UnclassifiedComponentCount { get; set; }
        public int UnclassifiedComponentTypeCount { get; set; }

        public bool HasErrors => Issues.Any(issue =>
            issue.Severity == PhantomValidationSeverity.ConfigurationError
            || issue.Severity == PhantomValidationSeverity.InternalError);
        public bool HasWarnings => Issues.Any(issue => issue.Severity == PhantomValidationSeverity.Warning);
        public bool HasInfo => Issues.Any(issue => issue.Severity == PhantomValidationSeverity.Info);
    }

    internal sealed class PhantomSourceValidationReport
    {
        public List<PhantomValidationIssue> GlobalIssues { get; } =
            new List<PhantomValidationIssue>();
        public List<PhantomSlotValidationResult> Slots { get; } =
            new List<PhantomSlotValidationResult>();

        public bool HasErrors => GlobalIssues.Any(issue =>
                                     issue.Severity == PhantomValidationSeverity.ConfigurationError
                                     || issue.Severity == PhantomValidationSeverity.InternalError)
                                 || Slots.Any(slot => slot.HasErrors);
    }

    internal static class PhantomSourceValidator
    {
        // VRChat SDK components are distributed across several assemblies.
        // Match the known assembly names explicitly to avoid treating SDK components as third-party.
        private static readonly HashSet<string> KnownVrcSdkAssemblyNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "VRCCore-Editor",
                "VRCSDKBase",
                "VRCSDKBase-Editor",
                "VRCSDK3A",
                "VRC.SDKBase",
                "VRC.SDKBase.Editor",
                "VRC.SDK3A",
                "VRC.Dynamics",
                "VRC.SDK3.Dynamics.Contact",
                "VRC.SDK3.Dynamics.PhysBone",
                "VRC.SDK3.Dynamics.Constraint",
                "VRC.SDK3.Dynamics.Raycast"
            };

        public static PhantomSourceValidationReport Validate(PhantomAuthoring authoring)
        {
            return Validate(authoring, PhantomParameterPlanner.Analyze(authoring));
        }

        internal static PhantomSourceValidationReport Validate(
            PhantomAuthoring authoring,
            PhantomParameterPlan parameterPlan)
        {
            return ValidateAuthoring(authoring, parameterPlan, null);
        }

        internal static PhantomSourceValidationReport Validate(
            PhantomAuthoring authoring,
            PhantomParameterPlan parameterPlan,
            ComputeContext previewContext)
        {
            return ValidateAuthoring(authoring, parameterPlan, previewContext);
        }

        public static PhantomSourceValidationReport ValidateAuthoring(PhantomAuthoring authoring)
        {
            return ValidateAuthoring(authoring, PhantomParameterPlanner.Analyze(authoring), null);
        }

        private static PhantomSourceValidationReport ValidateAuthoring(
            PhantomAuthoring authoring,
            PhantomParameterPlan parameterPlan,
            ComputeContext previewContext)
        {
            var report = new PhantomSourceValidationReport();
            if (authoring == null)
            {
                return report;
            }

            ObserveObject(previewContext, authoring);
            ObservePath(previewContext, authoring.transform);
            var slots = authoring.slots ?? new List<PhantomSlot>();
            for (var index = 0; index < slots.Count; index++)
            {
                report.Slots.Add(new PhantomSlotValidationResult());
            }

            var baseDescriptor = FindAvatarDescriptor(authoring.transform, previewContext);
            ValidateBaseAvatar(baseDescriptor, authoring, slots, report, previewContext);
            AddDuplicateSlotIdIssues(slots, report, authoring);
            AddDuplicateHierarchyNameIssues(slots, report, authoring);
            AddDuplicateNamespaceIssues(slots, report, authoring);

            for (var index = 0; index < slots.Count; index++)
            {
                ValidateSlot(
                    slots[index],
                    report.Slots[index],
                    baseDescriptor,
                    authoring,
                    previewContext);
            }

            parameterPlan ??= PhantomParameterPlan.Empty;
            foreach (var error in parameterPlan.Errors.Where(error =>
                         error?.Key != "diagnostic.parameter.duplicatePrefix"))
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS200", error, authoring);
            }
            for (var index = 0; index < report.Slots.Count && index < parameterPlan.Slots.Count; index++)
            {
                foreach (var rename in parameterPlan.Slots[index].AutomaticRenames)
                {
                    Add(
                        report.Slots[index],
                        PhantomValidationSeverity.Warning,
                        "PHS201",
                        L.D("diagnostic.validation.PHS201", rename.OriginalName, rename.FinalName, rename.Reason),
                        authoring);
                }
            }

            AssignSlotIndices(report);

            return report;
        }

        public static PhantomSourceValidationReport ValidatePrebakedState(PhantomBuildState state)
        {
            var report = new PhantomSourceValidationReport();
            var system = state?.System;
            if (system == null)
            {
                return report;
            }

            foreach (var unused in system.Slots)
            {
                report.Slots.Add(new PhantomSlotValidationResult());
            }

            if (system.Slots.Count == 0)
            {
                AddGlobal(
                    report,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS300",
                    L.D("diagnostic.validation.PHS300", system.AuthoringComponent.name),
                    system.AuthoringComponent);
                return report;
            }

            for (var index = 0; index < system.Slots.Count; index++)
            {
                var slot = system.Slots[index];
                var result = report.Slots[index];
                if (slot.PrebakedRoot == null)
                {
                    Add(
                        result,
                        PhantomValidationSeverity.ConfigurationError,
                        "PHS301",
                        L.D("diagnostic.validation.PHS301", slot.SlotId),
                        system.AuthoringComponent);
                    continue;
                }

                var descriptor = slot.PrebakedRoot.GetComponent<VRCAvatarDescriptor>();
                if (descriptor == null)
                {
                    Add(
                        result,
                        PhantomValidationSeverity.InternalError,
                        "PHS302",
                        L.D("diagnostic.validation.PHS302", slot.SlotId),
                        slot.PrebakedRoot);
                }

                var animator = slot.PrebakedRoot.GetComponent<Animator>();
                if (animator == null || !animator.isHuman)
                {
                    Add(
                        result,
                        PhantomValidationSeverity.InternalError,
                        "PHS303",
                        L.D("diagnostic.validation.PHS303", slot.SlotId),
                        slot.PrebakedRoot);
                }
            }

            AssignSlotIndices(report);

            return report;
        }

        private static void ValidateSlot(
            PhantomSlot slot,
            PhantomSlotValidationResult result,
            VRCAvatarDescriptor baseDescriptor,
            PhantomAuthoring authoring,
            ComputeContext previewContext)
        {
            if (slot == null)
            {
                Add(result, PhantomValidationSeverity.ConfigurationError, "PHS001", L.D("diagnostic.validation.PHS001"), authoring);
                return;
            }

            if (string.IsNullOrWhiteSpace(slot.id))
            {
                Add(
                    result,
                    PhantomValidationSeverity.Info,
                    "PHS002",
                    L.D("diagnostic.validation.PHS002", PhantomSlot.DefaultId),
                    authoring);
            }

            var source = slot.phantomAvatar;
            if (source == null)
            {
                Add(result, PhantomValidationSeverity.ConfigurationError, "PHS010", L.D("diagnostic.validation.PHS010"), authoring);
                return;
            }

            ObserveDescriptor(previewContext, source);
            ObservePath(previewContext, source.transform);
            if (baseDescriptor != null && source == baseDescriptor)
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS011",
                    L.D("diagnostic.validation.PHS011"),
                    source);
                return;
            }

            if (baseDescriptor != null && source.transform.IsChildOf(baseDescriptor.transform))
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS012",
                    L.D("diagnostic.validation.PHS012"),
                    source);
            }

            var animator = GetComponent<Animator>(previewContext, source.gameObject);
            if (animator == null)
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS013",
                    L.D("diagnostic.validation.PHS013"),
                    source);
            }
            else if (animator.avatar == null)
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS014",
                    L.D("diagnostic.validation.PHS014"),
                    animator);
            }
            else if (!animator.avatar.isValid || !animator.avatar.isHuman || !animator.isHuman)
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS015",
                    L.D("diagnostic.validation.PHS015"),
                    animator);
            }
            else if (!HasHumanoidBone(animator, HumanBodyBones.Hips))
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS016",
                    L.D("diagnostic.validation.PHS016"),
                    animator);
            }
            else if (slot.enablePhantomView && !HasHumanoidBone(animator, HumanBodyBones.Head))
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS017",
                    L.D("diagnostic.validation.PHS017"),
                    animator);
            }

            ObserveHumanoidRig(previewContext, animator);
            var nestedSystems = GetComponentsInChildren<PhantomAuthoring>(
                previewContext,
                source.gameObject);
            if (nestedSystems.Length > 0)
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS018",
                    L.D("diagnostic.validation.PHS018", nestedSystems.Length),
                    nestedSystems[0]);
            }

            var missingScriptObjects = FindMissingScriptGameObjects(
                source.gameObject,
                previewContext,
                out var missingScriptCount);
            if (missingScriptCount > 0)
            {
                Add(
                    result,
                    PhantomValidationSeverity.Warning,
                    "PHS020",
                    L.D("diagnostic.validation.PHS020", missingScriptCount, missingScriptObjects.Length),
                    source,
                    missingScriptObjects);
            }

            ScanComponentCompatibility(source, result, previewContext);
        }

        private static void ValidateBaseAvatar(
            VRCAvatarDescriptor baseDescriptor,
            PhantomAuthoring authoring,
            IReadOnlyList<PhantomSlot> slots,
            PhantomSourceValidationReport report,
            ComputeContext previewContext)
        {
            var context = baseDescriptor != null ? (UnityEngine.Object)baseDescriptor : authoring;
            ObserveDescriptor(previewContext, baseDescriptor);
            var animator = baseDescriptor != null
                ? GetComponent<Animator>(previewContext, baseDescriptor.gameObject)
                : null;
            if (baseDescriptor == null)
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS100",
                    L.D("diagnostic.validation.PHS100"), authoring);
                return;
            }

            if (animator == null)
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS101",
                    L.D("diagnostic.validation.PHS101"), context);
                return;
            }

            ObserveHumanoidRig(previewContext, animator);
            if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman || !animator.isHuman)
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS102",
                    L.D("diagnostic.validation.PHS102"), animator);
                return;
            }

            if (!HasHumanoidBone(animator, HumanBodyBones.Hips))
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS103",
                    L.D("diagnostic.validation.PHS103"), animator);
            }

            if (slots.Any(slot => slot != null && slot.enablePhantomView)
                && !HasHumanoidBone(animator, HumanBodyBones.Head))
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS104",
                    L.D("diagnostic.validation.PHS104"), animator);
            }

            if (slots.Any(slot => slot != null && slot.enablePhantomGrabbing)
                && (!HasHumanoidBone(animator, HumanBodyBones.LeftHand)
                    || !HasHumanoidBone(animator, HumanBodyBones.RightHand)))
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS105",
                    L.D("diagnostic.validation.PHS105"), animator);
            }
        }

        private static void ScanComponentCompatibility(
            VRCAvatarDescriptor source,
            PhantomSlotValidationResult result,
            ComputeContext previewContext)
        {
            var unclassifiedComponents = new List<MonoBehaviour>();
            foreach (var component in GetComponentsInChildren<MonoBehaviour>(
                         previewContext,
                         source.gameObject))
            {
                if (component == null)
                {
                    continue;
                }

                if (component is INDMFEditorOnly)
                {
                    result.NdmfEditorOnlyComponentCount++;
                    continue;
                }

                var componentType = component.GetType();
                if (IsKnownFrameworkComponent(componentType))
                {
                    continue;
                }

                result.UnclassifiedComponentCount++;
                unclassifiedComponents.Add(component);
            }

            foreach (var componentGroup in unclassifiedComponents
                         .GroupBy(component => component.GetType())
                         .OrderBy(group => group.Key.FullName, StringComparer.Ordinal))
            {
                result.UnclassifiedComponentTypeCount++;
                var components = componentGroup.ToArray();
                var gameObjects = components
                    .Select(component => component.gameObject)
                    .Where(gameObject => gameObject != null)
                    .Distinct()
                    .Cast<UnityEngine.Object>()
                    .ToArray();
                var componentType = componentGroup.Key;
                Add(
                    result,
                    PhantomValidationSeverity.Warning,
                    "PHS021",
                    L.D("diagnostic.validation.PHS021", componentType.FullName),
                    components[0],
                    gameObjects);
            }

            result.CompatibilityStatus = result.UnclassifiedComponentCount > 0
                ? PhantomCompatibilityStatus.Warnings
                : PhantomCompatibilityStatus.Compatible;
        }

        private static bool IsKnownFrameworkComponent(Type componentType)
        {
            if (componentType == null)
            {
                return false;
            }

            var assemblyName = componentType.Assembly.GetName().Name;
            return !string.IsNullOrEmpty(assemblyName)
                   && (KnownVrcSdkAssemblyNames.Contains(assemblyName)
                       || assemblyName.StartsWith("nadena.dev.ndmf", StringComparison.Ordinal)
                       || assemblyName.StartsWith("nadena.dev.modular-avatar", StringComparison.Ordinal));
        }

        private static void AddDuplicateSlotIdIssues(
            IReadOnlyList<PhantomSlot> slots,
            PhantomSourceValidationReport report,
            PhantomAuthoring authoring)
        {
            var groups = slots
                .Select((slot, index) => new
                {
                    Index = index,
                    Name = PhantomSlotIdentity.Create(slot).SlotId
                })
                .GroupBy(item => item.Name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1);

            foreach (var group in groups)
            {
                foreach (var item in group)
                {
                    Add(
                        report.Slots[item.Index],
                        PhantomValidationSeverity.ConfigurationError,
                        "PHS030",
                        L.D("diagnostic.validation.PHS030", group.Key),
                        authoring);
                }
            }
        }

        private static void AddDuplicateNamespaceIssues(
            IReadOnlyList<PhantomSlot> slots,
            PhantomSourceValidationReport report,
            PhantomAuthoring authoring)
        {
            var groups = slots
                .Select((slot, index) => new
                {
                    Index = index,
                    Identity = PhantomSlotIdentity.Create(slot),
                    Name = PhantomParameterNames.Activate(slot)
                })
                .GroupBy(item => item.Name, StringComparer.Ordinal)
                .Where(group => group
                    .Select(item => item.Identity.SlotId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() > 1);

            foreach (var group in groups)
            {
                foreach (var item in group)
                {
                    Add(
                        report.Slots[item.Index],
                        PhantomValidationSeverity.ConfigurationError,
                        "PHS032",
                        L.D("diagnostic.validation.PHS032", group.Key),
                        authoring);
                }
            }
        }

        private static void AddDuplicateHierarchyNameIssues(
            IReadOnlyList<PhantomSlot> slots,
            PhantomSourceValidationReport report,
            PhantomAuthoring authoring)
        {
            var groups = slots
                .Select((slot, index) => new
                {
                    Index = index,
                    Identity = PhantomSlotIdentity.Create(slot)
                })
                .GroupBy(item => item.Identity.HierarchyName, StringComparer.Ordinal)
                .Where(group => group
                    .Select(item => item.Identity.SlotId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() > 1);

            foreach (var group in groups)
            {
                foreach (var item in group)
                {
                    Add(
                        report.Slots[item.Index],
                        PhantomValidationSeverity.ConfigurationError,
                        "PHS031",
                        L.D("diagnostic.validation.PHS031", group.Key),
                        authoring);
                }
            }
        }

        private static UnityEngine.Object[] FindMissingScriptGameObjects(
            GameObject root,
            ComputeContext previewContext,
            out int missingScriptCount)
        {
            missingScriptCount = 0;
            var gameObjects = new List<UnityEngine.Object>();
            foreach (var transform in GetComponentsInChildren<Transform>(previewContext, root))
            {
                var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                if (count <= 0)
                {
                    continue;
                }

                missingScriptCount += count;
                gameObjects.Add(transform.gameObject);
            }

            return gameObjects.ToArray();
        }

        private static bool HasHumanoidBone(Animator animator, HumanBodyBones bone)
        {
            if (animator == null
                || animator.avatar == null
                || !animator.avatar.isValid
                || !animator.avatar.isHuman
                || !animator.isHuman)
            {
                return false;
            }

            try
            {
                return animator.GetBoneTransform(bone) != null;
            }
            catch (InvalidOperationException)
            {
                // Unity can temporarily leave an Animator without a bound runtime Avatar
                // while importing or refreshing serialized references. Inspector validation
                // must report an unavailable bone rather than breaking its delayed refresh.
                return false;
            }
        }

        private static VRCAvatarDescriptor FindAvatarDescriptor(
            Transform start,
            ComputeContext previewContext)
        {
            for (var current = start; current != null; current = current.parent)
            {
                ObservePath(previewContext, current);
                var descriptor = GetComponent<VRCAvatarDescriptor>(
                    previewContext,
                    current.gameObject);
                if (descriptor != null)
                {
                    return descriptor;
                }
            }

            return null;
        }

        private static void ObserveDescriptor(
            ComputeContext previewContext,
            VRCAvatarDescriptor descriptor)
        {
            if (previewContext == null || descriptor == null)
            {
                return;
            }

            previewContext.Observe(
                descriptor,
                DescriptorConfigurationSignature);
            if (descriptor.expressionParameters != null)
            {
                previewContext.Observe(descriptor.expressionParameters);
            }
            if (descriptor.expressionsMenu != null)
            {
                previewContext.Observe(descriptor.expressionsMenu);
            }
        }

        private static void ObserveHumanoidRig(
            ComputeContext previewContext,
            Animator animator)
        {
            if (previewContext == null || animator == null)
            {
                return;
            }

            previewContext.Observe(animator, value => value.avatar);
            if (animator.avatar != null)
            {
                previewContext.Observe(animator.avatar);
            }

            foreach (var transform in previewContext.GetComponentsInChildren<Transform>(
                         animator.gameObject,
                         true))
            {
                previewContext.Observe(transform.gameObject, gameObject => gameObject.name);
                previewContext.ObservePath(transform);
            }
        }

        private static void ObservePath(ComputeContext previewContext, Transform transform)
        {
            if (previewContext != null && transform != null)
            {
                previewContext.ObservePath(transform);
            }
        }

        private static T GetComponent<T>(ComputeContext previewContext, GameObject gameObject)
            where T : class
        {
            return previewContext != null
                ? previewContext.GetComponent<T>(gameObject)
                : gameObject != null
                    ? gameObject.GetComponent<T>()
                    : null;
        }

        private static T[] GetComponentsInChildren<T>(
            ComputeContext previewContext,
            GameObject gameObject)
            where T : class
        {
            return previewContext != null
                ? previewContext.GetComponentsInChildren<T>(gameObject, true)
                : gameObject != null
                    ? gameObject.GetComponentsInChildren<T>(true)
                    : Array.Empty<T>();
        }

        private static void ObserveObject<T>(ComputeContext previewContext, T obj)
            where T : UnityEngine.Object
        {
            if (previewContext != null && obj != null)
            {
                previewContext.Observe(obj);
            }
        }

        private static long DescriptorConfigurationSignature(VRCAvatarDescriptor descriptor)
        {
            unchecked
            {
                var hash = 1469598103934665603L;
                AddSignatureValue(ref hash, descriptor.customizeAnimationLayers ? 1 : 0);
                AddSignatureValue(ref hash, ObjectInstanceId(descriptor.expressionParameters));
                AddSignatureValue(ref hash, ObjectInstanceId(descriptor.expressionsMenu));
                var layers = descriptor.baseAnimationLayers ??
                             Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                AddSignatureValue(ref hash, layers.Length);
                foreach (var layer in layers)
                {
                    AddSignatureValue(ref hash, (int)layer.type);
                    AddSignatureValue(ref hash, layer.isDefault ? 1 : 0);
                    AddSignatureValue(ref hash, ObjectInstanceId(layer.animatorController));
                    AddSignatureValue(ref hash, ObjectInstanceId(layer.mask));
                }

                return hash;
            }
        }

        private static void AddSignatureValue(ref long hash, int value)
        {
            unchecked
            {
                hash = (hash ^ value) * 1099511628211L;
            }
        }

        private static int ObjectInstanceId(UnityEngine.Object obj)
        {
            return obj != null ? obj.GetInstanceID() : 0;
        }

        private static void Add(
            PhantomSlotValidationResult result,
            PhantomValidationSeverity severity,
            string code,
            PhantomDiagnostic message,
            UnityEngine.Object context,
            UnityEngine.Object[] selectionTargets = null)
        {
            result.Issues.Add(new PhantomValidationIssue
            {
                Code = code,
                Severity = severity,
                Diagnostic = message,
                Context = context,
                SelectionTargets = selectionTargets
                    ?? (context != null
                        ? new[] { context }
                        : Array.Empty<UnityEngine.Object>())
            });
        }

        private static void AddGlobal(
            PhantomSourceValidationReport report,
            PhantomValidationSeverity severity,
            string code,
            PhantomDiagnostic message,
            UnityEngine.Object context)
        {
            report.GlobalIssues.Add(new PhantomValidationIssue
            {
                Code = code,
                Severity = severity,
                Diagnostic = message,
                Context = context,
                SelectionTargets = context != null
                    ? new[] { context }
                    : Array.Empty<UnityEngine.Object>()
            });
        }

        private static void AssignSlotIndices(PhantomSourceValidationReport report)
        {
            for (var index = 0; index < report.Slots.Count; index++)
            {
                foreach (var issue in report.Slots[index].Issues)
                {
                    issue.SlotIndex = index;
                }
            }
        }
    }
}
