using System;
using System.Runtime.CompilerServices;

namespace Nexum.Core.Utilities
{
    public static class NetUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double CalculateJitter(double currentJitter, double currentPing, double previousPing)
        {
            double pingDeviation = Math.Abs(currentPing - previousPing);
            return currentJitter + (pingDeviation - currentJitter) / 16.0;
        }
    }
}
