using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>
    /// Excludes phantom animation-driver transforms from the final FX playable mask.
    /// VRChat derives the FX playable mask from the first Animator layer.
    /// </summary>
    internal static class PhantomFxPlayableMaskFinalizer
    {
        public static void Apply(BuildContext context, PhantomBuildState state)
        {
            if (state == null || !state.HasWork)
            {
                return;
            }

            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                state.Report.Error("Cannot finalize the Phantom FX mask because the final avatar descriptor is missing.");
                return;
            }

            var driverTransforms = state.System.Slots
                .Where(slot => slot.AnimationDriverRoot != null
                               && RequiresAnimationDriverIsolation(slot))
                .SelectMany(slot => slot.AnimationDriverRoot.GetComponentsInChildren<Transform>(true))
                .Distinct()
                .ToArray();
            if (driverTransforms.Length == 0)
            {
                return;
            }

            var fxController = FindController(
                descriptor,
                VRCAvatarDescriptor.AnimLayerType.FX);
            if (fxController == null)
            {
                state.Report.Error("Cannot finalize the Phantom FX mask because the final FX controller is missing.");
                return;
            }

            var layers = fxController.layers;
            if (layers.Length == 0)
            {
                state.Report.Error("Cannot finalize the Phantom FX mask because the final FX controller has no layers.", fxController);
                return;
            }

            var mask = CreateMask(
                layers[0].avatarMask,
                driverTransforms,
                context.AvatarRootTransform,
                state.Report);
            if (mask == null)
            {
                return;
            }

            mask.name = "PhantomSystem_FinalFxMask";
            context.AssetSaver.SaveAsset(mask);
            layers[0].avatarMask = mask;
            fxController.layers = layers;
            SetDescriptorMask(descriptor.baseAnimationLayers, mask);
            SetDescriptorMask(descriptor.specialAnimationLayers, mask);
        }

        internal static AvatarMask CreateMask(
            AvatarMask source,
            IEnumerable<Transform> driverTransforms,
            Transform avatarRoot,
            PhantomBuildReport report = null)
        {
            if (avatarRoot == null)
            {
                return null;
            }

            var result = new AvatarMask();
            var entries = new List<TransformMaskEntry>();
            var entryIndices = new Dictionary<string, int>(StringComparer.Ordinal);

            if (source == null)
            {
                for (var part = AvatarMaskBodyPart.Root;
                     part < AvatarMaskBodyPart.LastBodyPart;
                     part++)
                {
                    result.SetHumanoidBodyPartActive(part, false);
                }
                SetEntry(entries, entryIndices, string.Empty, true);
            }
            else
            {
                for (var part = AvatarMaskBodyPart.Root;
                     part < AvatarMaskBodyPart.LastBodyPart;
                     part++)
                {
                    result.SetHumanoidBodyPartActive(
                        part,
                        source.GetHumanoidBodyPartActive(part));
                }

                for (var index = 0; index < source.transformCount; index++)
                {
                    SetEntry(
                        entries,
                        entryIndices,
                        source.GetTransformPath(index) ?? string.Empty,
                        source.GetTransformActive(index));
                }
            }

            foreach (var transform in driverTransforms ?? Enumerable.Empty<Transform>())
            {
                if (transform == null)
                {
                    continue;
                }

                var path = TransformPathUtility.GetRelativePath(transform, avatarRoot);
                if (path == null)
                {
                    report?.InternalError(
                        $"Could not resolve Animation Driver transform '{transform.name}' relative to the final avatar root.",
                        transform);
                    continue;
                }

                SetEntry(entries, entryIndices, path, false);
            }

            result.transformCount = entries.Count;
            for (var index = 0; index < entries.Count; index++)
            {
                result.SetTransformPath(index, entries[index].Path);
                result.SetTransformActive(index, entries[index].Active);
            }
            return result;
        }

        internal static bool IsTransformExcluded(AvatarMask mask, string path)
        {
            if (mask == null || string.IsNullOrEmpty(path))
            {
                return false;
            }

            var bestLength = -1;
            var active = false;
            for (var index = 0; index < mask.transformCount; index++)
            {
                var candidate = mask.GetTransformPath(index) ?? string.Empty;
                if (!IsSameOrParent(candidate, path) || candidate.Length < bestLength)
                {
                    continue;
                }

                bestLength = candidate.Length;
                active = mask.GetTransformActive(index);
            }
            return bestLength >= 0 && !active;
        }

        internal static bool RequiresAnimationDriverIsolation(PhantomSlotBuildState slot)
        {
            return slot != null
                   && slot.SourcePlayableRegistrations != null
                   && (slot.SourcePlayableRegistrations.ContainsKey(
                           VRCAvatarDescriptor.AnimLayerType.Gesture)
                       || slot.SourcePlayableRegistrations.ContainsKey(
                           VRCAvatarDescriptor.AnimLayerType.Action));
        }

        private static void SetEntry(
            IList<TransformMaskEntry> entries,
            IDictionary<string, int> indices,
            string path,
            bool active)
        {
            path ??= string.Empty;
            if (indices.TryGetValue(path, out var index))
            {
                entries[index] = new TransformMaskEntry(path, active);
                return;
            }

            indices[path] = entries.Count;
            entries.Add(new TransformMaskEntry(path, active));
        }

        private static bool IsSameOrParent(string candidate, string path)
        {
            return string.IsNullOrEmpty(candidate)
                   || string.Equals(candidate, path, StringComparison.Ordinal)
                   || path.StartsWith(candidate + "/", StringComparison.Ordinal);
        }

        private static AnimatorController FindController(
            VRCAvatarDescriptor descriptor,
            VRCAvatarDescriptor.AnimLayerType type)
        {
            foreach (var layer in (descriptor.baseAnimationLayers
                         ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                     .Concat(descriptor.specialAnimationLayers
                         ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()))
            {
                if (layer.type == type)
                {
                    return GetBaseController(layer.animatorController);
                }
            }
            return null;
        }

        private static AnimatorController GetBaseController(
            RuntimeAnimatorController runtimeController)
        {
            var current = runtimeController;
            var visited = new HashSet<RuntimeAnimatorController>();
            while (current is AnimatorOverrideController overrideController
                   && visited.Add(current))
            {
                current = overrideController.runtimeAnimatorController;
            }
            return current as AnimatorController;
        }

        private static void SetDescriptorMask(
            VRCAvatarDescriptor.CustomAnimLayer[] layers,
            AvatarMask mask)
        {
            for (var index = 0; index < (layers?.Length ?? 0); index++)
            {
                if (layers[index].type == VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    layers[index].mask = mask;
                }
            }
        }

        private readonly struct TransformMaskEntry
        {
            public readonly string Path;
            public readonly bool Active;

            public TransformMaskEntry(string path, bool active)
            {
                Path = path;
                Active = active;
            }
        }
    }
}
