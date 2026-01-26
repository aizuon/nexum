namespace Nexum.Core.Configuration
{
    internal static class HolepunchConfig
    {
        internal const int BurstCount = 8;
        internal const int BurstDelayMs = 50;

        internal const double NatPortRecycleReuseSeconds = 10.0;
        internal const int NatPortShotgunTrialCount = 8;
        internal const int NatPortShotgunRange = 16;

        internal const int UdpMatchedDelayMs = 75;
        internal const int RetryIntervalMs = 1500;
        internal const int MaxRetryAttempts = 15;
        internal const int InitialBackoffDelayMs = 2;
        internal const int MaxBackoffDelayMs = 50;
        internal const int MaxSocketWaitAttempts = 25;
        internal const double UdpSetupRetrySeconds = 8.0;
        internal const double UdpPingTimeoutSeconds = 15.0;
    }
}
