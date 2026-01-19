using System.Runtime.CompilerServices;

namespace Nexum.Core
{
    internal static class SysUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }
    }
}
