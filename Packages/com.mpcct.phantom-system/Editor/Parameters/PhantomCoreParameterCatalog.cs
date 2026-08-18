using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Authoritative definition of every generated PhantomSystem parameter.</summary>
    internal static class PhantomCoreParameterCatalog
    {
        internal sealed class Entry
        {
            public PhantomParameterDefinition Parameter { get; }
            public AnimatorControllerParameterType ControllerParameterType { get; }

            public Entry(
                PhantomParameterDefinition parameter,
                AnimatorControllerParameterType controllerParameterType)
            {
                Parameter = parameter;
                ControllerParameterType = controllerParameterType;
            }
        }

        public static IReadOnlyList<Entry> ForSlot(PhantomSlot slot)
        {
            if (slot == null)
            {
                return Array.Empty<Entry>();
            }

            var entries = new List<Entry>
            {
                Synced(PhantomParameterNames.Activate(slot), AnimatorControllerParameterType.Bool, 0f),
                Synced(PhantomParameterNames.Freeze(slot), AnimatorControllerParameterType.Bool, 0f),
                Synced(PhantomParameterNames.PositionLock(slot), AnimatorControllerParameterType.Bool, 1f)
            };

            if (slot.enableScaleControl)
            {
                entries.Add(Synced(
                    PhantomParameterNames.Scale(slot),
                    AnimatorControllerParameterType.Float,
                    ScaleControlAnimatorModule.DefaultScaleParameter));
                entries.Add(Synced(
                    PhantomParameterNames.Mirror(slot),
                    AnimatorControllerParameterType.Bool,
                    0f,
                    AnimatorControllerParameterType.Float));
                entries.Add(AnimatorOnly(
                    PhantomParameterNames.ScaleDirectWeight(slot),
                    AnimatorControllerParameterType.Float,
                    1f));
                entries.Add(Local(
                    PhantomParameterNames.ScaleReset(slot),
                    AnimatorControllerParameterType.Bool,
                    0f));
            }

            if (slot.enablePhantomGrabbing)
            {
                entries.Add(Synced(
                    PhantomParameterNames.PhantomGrabbingShowBones(slot),
                    AnimatorControllerParameterType.Bool,
                    0f));
                entries.Add(AnimatorOnly(
                    PhantomParameterNames.PhantomGrabbingContactLeft(slot),
                    AnimatorControllerParameterType.Bool,
                    0f));
                entries.Add(AnimatorOnly(
                    PhantomParameterNames.PhantomGrabbingContactRight(slot),
                    AnimatorControllerParameterType.Bool,
                    0f));
            }

            if (slot.enablePhantomView)
            {
                entries.Add(Local(
                    PhantomParameterNames.PhantomViewEnabled(slot),
                    AnimatorControllerParameterType.Bool,
                    0f));
                entries.Add(Local(
                    PhantomParameterNames.PhantomViewStereoStrength(slot),
                    AnimatorControllerParameterType.Float,
                    PhantomViewAnimatorModule.DefaultStereoStrengthParameter));
                entries.Add(Local(
                    PhantomParameterNames.PhantomViewMaskSize(slot),
                    AnimatorControllerParameterType.Float,
                    PhantomViewAnimatorModule.DefaultMaskSizeParameter));
                entries.Add(AnimatorOnly(
                    PhantomParameterNames.PhantomViewDirectWeight(slot),
                    AnimatorControllerParameterType.Float,
                    1f));
            }

            if (slot.tryConvertAnimatorTrackingControl && !slot.removeSourceControls)
            {
                foreach (var name in PhantomTrackingControlGroups.Parameters(slot))
                {
                    entries.Add(AnimatorOnly(name, AnimatorControllerParameterType.Float, 1f));
                }
                entries.Add(AnimatorOnly(
                    PhantomParameterNames.TrackingDirectWeight(slot),
                    AnimatorControllerParameterType.Float,
                    1f));
            }

            return entries;
        }

        public static Entry Require(PhantomSlot slot, string name)
        {
            var entry = ForSlot(slot).FirstOrDefault(candidate =>
                string.Equals(candidate.Parameter.Name, name, StringComparison.Ordinal));
            if (entry == null)
            {
                throw new InvalidOperationException(
                    $"Core parameter '{name}' is not enabled for the current PhantomSystem slot.");
            }

            return entry;
        }

        private static Entry Synced(
            string name,
            AnimatorControllerParameterType exposedType,
            float defaultValue,
            AnimatorControllerParameterType? controllerType = null)
        {
            return Create(name, exposedType, controllerType ?? exposedType, true, false, defaultValue);
        }

        private static Entry Local(
            string name,
            AnimatorControllerParameterType type,
            float defaultValue)
        {
            return Create(name, type, type, false, false, defaultValue);
        }

        private static Entry AnimatorOnly(
            string name,
            AnimatorControllerParameterType type,
            float defaultValue)
        {
            return Create(name, type, type, false, true, defaultValue);
        }

        private static Entry Create(
            string name,
            AnimatorControllerParameterType exposedType,
            AnimatorControllerParameterType controllerType,
            bool synced,
            bool animatorOnly,
            float defaultValue)
        {
            return new Entry(
                new PhantomParameterDefinition
                {
                    Name = name,
                    ParameterType = exposedType,
                    WantSynced = synced,
                    IsAnimatorOnly = animatorOnly,
                    IsHidden = false,
                    DefaultValue = defaultValue,
                    Saved = animatorOnly ? (bool?)null : false
                },
                controllerType);
        }
    }
}
