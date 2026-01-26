namespace Nexum.Core.Configuration
{
    internal static class MtuConfig
    {
        internal const int MinMtu = 400;
        internal const int MaxMtu = 1400;
        internal const int DefaultMtu = 500;

        internal const int RequiredSuccessCount = 2;
        internal const int MaxFailureCount = 2;
        internal const double ProbeTimeoutSeconds = 0.8;
        internal const double ProbeIntervalSeconds = 0.15;
        internal const int HeaderOverhead = 40;
    }
}
