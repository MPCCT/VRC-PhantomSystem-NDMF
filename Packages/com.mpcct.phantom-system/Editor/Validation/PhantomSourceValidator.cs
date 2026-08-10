using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
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
        public string Message;
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
            return ValidateAuthoring(authoring);
        }

        public static PhantomSourceValidationReport ValidateAuthoring(PhantomAuthoring authoring)
        {
            var report = new PhantomSourceValidationReport();
            if (authoring == null)
            {
                return report;
            }

            var slots = authoring.slots ?? new List<PhantomSlot>();
            for (var index = 0; index < slots.Count; index++)
            {
                report.Slots.Add(new PhantomSlotValidationResult());
            }

            var baseDescriptor = FindAvatarDescriptor(authoring.transform);
            ValidateBaseAvatar(baseDescriptor, authoring, slots, report);
            AddDuplicateSlotIdIssues(slots, report, authoring);
            AddDuplicateHierarchyNameIssues(slots, report, authoring);
            AddDuplicateNamespaceIssues(slots, report, authoring);

            for (var index = 0; index < slots.Count; index++)
            {
                ValidateSlot(slots[index], report.Slots[index], baseDescriptor, authoring);
            }

            var parameterAnalysis = PhantomParameterAnalysis.Analyze(authoring);
            foreach (var error in parameterAnalysis.ResolutionErrors.Where(error =>
                         error == null
                         || error.IndexOf("use the same core parameter prefix", StringComparison.Ordinal) < 0))
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS200", error, authoring);
            }
            for (var index = 0; index < report.Slots.Count && index < parameterAnalysis.Slots.Count; index++)
            {
                foreach (var rename in parameterAnalysis.Slots[index].AutomaticRenames)
                {
                    Add(
                        report.Slots[index],
                        PhantomValidationSeverity.Warning,
                        "PHS201",
                        $"Parameter '{rename.OriginalName}' will be renamed to '{rename.FinalName}' "
                        + $"to avoid an incompatible collision ({rename.Reason}).",
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
                    $"PhantomSystem on '{system.AuthoringComponent.name}' has no slots.",
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
                        $"Slot '{slot.SlotId}' has no PhantomSystem prebake result. Manual Bake commands do not "
                        + "run the VRChat preprocess hook; use 'Bake Avatar with PhantomSystem' or VRChat SDK Build/Upload.",
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
                        $"Prebaked phantom for Slot '{slot.SlotId}' has no VRCAvatarDescriptor.",
                        slot.PrebakedRoot);
                }

                var animator = slot.PrebakedRoot.GetComponent<Animator>();
                if (animator == null || !animator.isHuman)
                {
                    Add(
                        result,
                        PhantomValidationSeverity.InternalError,
                        "PHS303",
                        $"Prebaked phantom for Slot '{slot.SlotId}' has no Humanoid Animator.",
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
            PhantomAuthoring authoring)
        {
            if (slot == null)
            {
                Add(result, PhantomValidationSeverity.ConfigurationError, "PHS001", "The slot data is missing.", authoring);
                return;
            }

            if (string.IsNullOrWhiteSpace(slot.id))
            {
                Add(
                    result,
                    PhantomValidationSeverity.Info,
                    "PHS002",
                    $"Slot Name is empty; it will be resolved as '{PhantomSlot.DefaultId}'.",
                    authoring);
            }

            var source = slot.phantomAvatar;
            if (source == null)
            {
                Add(result, PhantomValidationSeverity.ConfigurationError, "PHS010", "No phantom avatar is assigned.", authoring);
                return;
            }

            if (baseDescriptor != null && source == baseDescriptor)
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS011",
                    "The slot references the base avatar itself.",
                    source);
                return;
            }

            if (baseDescriptor != null && source.transform.IsChildOf(baseDescriptor.transform))
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS012",
                    "The phantom source is inside the base avatar hierarchy.",
                    source);
            }

            var animator = source.GetComponent<Animator>();
            if (animator == null)
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS013",
                    "The phantom source root has no Animator.",
                    source);
            }
            else if (animator.avatar == null)
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS014",
                    "The phantom source Animator has no Avatar asset.",
                    animator);
            }
            else if (!animator.avatar.isValid || !animator.avatar.isHuman || !animator.isHuman)
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS015",
                    "The phantom source Animator is not a valid Humanoid.",
                    animator);
            }
            else if (!HasHumanoidBone(animator, HumanBodyBones.Hips))
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS016",
                    "The phantom source Humanoid has no resolvable Hips bone.",
                    animator);
            }
            else if (slot.enablePhantomView && !HasHumanoidBone(animator, HumanBodyBones.Head))
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS017",
                    "Phantom View requires the phantom source Humanoid to expose a Head bone.",
                    animator);
            }

            var nestedSystems = source.GetComponentsInChildren<PhantomAuthoring>(true);
            if (nestedSystems.Length > 0)
            {
                Add(
                    result,
                    PhantomValidationSeverity.ConfigurationError,
                    "PHS018",
                    $"The phantom source contains {nestedSystems.Length} nested PhantomSystem component(s).",
                    nestedSystems[0]);
            }

            var missingScriptObjects = FindMissingScriptGameObjects(
                source.gameObject,
                out var missingScriptCount);
            if (missingScriptCount > 0)
            {
                Add(
                    result,
                    PhantomValidationSeverity.Warning,
                    "PHS020",
                    $"The phantom source contains {missingScriptCount} missing script(s) on "
                    + $"{missingScriptObjects.Length} GameObject(s).",
                    source,
                    missingScriptObjects);
            }

            ScanComponentCompatibility(source, result);
        }

        private static void ValidateBaseAvatar(
            VRCAvatarDescriptor baseDescriptor,
            PhantomAuthoring authoring,
            IReadOnlyList<PhantomSlot> slots,
            PhantomSourceValidationReport report)
        {
            var context = baseDescriptor != null ? (UnityEngine.Object)baseDescriptor : authoring;
            var animator = baseDescriptor != null ? baseDescriptor.GetComponent<Animator>() : null;
            if (baseDescriptor == null)
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS100",
                    "PhantomSystem must be placed under a VRCAvatarDescriptor.", authoring);
                return;
            }

            if (animator == null)
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS101",
                    "The base avatar root has no Animator.", context);
                return;
            }

            if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman || !animator.isHuman)
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS102",
                    "The base avatar Animator must use a valid Humanoid Avatar.", animator);
                return;
            }

            if (!HasHumanoidBone(animator, HumanBodyBones.Hips))
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS103",
                    "The base avatar Humanoid has no resolvable Hips bone.", animator);
            }

            if (slots.Any(slot => slot != null && slot.enablePhantomView)
                && !HasHumanoidBone(animator, HumanBodyBones.Head))
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS104",
                    "Phantom View requires the base avatar Humanoid to expose a Head bone.", animator);
            }

            if (slots.Any(slot => slot != null && slot.enablePhantomGrabbing)
                && (!HasHumanoidBone(animator, HumanBodyBones.LeftHand)
                    || !HasHumanoidBone(animator, HumanBodyBones.RightHand)))
            {
                AddGlobal(report, PhantomValidationSeverity.ConfigurationError, "PHS105",
                    "Phantom Grabbing requires both Humanoid hand bones on the base avatar.", animator);
            }
        }

        private static void ScanComponentCompatibility(
            VRCAvatarDescriptor source,
            PhantomSlotValidationResult result)
        {
            var unclassifiedComponents = new List<MonoBehaviour>();
            foreach (var component in source.GetComponentsInChildren<MonoBehaviour>(true))
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
                    $"Component type '{componentType.FullName}' does not implement INDMFEditorOnly;"
                    + $"phantom prebake compatibility cannot be verified.",
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
                        $"Slot Name '{group.Key}' is duplicated.",
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
                        $"The core parameter namespace is duplicated ('{group.Key}').",
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
                        $"Slot hierarchy name '{group.Key}' is duplicated after invalid path characters are normalized.",
                        authoring);
                }
            }
        }

        private static UnityEngine.Object[] FindMissingScriptGameObjects(
            GameObject root,
            out int missingScriptCount)
        {
            missingScriptCount = 0;
            var gameObjects = new List<UnityEngine.Object>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
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

        private static VRCAvatarDescriptor FindAvatarDescriptor(Transform start)
        {
            for (var current = start; current != null; current = current.parent)
            {
                var descriptor = current.GetComponent<VRCAvatarDescriptor>();
                if (descriptor != null)
                {
                    return descriptor;
                }
            }

            return null;
        }

        private static void Add(
            PhantomSlotValidationResult result,
            PhantomValidationSeverity severity,
            string code,
            string message,
            UnityEngine.Object context,
            UnityEngine.Object[] selectionTargets = null)
        {
            result.Issues.Add(new PhantomValidationIssue
            {
                Code = code,
                Severity = severity,
                Message = message,
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
            string message,
            UnityEngine.Object context)
        {
            report.GlobalIssues.Add(new PhantomValidationIssue
            {
                Code = code,
                Severity = severity,
                Message = message,
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
