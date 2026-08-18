using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    internal enum PhantomAnimationBindingKind
    {
        ResolvedHumanoid,
        RootTransform,
        AnimatorParameter,
        UnsupportedAnimator,
        NonHumanoid
    }

    internal static class PhantomAnimationBindingClassifier
    {
        public static PhantomAnimationBindingKind Classify(
            EditorCurveBinding binding,
            ISet<string> animatorParameterNames = null)
        {
            if (binding.type == typeof(Animator))
            {
                if (PhantomHumanoidBindingUtility.IsRootMotionBinding(binding))
                {
                    return PhantomAnimationBindingKind.RootTransform;
                }

                if (PhantomHumanoidBindingUtility.TryResolveHumanoidBinding(
                        binding,
                        out _,
                        out _))
                {
                    return PhantomAnimationBindingKind.ResolvedHumanoid;
                }

                return string.IsNullOrEmpty(binding.path)
                       && animatorParameterNames != null
                       && animatorParameterNames.Contains(binding.propertyName)
                    ? PhantomAnimationBindingKind.AnimatorParameter
                    : PhantomAnimationBindingKind.UnsupportedAnimator;
            }

            if (PhantomHumanoidBindingUtility.IsRootPositionOrRotationBinding(binding)
                || PhantomHumanoidBindingUtility.IsRootScaleBinding(binding))
            {
                return PhantomAnimationBindingKind.RootTransform;
            }

            return PhantomAnimationBindingKind.NonHumanoid;
        }
    }

    internal sealed class PhantomHumanoidClipAnalysis
    {
        internal IReadOnlyList<EditorCurveBinding> RelevantBindings { get; }
        internal HashSet<HumanBodyBones> AffectedBones { get; }
        internal HashSet<HumanBodyBones> ForcePositionBones { get; }
        internal HashSet<HumanBodyBones> ExplicitlyAnimatedBones { get; }
        internal IReadOnlyList<EditorCurveBinding> SkippedAnimatorBindings { get; }
        internal IReadOnlyList<EditorCurveBinding> IgnoredRootScaleBindings { get; }
        internal int ResolvedHumanoidBindingCount { get; }
        internal bool HasRootMotion { get; }

        internal PhantomHumanoidClipAnalysis(
            IReadOnlyList<EditorCurveBinding> relevantBindings,
            HashSet<HumanBodyBones> affectedBones,
            HashSet<HumanBodyBones> forcePositionBones,
            HashSet<HumanBodyBones> explicitlyAnimatedBones,
            IReadOnlyList<EditorCurveBinding> skippedAnimatorBindings,
            IReadOnlyList<EditorCurveBinding> ignoredRootScaleBindings,
            int resolvedHumanoidBindingCount,
            bool hasRootMotion)
        {
            RelevantBindings = relevantBindings;
            AffectedBones = affectedBones;
            ForcePositionBones = forcePositionBones;
            ExplicitlyAnimatedBones = explicitlyAnimatedBones;
            SkippedAnimatorBindings = skippedAnimatorBindings;
            IgnoredRootScaleBindings = ignoredRootScaleBindings;
            ResolvedHumanoidBindingCount = resolvedHumanoidBindingCount;
            HasRootMotion = hasRootMotion;
        }
    }

    internal static class PhantomHumanoidClipAnalyzer
    {
        internal static PhantomHumanoidClipAnalysis Analyze(
            AnimationClip source,
            PhantomHumanoidClipBakeOptions options,
            bool effectiveMirror)
        {
            var allBindings = AnimationUtility.GetCurveBindings(source);
            var animatorBindings = allBindings
                .Where(binding => binding.type == typeof(Animator))
                .ToArray();
            var rootTransformBindings = allBindings
                .Where(binding => binding.type == typeof(Transform)
                                  && string.IsNullOrEmpty(binding.path))
                .ToArray();
            var hasRootMotion = animatorBindings.Any(
                                    PhantomHumanoidBindingUtility.IsRootMotionBinding)
                                || rootTransformBindings.Any(
                                    PhantomHumanoidBindingUtility.IsRootPositionOrRotationBinding);
            var ignoredRootScaleBindings = options.LocalizeRootMotionToHips
                ? rootTransformBindings
                    .Where(PhantomHumanoidBindingUtility.IsRootScaleBinding)
                    .ToArray()
                : Array.Empty<EditorCurveBinding>();
            var relevantBindings = animatorBindings
                .Where(binding => PhantomAnimationBindingClassifier.Classify(
                    binding,
                    options.AnimatorParameterNames) != PhantomAnimationBindingKind.AnimatorParameter)
                .Concat(rootTransformBindings.Where(binding =>
                    PhantomHumanoidBindingUtility.IsRootPositionOrRotationBinding(binding)
                    || PhantomHumanoidBindingUtility.IsRootScaleBinding(binding)))
                .ToArray();

            var affectedBones = new HashSet<HumanBodyBones>();
            var forcePositionBones = new HashSet<HumanBodyBones>();
            var skippedAnimatorBindings = new List<EditorCurveBinding>();
            var resolvedHumanoidBindingCount = 0;
            foreach (var binding in animatorBindings)
            {
                var kind = PhantomAnimationBindingClassifier.Classify(
                    binding,
                    options.AnimatorParameterNames);
                if (kind == PhantomAnimationBindingKind.UnsupportedAnimator)
                {
                    skippedAnimatorBindings.Add(binding);
                    continue;
                }

                if (kind == PhantomAnimationBindingKind.ResolvedHumanoid
                    && PhantomHumanoidBindingUtility.TryResolveHumanoidBinding(
                        binding,
                        out var bone,
                        out var forcePosition))
                {
                    resolvedHumanoidBindingCount++;
                    if (effectiveMirror)
                    {
                        bone = PhantomHumanoidBindingUtility.MirrorHumanoidBone(bone);
                    }
                    affectedBones.Add(bone);
                    if (forcePosition)
                    {
                        forcePositionBones.Add(bone);
                    }
                }
            }

            if (options.LocalizeRootMotionToHips && hasRootMotion)
            {
                affectedBones.Add(HumanBodyBones.Hips);
                forcePositionBones.Add(HumanBodyBones.Hips);
            }

            return new PhantomHumanoidClipAnalysis(
                relevantBindings,
                affectedBones,
                forcePositionBones,
                new HashSet<HumanBodyBones>(affectedBones),
                skippedAnimatorBindings,
                ignoredRootScaleBindings,
                resolvedHumanoidBindingCount,
                hasRootMotion);
        }

        internal static IReadOnlyList<float> CollectSourceCandidateTimes(
            AnimationClip source,
            IReadOnlyList<EditorCurveBinding> relevantBindings)
        {
            var times = new SortedSet<float> { 0f, source.length };
            foreach (var binding in relevantBindings)
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null)
                {
                    continue;
                }

                foreach (var key in curve.keys)
                {
                    times.Add(Mathf.Clamp(key.time, 0f, source.length));
                }
            }
            return times.ToArray();
        }

        internal static IReadOnlyDictionary<HumanBodyBones, List<PhantomTimeInterval>>
            CollectConstantIntervals(
                AnimationClip source,
                IReadOnlyList<EditorCurveBinding> relevantBindings,
                bool mirrorBindings)
        {
            var intervals = new Dictionary<HumanBodyBones, List<PhantomTimeInterval>>();
            foreach (var binding in relevantBindings)
            {
                HumanBodyBones bone;
                if (binding.type == typeof(Animator))
                {
                    if (!PhantomHumanoidBindingUtility.TryResolveHumanoidBinding(
                            binding,
                            out bone,
                            out _))
                    {
                        continue;
                    }
                }
                else if (PhantomHumanoidBindingUtility.IsRootPositionOrRotationBinding(binding))
                {
                    bone = HumanBodyBones.Hips;
                }
                else
                {
                    continue;
                }

                if (mirrorBindings)
                {
                    bone = PhantomHumanoidBindingUtility.MirrorHumanoidBone(bone);
                }

                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null)
                {
                    continue;
                }

                for (var index = 0; index + 1 < curve.length; index++)
                {
                    if (AnimationUtility.GetKeyRightTangentMode(curve, index)
                        != AnimationUtility.TangentMode.Constant)
                    {
                        continue;
                    }

                    if (!intervals.TryGetValue(bone, out var boneIntervals))
                    {
                        boneIntervals = new List<PhantomTimeInterval>();
                        intervals.Add(bone, boneIntervals);
                    }
                    boneIntervals.Add(new PhantomTimeInterval(
                        Mathf.Clamp(curve.keys[index].time, 0f, source.length),
                        Mathf.Clamp(curve.keys[index + 1].time, 0f, source.length)));
                }
            }
            return intervals;
        }
    }

    internal static class PhantomHumanoidBindingUtility
    {
        private static readonly Dictionary<string, int> MuscleIndices =
            HumanTrait.MuscleName
                .Select((name, index) => new KeyValuePair<string, int>(name, index))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        internal static bool ResolveEffectiveMirror(bool clipMirror, bool inheritedMirror)
        {
            return clipMirror ^ inheritedMirror;
        }

        internal static HumanBodyBones MirrorHumanoidBone(HumanBodyBones bone)
        {
            if (bone < 0 || bone >= HumanBodyBones.LastBone)
            {
                return bone;
            }

            var name = bone.ToString();
            string mirroredName;
            if (name.StartsWith("Left", StringComparison.Ordinal))
            {
                mirroredName = "Right" + name.Substring("Left".Length);
            }
            else if (name.StartsWith("Right", StringComparison.Ordinal))
            {
                mirroredName = "Left" + name.Substring("Right".Length);
            }
            else
            {
                return bone;
            }

            return Enum.TryParse(mirroredName, false, out HumanBodyBones mirrored)
                   && mirrored >= 0
                   && mirrored < HumanBodyBones.LastBone
                   && string.Equals(mirrored.ToString(), mirroredName, StringComparison.Ordinal)
                ? mirrored
                : bone;
        }

        internal static bool TryResolveHumanoidBinding(
            EditorCurveBinding binding,
            out HumanBodyBones bone,
            out bool forcePosition)
        {
            bone = HumanBodyBones.LastBone;
            forcePosition = false;
            if (binding.type != typeof(Animator))
            {
                return false;
            }

            if (TryResolveFingerBinding(binding.propertyName, out bone))
            {
                return true;
            }

            if (MuscleIndices.TryGetValue(binding.propertyName, out var muscleIndex))
            {
                var boneIndex = HumanTrait.BoneFromMuscle(muscleIndex);
                if (boneIndex >= 0 && boneIndex < (int)HumanBodyBones.LastBone)
                {
                    bone = (HumanBodyBones)boneIndex;
                    return true;
                }
            }

            if (TryResolveTranslationDofBinding(binding.propertyName, out bone))
            {
                forcePosition = true;
                return true;
            }

            if (StartsWithAny(binding.propertyName, "RootT.", "BodyT."))
            {
                bone = HumanBodyBones.Hips;
                forcePosition = true;
                return true;
            }
            if (StartsWithAny(binding.propertyName, "RootQ.", "BodyQ."))
            {
                bone = HumanBodyBones.Hips;
                return true;
            }
            if (StartsWithAny(binding.propertyName, "LeftFootT."))
            {
                bone = HumanBodyBones.LeftFoot;
                forcePosition = true;
                return true;
            }
            if (StartsWithAny(binding.propertyName, "LeftFootQ."))
            {
                bone = HumanBodyBones.LeftFoot;
                return true;
            }
            if (StartsWithAny(binding.propertyName, "RightFootT."))
            {
                bone = HumanBodyBones.RightFoot;
                forcePosition = true;
                return true;
            }
            if (StartsWithAny(binding.propertyName, "RightFootQ."))
            {
                bone = HumanBodyBones.RightFoot;
                return true;
            }
            if (StartsWithAny(binding.propertyName, "LeftHandT."))
            {
                bone = HumanBodyBones.LeftHand;
                forcePosition = true;
                return true;
            }
            if (StartsWithAny(binding.propertyName, "LeftHandQ."))
            {
                bone = HumanBodyBones.LeftHand;
                return true;
            }
            if (StartsWithAny(binding.propertyName, "RightHandT."))
            {
                bone = HumanBodyBones.RightHand;
                forcePosition = true;
                return true;
            }
            if (StartsWithAny(binding.propertyName, "RightHandQ."))
            {
                bone = HumanBodyBones.RightHand;
                return true;
            }
            return false;
        }

        internal static bool IsRootMotionBinding(EditorCurveBinding binding)
        {
            return binding.type == typeof(Animator)
                   && StartsWithAny(binding.propertyName, "RootT.", "RootQ.");
        }

        internal static bool IsRootPositionOrRotationBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Transform) || !string.IsNullOrEmpty(binding.path))
            {
                return false;
            }

            return StartsWithAny(
                binding.propertyName,
                "m_LocalPosition.",
                "m_LocalRotation.",
                "localEulerAnglesRaw.",
                "localEulerAnglesBaked.");
        }

        internal static bool IsRootScaleBinding(EditorCurveBinding binding)
        {
            return binding.type == typeof(Transform)
                   && string.IsNullOrEmpty(binding.path)
                   && StartsWithAny(binding.propertyName, "m_LocalScale.");
        }

        private static bool TryResolveTranslationDofBinding(
            string propertyName,
            out HumanBodyBones bone)
        {
            bone = HumanBodyBones.LastBone;
            if (string.IsNullOrEmpty(propertyName) || propertyName.Length < 3)
            {
                return false;
            }

            var separator = propertyName.LastIndexOf('.');
            if (separator <= 0 || separator != propertyName.Length - 2)
            {
                return false;
            }
            var component = propertyName[propertyName.Length - 1];
            if (component != 'x' && component != 'y' && component != 'z')
            {
                return false;
            }

            const string suffix = "TDOF";
            var serializedBoneName = propertyName.Substring(0, separator);
            if (!serializedBoneName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            var boneName = serializedBoneName.Substring(
                0,
                serializedBoneName.Length - suffix.Length);
            if (!Enum.TryParse(boneName, false, out bone)
                || bone == HumanBodyBones.LastBone
                || !Enum.IsDefined(typeof(HumanBodyBones), bone)
                || !string.Equals(bone.ToString(), boneName, StringComparison.Ordinal))
            {
                bone = HumanBodyBones.LastBone;
                return false;
            }
            return true;
        }

        private static bool TryResolveFingerBinding(
            string propertyName,
            out HumanBodyBones bone)
        {
            bone = HumanBodyBones.LastBone;
            if (string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            var parts = propertyName.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            var isLeft = string.Equals(parts[0], "LeftHand", StringComparison.Ordinal);
            var isRight = string.Equals(parts[0], "RightHand", StringComparison.Ordinal);
            if (!isLeft && !isRight)
            {
                return false;
            }

            var segment = parts[2];
            var jointIndex = string.Equals(segment, "1 Stretched", StringComparison.Ordinal)
                || string.Equals(segment, "Spread", StringComparison.Ordinal)
                    ? 0
                    : string.Equals(segment, "2 Stretched", StringComparison.Ordinal)
                        ? 1
                        : string.Equals(segment, "3 Stretched", StringComparison.Ordinal)
                            ? 2
                            : -1;
            return jointIndex >= 0
                   && TryResolveFingerBone(isLeft, parts[1], jointIndex, out bone);
        }

        private static bool TryResolveFingerBone(
            bool isLeft,
            string finger,
            int jointIndex,
            out HumanBodyBones bone)
        {
            bone = HumanBodyBones.LastBone;
            switch (finger)
            {
                case "Thumb":
                    bone = SelectFingerBone(
                        isLeft,
                        jointIndex,
                        HumanBodyBones.LeftThumbProximal,
                        HumanBodyBones.LeftThumbIntermediate,
                        HumanBodyBones.LeftThumbDistal,
                        HumanBodyBones.RightThumbProximal,
                        HumanBodyBones.RightThumbIntermediate,
                        HumanBodyBones.RightThumbDistal);
                    return true;
                case "Index":
                    bone = SelectFingerBone(
                        isLeft,
                        jointIndex,
                        HumanBodyBones.LeftIndexProximal,
                        HumanBodyBones.LeftIndexIntermediate,
                        HumanBodyBones.LeftIndexDistal,
                        HumanBodyBones.RightIndexProximal,
                        HumanBodyBones.RightIndexIntermediate,
                        HumanBodyBones.RightIndexDistal);
                    return true;
                case "Middle":
                    bone = SelectFingerBone(
                        isLeft,
                        jointIndex,
                        HumanBodyBones.LeftMiddleProximal,
                        HumanBodyBones.LeftMiddleIntermediate,
                        HumanBodyBones.LeftMiddleDistal,
                        HumanBodyBones.RightMiddleProximal,
                        HumanBodyBones.RightMiddleIntermediate,
                        HumanBodyBones.RightMiddleDistal);
                    return true;
                case "Ring":
                    bone = SelectFingerBone(
                        isLeft,
                        jointIndex,
                        HumanBodyBones.LeftRingProximal,
                        HumanBodyBones.LeftRingIntermediate,
                        HumanBodyBones.LeftRingDistal,
                        HumanBodyBones.RightRingProximal,
                        HumanBodyBones.RightRingIntermediate,
                        HumanBodyBones.RightRingDistal);
                    return true;
                case "Little":
                    bone = SelectFingerBone(
                        isLeft,
                        jointIndex,
                        HumanBodyBones.LeftLittleProximal,
                        HumanBodyBones.LeftLittleIntermediate,
                        HumanBodyBones.LeftLittleDistal,
                        HumanBodyBones.RightLittleProximal,
                        HumanBodyBones.RightLittleIntermediate,
                        HumanBodyBones.RightLittleDistal);
                    return true;
                default:
                    return false;
            }
        }

        private static HumanBodyBones SelectFingerBone(
            bool isLeft,
            int jointIndex,
            HumanBodyBones left0,
            HumanBodyBones left1,
            HumanBodyBones left2,
            HumanBodyBones right0,
            HumanBodyBones right1,
            HumanBodyBones right2)
        {
            if (isLeft)
            {
                return jointIndex == 0 ? left0 : jointIndex == 1 ? left1 : left2;
            }
            return jointIndex == 0 ? right0 : jointIndex == 1 ? right1 : right2;
        }

        private static bool StartsWithAny(string value, params string[] prefixes)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (var prefix in prefixes)
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
