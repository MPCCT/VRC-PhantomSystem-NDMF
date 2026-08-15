namespace MPCCT.PhantomSystem
{
    public static class PhantomParameterNames
    {
        public static string Activate(PhantomSlot slot) => Name(slot, "Activate");
        public static string Freeze(PhantomSlot slot) => Name(slot, "Freeze");
        public static string PositionLock(PhantomSlot slot) => Name(slot, "PositionLock");
        public static string Scale(PhantomSlot slot) => Name(slot, "Scale");
        public static string Mirror(PhantomSlot slot) => Name(slot, "Mirror");
        public static string ScaleDirectWeight(PhantomSlot slot) =>
            Name(slot, "ScaleDirectWeight");
        public static string ScaleReset(PhantomSlot slot) => Name(slot, "ScaleReset");
        public static string PhantomViewEnabled(PhantomSlot slot) =>
            Name(slot, "PhantomView/Enabled");
        public static string PhantomViewStereoStrength(PhantomSlot slot) =>
            Name(slot, "PhantomView/StereoStrength");
        public static string PhantomViewMaskSize(PhantomSlot slot) =>
            Name(slot, "PhantomView/MaskSize");
        public static string PhantomViewDirectWeight(PhantomSlot slot) =>
            Name(slot, "PhantomView/DirectWeight");
        public static string PhantomGrabbingContactLeft(PhantomSlot slot) =>
            Name(slot, "PhantomGrabbing/ContactLeft");
        public static string PhantomGrabbingContactRight(PhantomSlot slot) =>
            Name(slot, "PhantomGrabbing/ContactRight");
        public static string PhantomGrabbingShowBones(PhantomSlot slot) =>
            Name(slot, "PhantomGrabbing/ShowBones");
        public static string TrackingHead(PhantomSlot slot) => Name(slot, "Tracking/Head");
        public static string TrackingLeftHand(PhantomSlot slot) => Name(slot, "Tracking/LeftHand");
        public static string TrackingRightHand(PhantomSlot slot) => Name(slot, "Tracking/RightHand");
        public static string TrackingHip(PhantomSlot slot) => Name(slot, "Tracking/Hip");
        public static string TrackingLeftFoot(PhantomSlot slot) => Name(slot, "Tracking/LeftFoot");
        public static string TrackingRightFoot(PhantomSlot slot) => Name(slot, "Tracking/RightFoot");
        public static string TrackingLeftFingers(PhantomSlot slot) => Name(slot, "Tracking/LeftFingers");
        public static string TrackingRightFingers(PhantomSlot slot) => Name(slot, "Tracking/RightFingers");
        public static string TrackingEyes(PhantomSlot slot) => Name(slot, "Tracking/Eyes");
        public static string TrackingMouth(PhantomSlot slot) => Name(slot, "Tracking/Mouth");
        public static string TrackingDirectWeight(PhantomSlot slot) =>
            Name(slot, "Tracking/DirectWeight");

        public static string OriginalParameterPrefix(PhantomSlot slot)
        {
            return $"{ParameterPrefix(slot)}/Original/";
        }

        private static string Name(PhantomSlot slot, string name)
        {
            return $"{ParameterPrefix(slot)}/{name}";
        }

        private static string ParameterPrefix(PhantomSlot slot)
        {
            var configuredPrefix = slot?.parameterPrefix?.Trim().TrimEnd('/');
            return !string.IsNullOrWhiteSpace(configuredPrefix)
                ? configuredPrefix
                : $"PhantomSystem/{SlotId(slot)}";
        }

        private static string SlotId(PhantomSlot slot)
        {
            return string.IsNullOrWhiteSpace(slot?.id) ? PhantomSlot.DefaultId : slot.id.Trim();
        }
    }
}
