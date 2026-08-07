using UnityEditor;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    [FilePath("ProjectSettings/PhantomSystemSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class PhantomSystemProjectSettings
        : ScriptableSingleton<PhantomSystemProjectSettings>
    {
        internal const int DefaultViewTextureSize = 1024;
        internal const float DefaultMaximumAdaptiveSampleRate = 30f;
        internal const float DefaultPositionErrorTolerance = 0.0005f;
        internal const float DefaultRotationErrorToleranceDegrees = 0.25f;

        internal static readonly int[] ViewTextureSizes = { 256, 512, 1024, 2048, 4096 };

        [SerializeField] private int phantomViewTextureSize = DefaultViewTextureSize;
        [SerializeField] private float maximumAdaptiveSampleRate = DefaultMaximumAdaptiveSampleRate;
        [SerializeField] private float positionErrorTolerance = DefaultPositionErrorTolerance;
        [SerializeField] private float rotationErrorToleranceDegrees = DefaultRotationErrorToleranceDegrees;

        internal int PhantomViewTextureSize
        {
            get => NormalizeTextureSize(phantomViewTextureSize);
            set => phantomViewTextureSize = NormalizeTextureSize(value);
        }

        internal float MaximumAdaptiveSampleRate
        {
            get => NormalizeFloat(maximumAdaptiveSampleRate, 1f, 120f, DefaultMaximumAdaptiveSampleRate);
            set => maximumAdaptiveSampleRate = NormalizeFloat(value, 1f, 120f, DefaultMaximumAdaptiveSampleRate);
        }

        internal float PositionErrorTolerance
        {
            get => NormalizeFloat(positionErrorTolerance, 0.000001f, 0.1f, DefaultPositionErrorTolerance);
            set => positionErrorTolerance = NormalizeFloat(value, 0.000001f, 0.1f, DefaultPositionErrorTolerance);
        }

        internal float RotationErrorToleranceDegrees
        {
            get => NormalizeFloat(rotationErrorToleranceDegrees, 0.001f, 10f, DefaultRotationErrorToleranceDegrees);
            set => rotationErrorToleranceDegrees = NormalizeFloat(value, 0.001f, 10f, DefaultRotationErrorToleranceDegrees);
        }

        internal PhantomSystemProjectSettingsSnapshot CreateSnapshot()
        {
            return new PhantomSystemProjectSettingsSnapshot(
                PhantomViewTextureSize,
                MaximumAdaptiveSampleRate,
                PositionErrorTolerance,
                RotationErrorToleranceDegrees);
        }

        internal void SaveImmediately()
        {
            Save(true);
        }

        internal void ResetToDefaults()
        {
            phantomViewTextureSize = DefaultViewTextureSize;
            maximumAdaptiveSampleRate = DefaultMaximumAdaptiveSampleRate;
            positionErrorTolerance = DefaultPositionErrorTolerance;
            rotationErrorToleranceDegrees = DefaultRotationErrorToleranceDegrees;
            SaveImmediately();
        }

        private static int NormalizeTextureSize(int value)
        {
            foreach (var size in ViewTextureSizes)
            {
                if (value == size)
                {
                    return value;
                }
            }

            return DefaultViewTextureSize;
        }

        private static float NormalizeFloat(
            float value,
            float minimum,
            float maximum,
            float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : Mathf.Clamp(value, minimum, maximum);
        }
    }

    internal readonly struct PhantomSystemProjectSettingsSnapshot
    {
        internal int PhantomViewTextureSize { get; }
        internal float MaximumAdaptiveSampleRate { get; }
        internal float PositionErrorTolerance { get; }
        internal float RotationErrorToleranceDegrees { get; }

        internal PhantomSystemProjectSettingsSnapshot(
            int phantomViewTextureSize,
            float maximumAdaptiveSampleRate,
            float positionErrorTolerance,
            float rotationErrorToleranceDegrees)
        {
            PhantomViewTextureSize = phantomViewTextureSize;
            MaximumAdaptiveSampleRate = maximumAdaptiveSampleRate;
            PositionErrorTolerance = positionErrorTolerance;
            RotationErrorToleranceDegrees = rotationErrorToleranceDegrees;
        }
    }
}
