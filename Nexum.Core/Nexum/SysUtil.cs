namespace Nexum.Core
{
    internal static class SysUtil
    {
        public static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }
    }
}
