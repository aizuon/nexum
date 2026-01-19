namespace Nexum.Core
{
    internal static class MtuConfig
    {
        internal const int MinMtu = 400;
        internal const int MaxMtu = 1400;
        internal const int DefaultMtu = 500;

        internal const int RequiredSuccessCount = 2;
        internal const int MaxFailureCount = 3;
        internal const double ProbeTimeoutSeconds = 1.5;
        internal const double ProbeIntervalSeconds = 0.3;
        internal const int HeaderOverhead = 40;
    }
}
