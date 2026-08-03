using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem.Editor
{
    public sealed partial class PhantomSystemEditor
    {
        private bool DrawParameterSharing(
            int slotIndex,
            SerializedProperty renameProperty,
            SerializedProperty sharedNames)
        {
            EditorGUILayout.Space();
            var expanded = GetSharedParameterFoldout(slotIndex);
            var nextExpanded = EditorGUILayout.Foldout(
                expanded,
                "Same-name Parameter Sharing",
                true);
            if (nextExpanded != expanded)
            {
                SetSharedParameterFoldout(slotIndex, nextExpanded);
            }

            if (!nextExpanded)
            {
                return false;
            }

            var slotAnalysis = parameterAnalysis != null && slotIndex < parameterAnalysis.Slots.Count
                ? parameterAnalysis.Slots[slotIndex]
                : null;
            if (slotAnalysis == null)
            {
                EditorGUILayout.LabelField("Analyzing NDMF parameters...", EditorStyles.miniLabel);
                ScheduleRefresh();
                return false;
            }

            EditorGUILayout.LabelField(
                $"Source {slotAnalysis.SourceParameterCost} bits · "
                + $"shared -{slotAnalysis.SharedParameterSavings} bits · "
                + $"PhantomSystem {slotAnalysis.GeneratedParameterCost} bits · "
                + $"final {slotAnalysis.FinalContributionCost} bits",
                EditorStyles.miniLabel);

            if (!renameProperty.boolValue)
            {
                DrawIndentedHelpBox(
                    "Parameter namespacing is disabled. Compatible same-name parameters already share their original "
                    + "names, so selective sharing rules are not applied.",
                    MessageType.Info);
                return false;
            }

            if (slotAnalysis.Candidates.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "No same-name expression parameters were found on the base avatar.",
                    EditorStyles.miniLabel);
                return DrawStaleRules(sharedNames, new HashSet<string>());
            }

            var changed = false;
            var actionRect = EditorGUI.IndentedRect(
                EditorGUILayout.GetControlRect(
                    false,
                    EditorGUIUtility.singleLineHeight));
            var shareRect = new Rect(
                actionRect.x,
                actionRect.y,
                actionRect.width * 0.5f,
                actionRect.height);
            var clearRect = new Rect(
                shareRect.xMax,
                actionRect.y,
                actionRect.width - shareRect.width,
                actionRect.height);
            if (GUI.Button(shareRect, "Share Compatible", EditorStyles.miniButtonLeft))
            {
                foreach (var candidate in slotAnalysis.Candidates.Where(candidate => candidate.IsCompatible))
                {
                    changed |= SetSharedName(sharedNames, candidate.Name, true);
                }
            }

            if (GUI.Button(clearRect, "Clear", EditorStyles.miniButtonRight))
            {
                if (sharedNames.arraySize > 0)
                {
                    sharedNames.ClearArray();
                    changed = true;
                }
            }

            var candidateNames = new HashSet<string>(
                slotAnalysis.Candidates.Select(candidate => candidate.Name));
            var sourceGroups = slotAnalysis.Candidates
                .GroupBy(candidate => ParameterSourceCategoryKey(candidate.SourceParameter))
                .OrderBy(
                    group => ParameterSourceCategoryLabel(
                        group.First().SourceParameter,
                        slotAnalysis.Slot),
                    System.StringComparer.Ordinal);
            foreach (var sourceGroup in sourceGroups)
            {
                changed |= DrawSharedParameterSourceGroup(
                    slotAnalysis.Slot,
                    sourceGroup.Key,
                    sourceGroup.ToList(),
                    sharedNames);
            }

            changed |= DrawStaleRules(sharedNames, candidateNames);
            return changed;
        }

        private bool DrawSharedParameterSourceGroup(
            PhantomSlot slot,
            string categoryKey,
            IReadOnlyList<PhantomSharedParameterCandidate> candidates,
            SerializedProperty sharedNames)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }

            var sourceParameter = candidates[0].SourceParameter;
            var sourceComponent = sourceParameter?.SourceComponent;
            var canSelect = sourceComponent != null
                && !(sourceComponent is VRCAvatarDescriptor);
            var stateKey = SharedParameterSourceFoldoutKey(slot, categoryKey);
            var expanded = SessionState.GetBool(stateKey, false);
            var headerRect = EditorGUI.IndentedRect(
                EditorGUILayout.GetControlRect(
                    false,
                    EditorGUIUtility.singleLineHeight));
            const float selectButtonWidth = 64f;
            const float columnGap = 4f;
            var foldoutRect = headerRect;
            var selectRect = default(Rect);
            if (canSelect)
            {
                foldoutRect.width = Mathf.Max(
                    0f,
                    headerRect.width - selectButtonWidth - columnGap);
                selectRect = new Rect(
                    foldoutRect.xMax + columnGap,
                    headerRect.y,
                    selectButtonWidth,
                    headerRect.height);
            }

            var nextExpanded = EditorGUI.Foldout(
                foldoutRect,
                expanded,
                new GUIContent(
                    $"{ParameterSourceCategoryLabel(sourceParameter, slot)} ({candidates.Count})",
                    ParameterSourceCategoryTooltip(sourceParameter, slot)),
                true);
            if (canSelect && GUI.Button(selectRect, "Select", EditorStyles.miniButton))
            {
                Selection.activeObject = sourceComponent;
                EditorGUIUtility.PingObject(sourceComponent);
            }

            if (nextExpanded != expanded)
            {
                SessionState.SetBool(stateKey, nextExpanded);
            }

            if (!nextExpanded)
            {
                return false;
            }

            var changed = false;
            using (new EditorGUI.IndentLevelScope())
            {
                foreach (var candidate in candidates)
                {
                    changed |= DrawSharedParameterCandidate(candidate, sharedNames);
                }
            }

            return changed;
        }

        private static bool DrawSharedParameterCandidate(
            PhantomSharedParameterCandidate candidate,
            SerializedProperty sharedNames)
        {
            var changed = false;
            var selected = ContainsString(sharedNames, candidate.Name);
            var syncLabel = candidate.SourceParameter.WantSynced ? "network" : "local";
            var label = $"{candidate.Name}  ({candidate.SourceParameter.ParameterType}, {syncLabel})";
            using (new EditorGUI.DisabledScope(!candidate.IsCompatible))
            {
                var toggleRect = EditorGUI.IndentedRect(
                    EditorGUILayout.GetControlRect(
                        false,
                        EditorGUIUtility.singleLineHeight));
                var next = EditorGUI.ToggleLeft(
                    toggleRect,
                    new GUIContent(
                        label,
                        candidate.IsCompatible
                            ? "Keep this phantom parameter unrenamed so it shares the base parameter."
                            : candidate.IncompatibilityReason),
                    selected);
                if (next != selected)
                {
                    changed |= SetSharedName(sharedNames, candidate.Name, next);
                }
            }

            if (!candidate.IsCompatible)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField(
                        candidate.IncompatibilityReason,
                        EditorStyles.miniLabel);
                }
            }

            return changed;
        }

        private string SharedParameterSourceFoldoutKey(
            PhantomSlot slot,
            string categoryKey)
        {
            var slotId = string.IsNullOrWhiteSpace(slot?.id)
                ? PhantomSlot.DefaultId
                : slot.id.Trim();
            var categoryHash = Hash128.Compute($"{slotId}\n{categoryKey}");
            return $"{slotFoldoutStateKey}.SharedParameterSource.{categoryHash}";
        }

        private static string ParameterSourceCategoryKey(PhantomParameterDefinition parameter)
        {
            var source = parameter?.SourceComponent;
            var plugin = parameter?.SourcePlugin;
            var sourceType = source != null
                ? source.GetType().AssemblyQualifiedName
                : "<unknown-component>";
            var sourceIdentity = source != null
                ? GlobalObjectId.GetGlobalObjectIdSlow(source).ToString()
                : "<unknown-object>";
            var pluginId = plugin != null
                ? plugin.QualifiedName
                : source is VRCAvatarDescriptor
                    ? "vrchat-avatar"
                    : "<unknown-plugin>";
            return $"{pluginId}|{sourceType}|{sourceIdentity}";
        }

        private static string ParameterSourceCategoryLabel(
            PhantomParameterDefinition parameter,
            PhantomSlot slot)
        {
            var source = parameter?.SourceComponent;
            var pluginName = parameter?.SourcePlugin?.DisplayName;
            if (source is VRCAvatarDescriptor)
            {
                return string.IsNullOrWhiteSpace(pluginName)
                    ? "Avatar Parameters · VRChat SDK"
                    : $"Avatar Parameters · {pluginName}";
            }

            var componentName = source != null
                ? ObjectNames.NicifyVariableName(source.GetType().Name)
                : "Unknown Parameter Source";
            var sourcePath = ParameterSourcePath(source, slot);
            return string.IsNullOrWhiteSpace(sourcePath)
                ? componentName
                : $"{componentName} · {sourcePath}";
        }

        private static string ParameterSourceCategoryTooltip(
            PhantomParameterDefinition parameter,
            PhantomSlot slot)
        {
            var source = parameter?.SourceComponent;
            var pluginName = parameter?.SourcePlugin?.DisplayName;
            if (source is VRCAvatarDescriptor)
            {
                return "Parameters declared by the prebaked avatar's VRCExpressionParameters asset.";
            }

            if (source == null)
            {
                return "NDMF did not expose a source component for this resolved parameter.";
            }

            var pluginText = string.IsNullOrWhiteSpace(pluginName)
                ? "an unlabelled NDMF provider"
                : $"the {pluginName} NDMF plugin";
            var sourcePath = ParameterSourcePath(source, slot);
            return $"Provided by {source.GetType().FullName} at '{sourcePath}' through {pluginText}.";
        }

        private static string ParameterSourcePath(Component source, PhantomSlot slot)
        {
            if (source == null)
            {
                return null;
            }

            var avatarRoot = slot?.phantomAvatar != null
                ? slot.phantomAvatar.transform
                : null;
            if (avatarRoot == null
                || (source.transform != avatarRoot && !source.transform.IsChildOf(avatarRoot)))
            {
                return source.gameObject.name;
            }

            if (source.transform == avatarRoot)
            {
                return "Avatar Root";
            }

            return AnimationUtility.CalculateTransformPath(source.transform, avatarRoot);
        }

        private static bool DrawStaleRules(
            SerializedProperty sharedNames,
            HashSet<string> candidateNames)
        {
            var staleNames = new List<string>();
            for (var index = 0; index < sharedNames.arraySize; index++)
            {
                var value = sharedNames.GetArrayElementAtIndex(index).stringValue;
                if (!string.IsNullOrWhiteSpace(value) && !candidateNames.Contains(value))
                {
                    staleNames.Add(value);
                }
            }

            if (staleNames.Count == 0)
            {
                return false;
            }

            DrawIndentedHelpBox(
                "Stored sharing rules are no longer eligible and will fall back to namespacing: "
                + string.Join(", ", staleNames),
                MessageType.Warning);
            var buttonRect = EditorGUI.IndentedRect(
                EditorGUILayout.GetControlRect(
                    false,
                    EditorGUIUtility.singleLineHeight));
            if (!GUI.Button(buttonRect, "Remove Stale Rules", EditorStyles.miniButton))
            {
                return false;
            }

            foreach (var staleName in staleNames)
            {
                SetSharedName(sharedNames, staleName, false);
            }

            return true;
        }

        private static bool ContainsString(SerializedProperty array, string value)
        {
            for (var index = 0; index < array.arraySize; index++)
            {
                if (string.Equals(
                        array.GetArrayElementAtIndex(index).stringValue,
                        value,
                        System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SetSharedName(
            SerializedProperty array,
            string value,
            bool selected)
        {
            for (var index = 0; index < array.arraySize; index++)
            {
                if (!string.Equals(
                        array.GetArrayElementAtIndex(index).stringValue,
                        value,
                        System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (!selected)
                {
                    array.DeleteArrayElementAtIndex(index);
                    return true;
                }

                return false;
            }

            if (!selected)
            {
                return false;
            }

            var newIndex = array.arraySize;
            array.InsertArrayElementAtIndex(newIndex);
            array.GetArrayElementAtIndex(newIndex).stringValue = value;
            return true;
        }
    }
}
