using System.Collections.Generic;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    internal enum PhantomTrackingControlGroup
    {
        Head,
        LeftHand,
        RightHand,
        Hip,
        LeftFoot,
        RightFoot,
        LeftFingers,
        RightFingers,
        Eyes,
        Mouth
    }

    internal static class PhantomTrackingControlGroups
    {
        public static readonly PhantomTrackingControlGroup[] All =
        {
            PhantomTrackingControlGroup.Head,
            PhantomTrackingControlGroup.LeftHand,
            PhantomTrackingControlGroup.RightHand,
            PhantomTrackingControlGroup.Hip,
            PhantomTrackingControlGroup.LeftFoot,
            PhantomTrackingControlGroup.RightFoot,
            PhantomTrackingControlGroup.LeftFingers,
            PhantomTrackingControlGroup.RightFingers,
            PhantomTrackingControlGroup.Eyes,
            PhantomTrackingControlGroup.Mouth
        };

        public static string Parameter(PhantomSlot slot, PhantomTrackingControlGroup group)
        {
            return group switch
            {
                PhantomTrackingControlGroup.Head => PhantomParameterNames.TrackingHead(slot),
                PhantomTrackingControlGroup.LeftHand => PhantomParameterNames.TrackingLeftHand(slot),
                PhantomTrackingControlGroup.RightHand => PhantomParameterNames.TrackingRightHand(slot),
                PhantomTrackingControlGroup.Hip => PhantomParameterNames.TrackingHip(slot),
                PhantomTrackingControlGroup.LeftFoot => PhantomParameterNames.TrackingLeftFoot(slot),
                PhantomTrackingControlGroup.RightFoot => PhantomParameterNames.TrackingRightFoot(slot),
                PhantomTrackingControlGroup.LeftFingers => PhantomParameterNames.TrackingLeftFingers(slot),
                PhantomTrackingControlGroup.RightFingers => PhantomParameterNames.TrackingRightFingers(slot),
                PhantomTrackingControlGroup.Eyes => PhantomParameterNames.TrackingEyes(slot),
                PhantomTrackingControlGroup.Mouth => PhantomParameterNames.TrackingMouth(slot),
                _ => null
            };
        }

        public static IEnumerable<string> Parameters(PhantomSlot slot)
        {
            foreach (var group in All)
            {
                yield return Parameter(slot, group);
            }
        }

        public static IReadOnlyList<HumanBodyBones> Bones(PhantomTrackingControlGroup group)
        {
            switch (group)
            {
                case PhantomTrackingControlGroup.Head:
                    return new[] { HumanBodyBones.Neck, HumanBodyBones.Head };
                case PhantomTrackingControlGroup.LeftHand:
                    return new[]
                    {
                        HumanBodyBones.LeftShoulder,
                        HumanBodyBones.LeftUpperArm,
                        HumanBodyBones.LeftLowerArm,
                        HumanBodyBones.LeftHand
                    };
                case PhantomTrackingControlGroup.RightHand:
                    return new[]
                    {
                        HumanBodyBones.RightShoulder,
                        HumanBodyBones.RightUpperArm,
                        HumanBodyBones.RightLowerArm,
                        HumanBodyBones.RightHand
                    };
                case PhantomTrackingControlGroup.Hip:
                    return new[]
                    {
                        HumanBodyBones.Hips,
                        HumanBodyBones.Spine,
                        HumanBodyBones.Chest,
                        HumanBodyBones.UpperChest
                    };
                case PhantomTrackingControlGroup.LeftFoot:
                    return new[]
                    {
                        HumanBodyBones.LeftUpperLeg,
                        HumanBodyBones.LeftLowerLeg,
                        HumanBodyBones.LeftFoot,
                        HumanBodyBones.LeftToes
                    };
                case PhantomTrackingControlGroup.RightFoot:
                    return new[]
                    {
                        HumanBodyBones.RightUpperLeg,
                        HumanBodyBones.RightLowerLeg,
                        HumanBodyBones.RightFoot,
                        HumanBodyBones.RightToes
                    };
                case PhantomTrackingControlGroup.LeftFingers:
                    return new[]
                    {
                        HumanBodyBones.LeftThumbProximal,
                        HumanBodyBones.LeftThumbIntermediate,
                        HumanBodyBones.LeftThumbDistal,
                        HumanBodyBones.LeftIndexProximal,
                        HumanBodyBones.LeftIndexIntermediate,
                        HumanBodyBones.LeftIndexDistal,
                        HumanBodyBones.LeftMiddleProximal,
                        HumanBodyBones.LeftMiddleIntermediate,
                        HumanBodyBones.LeftMiddleDistal,
                        HumanBodyBones.LeftRingProximal,
                        HumanBodyBones.LeftRingIntermediate,
                        HumanBodyBones.LeftRingDistal,
                        HumanBodyBones.LeftLittleProximal,
                        HumanBodyBones.LeftLittleIntermediate,
                        HumanBodyBones.LeftLittleDistal
                    };
                case PhantomTrackingControlGroup.RightFingers:
                    return new[]
                    {
                        HumanBodyBones.RightThumbProximal,
                        HumanBodyBones.RightThumbIntermediate,
                        HumanBodyBones.RightThumbDistal,
                        HumanBodyBones.RightIndexProximal,
                        HumanBodyBones.RightIndexIntermediate,
                        HumanBodyBones.RightIndexDistal,
                        HumanBodyBones.RightMiddleProximal,
                        HumanBodyBones.RightMiddleIntermediate,
                        HumanBodyBones.RightMiddleDistal,
                        HumanBodyBones.RightRingProximal,
                        HumanBodyBones.RightRingIntermediate,
                        HumanBodyBones.RightRingDistal,
                        HumanBodyBones.RightLittleProximal,
                        HumanBodyBones.RightLittleIntermediate,
                        HumanBodyBones.RightLittleDistal
                    };
                case PhantomTrackingControlGroup.Eyes:
                    return new[] { HumanBodyBones.LeftEye, HumanBodyBones.RightEye };
                case PhantomTrackingControlGroup.Mouth:
                    return new[] { HumanBodyBones.Jaw };
                default:
                    return new HumanBodyBones[0];
            }
        }
    }
}
