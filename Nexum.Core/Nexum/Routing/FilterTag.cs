using System.Runtime.CompilerServices;

namespace Nexum.Core.Routing
{
    internal static class FilterTag
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ushort Create(uint srcHostId, uint destHostId)
        {
            return (ushort)(((destHostId | (srcHostId << 8)) & 0xFF) | ((srcHostId << 8) & 0xFF00));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool ShouldFilter(ushort filterTag, uint srcHostId, uint localHostId)
        {
            if (filterTag == 0 && srcHostId == (uint)HostId.Server)
                return false;

            if (srcHostId == (uint)HostId.None)
                return false;

            ushort expectedFilterTag = Create(srcHostId, localHostId);
            return filterTag != expectedFilterTag;
        }
    }
}
