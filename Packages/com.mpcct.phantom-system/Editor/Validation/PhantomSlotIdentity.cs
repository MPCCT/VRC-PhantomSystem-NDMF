using System;

namespace MPCCT.PhantomSystem.Editor
{
    internal sealed class PhantomSlotIdentity
    {
        public string RawId { get; }
        public string SlotId { get; }
        public string HierarchyName { get; }
        public string ParameterPrefix { get; }

        private PhantomSlotIdentity(
            string rawId,
            string slotId,
            string hierarchyName,
            string parameterPrefix)
        {
            RawId = rawId;
            SlotId = slotId;
            HierarchyName = hierarchyName;
            ParameterPrefix = parameterPrefix;
        }

        public static PhantomSlotIdentity Create(PhantomSlot slot)
        {
            var rawId = slot?.id;
            var slotId = string.IsNullOrWhiteSpace(rawId)
                ? PhantomSlot.DefaultId
                : rawId.Trim();
            var configuredPrefix = slot?.parameterPrefix?.Trim().TrimEnd('/');
            var parameterPrefix = string.IsNullOrWhiteSpace(configuredPrefix)
                ? $"PhantomSystem/{slotId}"
                : configuredPrefix;
            return new PhantomSlotIdentity(
                rawId,
                slotId,
                TransformPathUtility.SafeName(slotId, PhantomSlot.DefaultId),
                parameterPrefix);
        }

        public string OriginalParameterName(string originalName)
        {
            return $"{ParameterPrefix}/Original/{originalName}";
        }
    }
}
