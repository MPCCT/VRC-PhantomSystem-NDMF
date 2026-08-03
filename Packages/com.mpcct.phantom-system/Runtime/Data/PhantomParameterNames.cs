namespace MPCCT.PhantomSystem
{
    public static class PhantomParameterNames
    {
        public static string Activate(PhantomSlot slot) => Name(slot, "Activate");
        public static string Freeze(PhantomSlot slot) => Name(slot, "Freeze");
        public static string PositionLock(PhantomSlot slot) => Name(slot, "PositionLock");
        public static string Scale(PhantomSlot slot) => Name(slot, "Scale");
        public static string Mirror(PhantomSlot slot) => Name(slot, "Mirror");
        public static string ScaleReset(PhantomSlot slot) => Name(slot, "ScaleReset");
        public static string ScaleControlWeight(PhantomSlot slot) => Name(slot, "ScaleControl/Weight");
        public static string PhantomGrabbingContactLeft(PhantomSlot slot) =>
            Name(slot, "PhantomGrabbing/ContactLeft");
        public static string PhantomGrabbingContactRight(PhantomSlot slot) =>
            Name(slot, "PhantomGrabbing/ContactRight");
        public static string PhantomGrabbingShowBones(PhantomSlot slot) =>
            Name(slot, "PhantomGrabbing/ShowBones");

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

