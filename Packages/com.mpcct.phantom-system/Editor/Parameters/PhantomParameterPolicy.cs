using System.Collections.Generic;

namespace MPCCT.PhantomSystem.Editor
{
    internal static class PhantomParameterPolicy
    {
        private static readonly HashSet<string> VrcReservedParameters = new HashSet<string>
        {
            "IsLocal",
            "PreviewMode",
            "Viseme",
            "Voice",
            "GestureLeft",
            "GestureRight",
            "GestureLeftWeight",
            "GestureRightWeight",
            "AngularY",
            "VelocityX",
            "VelocityY",
            "VelocityZ",
            "VelocityMagnitude",
            "Upright",
            "Grounded",
            "Seated",
            "AFK",
            "TrackingType",
            "VRMode",
            "MuteSelf",
            "InStation",
            "Earmuffs",
            "IsOnFriendsList",
            "AvatarVersion",
            "ScaleModified",
            "ScaleFactor",
            "ScaleFactorInverse",
            "EyeHeightAsMeters",
            "EyeHeightAsPercent",
            "IsAnimatorEnabled",
            "VRCEmote",
            "VRCFaceBlendH",
            "VRCFaceBlendV"
        };

        public static bool IsVrcReserved(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && VrcReservedParameters.Contains(name);
        }

        public static bool IsConfiguredShared(PhantomSlot slot, string name)
        {
            if (slot?.sharedParameterNames == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            foreach (var configuredName in slot.sharedParameterNames)
            {
                if (string.Equals(configuredName, name, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
