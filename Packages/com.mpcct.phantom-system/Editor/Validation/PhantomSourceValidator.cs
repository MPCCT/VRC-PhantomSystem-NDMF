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
        Warning,
        Error
    }

    internal enum PhantomCompatibilityStatus
    {
        NotScanned,
        Compatible,
        Warnings
    }

    internal sealed class PhantomValidationIssue
    {
        public PhantomValidationSeverity Severity;
        public string Message;
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

        public bool HasErrors => Issues.Any(issue => issue.Severity == PhantomValidationSeverity.Error);
        public bool HasWarnings => Issues.Any(issue => issue.Severity == PhantomValidationSeverity.Warning);
    }

    internal sealed class PhantomSourceValidationReport
    {
        public List<PhantomSlotValidationResult> Slots { get; } =
            new List<PhantomSlotValidationResult>();
    }

    internal static class PhantomSourceValidator
    {
        public static PhantomSourceValidationReport Validate(PhantomAuthoring authoring)
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
            AddDuplicateSlotIdIssues(slots, report, authoring);
            AddDuplicateNamespaceIssues(slots, report, authoring);

            for (var index = 0; index < slots.Count; index++)
            {
                ValidateSlot(slots[index], report.Slots[index], baseDescriptor, authoring);
            }

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
                Add(result, PhantomValidationSeverity.Error, "The slot data is missing.", authoring);
                return;
            }

            if (string.IsNullOrWhiteSpace(slot.id))
            {
                Add(
                    result,
                    PhantomValidationSeverity.Error,
                    "Slot Name is empty. Assign a unique name before building.",
                    authoring);
            }

            if (slot.enablePhantomGrabbing)
            {
                var baseAnimator = baseDescriptor != null
                    ? baseDescriptor.GetComponent<Animator>()
                    : null;
                if (baseAnimator == null)
                {
                    Add(
                        result,
                        PhantomValidationSeverity.Error,
                        "Phantom Grabbing requires the base avatar root to have an Animator.",
                        baseDescriptor != null
                            ? (UnityEngine.Object)baseDescriptor
                            : authoring);
                }
                else if (baseAnimator.avatar == null)
                {
                    Add(
                        result,
                        PhantomValidationSeverity.Error,
                        "Phantom Grabbing requires the base Animator to have an Avatar asset.",
                        baseAnimator);
                }
                else if (!baseAnimator.avatar.isValid
                         || !baseAnimator.avatar.isHuman
                         || !baseAnimator.isHuman)
                {
                    Add(
                        result,
                        PhantomValidationSeverity.Error,
                        "Phantom Grabbing requires the base Animator to use a valid Humanoid Avatar.",
                        baseAnimator);
                }
                else if (!HasHumanoidBone(baseAnimator, HumanBodyBones.LeftHand)
                         || !HasHumanoidBone(baseAnimator, HumanBodyBones.RightHand))
                {
                    Add(
                        result,
                        PhantomValidationSeverity.Error,
                        "Phantom Grabbing requires the base avatar to expose both Humanoid hand bones.",
                        baseAnimator);
                }
            }

            var source = slot.phantomAvatar;
            if (source == null)
            {
                Add(result, PhantomValidationSeverity.Error, "No phantom avatar is assigned.", authoring);
                return;
            }

            if (baseDescriptor != null && source == baseDescriptor)
            {
                Add(
                    result,
                    PhantomValidationSeverity.Error,
                    "The slot references the base avatar itself.",
                    source);
                return;
            }

            if (baseDescriptor != null && source.transform.IsChildOf(baseDescriptor.transform))
            {
                Add(
                    result,
                    PhantomValidationSeverity.Error,
                    "The phantom source is inside the base avatar hierarchy.",
                    source);
            }

            var animator = source.GetComponent<Animator>();
            if (animator == null)
            {
                Add(
                    result,
                    PhantomValidationSeverity.Error,
                    "The phantom source root has no Animator.",
                    source);
            }
            else if (animator.avatar == null)
            {
                Add(
                    result,
                    PhantomValidationSeverity.Error,
                    "The phantom source Animator has no Avatar asset.",
                    animator);
            }
            else if (!animator.avatar.isValid || !animator.avatar.isHuman || !animator.isHuman)
            {
                Add(
                    result,
                    PhantomValidationSeverity.Error,
                    "The phantom source Animator is not a valid Humanoid.",
                    animator);
            }
            else if (!HasHumanoidBone(animator, HumanBodyBones.Hips))
            {
                Add(
                    result,
                    PhantomValidationSeverity.Error,
                    "The phantom source Humanoid has no resolvable Hips bone.",
                    animator);
            }

            var nestedSystems = source.GetComponentsInChildren<PhantomAuthoring>(true);
            if (nestedSystems.Length > 0)
            {
                Add(
                    result,
                    PhantomValidationSeverity.Error,
                    $"The phantom source contains {nestedSystems.Length} nested PhantomSystem component(s).",
                    nestedSystems[0]);
            }

            var missingScriptCount = CountMissingScripts(source.gameObject);
            if (missingScriptCount > 0)
            {
                Add(
                    result,
                    PhantomValidationSeverity.Warning,
                    $"The phantom source contains {missingScriptCount} missing script(s).",
                    source);
            }

            ScanComponentCompatibility(source, result);
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
                if (IsVrcSdkComponent(componentType))
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
                    $"Component type '{componentType.FullName}' does not implement INDMFEditorOnly;"
                    + $"phantom prebake compatibility cannot be verified.",
                    components[0],
                    gameObjects);
            }

            result.CompatibilityStatus = result.UnclassifiedComponentCount > 0
                ? PhantomCompatibilityStatus.Warnings
                : PhantomCompatibilityStatus.Compatible;
        }

        private static bool IsVrcSdkComponent(Type componentType)
        {
            var componentNamespace = componentType?.Namespace;
            return string.Equals(componentNamespace, "VRC", StringComparison.Ordinal)
                   || (componentNamespace?.StartsWith(
                       "VRC.",
                       StringComparison.Ordinal) ?? false);
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
                    Name = string.IsNullOrWhiteSpace(slot?.id) ? null : slot.id.Trim()
                })
                .Where(item => item.Name != null)
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);

            foreach (var group in groups)
            {
                foreach (var item in group)
                {
                    Add(
                        report.Slots[item.Index],
                        PhantomValidationSeverity.Error,
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
                    Name = PhantomParameterNames.Activate(slot)
                })
                .GroupBy(item => item.Name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1);

            foreach (var group in groups)
            {
                foreach (var item in group)
                {
                    Add(
                        report.Slots[item.Index],
                        PhantomValidationSeverity.Error,
                        $"The core parameter namespace is duplicated ('{group.Key}').",
                        authoring);
                }
            }
        }

        private static int CountMissingScripts(GameObject root)
        {
            var count = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            }

            return count;
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
            string message,
            UnityEngine.Object context,
            UnityEngine.Object[] selectionTargets = null)
        {
            result.Issues.Add(new PhantomValidationIssue
            {
                Severity = severity,
                Message = message,
                SelectionTargets = selectionTargets
                    ?? (context != null
                        ? new[] { context }
                        : Array.Empty<UnityEngine.Object>())
            });
        }
    }
}
