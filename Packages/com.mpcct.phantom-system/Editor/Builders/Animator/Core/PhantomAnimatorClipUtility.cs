using System;
using UnityEditor;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>Provides shared animation-curve construction helpers.</summary>
    internal static class PhantomAnimatorClipUtility
    {
        public const float FramesPerSecond = 60f;
        public const float FrameDuration = 1f / FramesPerSecond;

        public const string IsActive = "IsActive";
        public const string FreezeToWorld = "FreezeToWorld";
        public const string SolveInLocalSpace = "SolveInLocalSpace";

        public static string SourceWeight(int sourceIndex)
        {
            return $"Sources.source{sourceIndex}.Weight";
        }

        public static void SetGameObjectActive(AnimationClip clip, string path, bool active)
        {
            clip.SetCurve(
                path,
                typeof(GameObject),
                "m_IsActive",
                AnimationCurve.Constant(0f, 0f, active ? 1f : 0f));
        }

        public static void SetFloat(
            AnimationClip clip,
            string path,
            Type componentType,
            string property,
            bool value)
        {
            SetFloat(clip, path, componentType, property, value ? 1f : 0f);
        }

        public static void SetFloat(
            AnimationClip clip,
            string path,
            Type componentType,
            string property,
            float value)
        {
            SetFloat(
                clip,
                path,
                componentType,
                property,
                AnimationCurve.Constant(0f, 0f, value));
        }

        public static void SetFloat(
            AnimationClip clip,
            string path,
            Type componentType,
            string property,
            AnimationCurve curve)
        {
            clip.SetCurve(path, componentType, property, curve);
        }

        public static AnimationCurve Constant(float duration, float value)
        {
            return AnimationCurve.Constant(0f, duration, value);
        }

        public static AnimationCurve Stepped(params Keyframe[] keys)
        {
            var curve = new AnimationCurve(keys);
            for (var index = 0; index < keys.Length - 1; index++)
            {
                AnimationUtility.SetKeyRightTangentMode(
                    curve,
                    index,
                    AnimationUtility.TangentMode.Constant);
            }

            return curve;
        }
    }
}
