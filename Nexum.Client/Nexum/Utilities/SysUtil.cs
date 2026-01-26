using System.Runtime.CompilerServices;

namespace Nexum.Client.Utilities
{
    internal static class SysUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Swap<T>(ref T lhs, ref T rhs)
        {
            var obj = lhs;
            lhs = rhs;
            rhs = obj;
        }
    }
}
