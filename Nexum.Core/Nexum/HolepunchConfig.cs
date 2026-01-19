namespace Nexum.Core
{
    internal static class HolepunchConfig
    {
        internal const int BurstCount = 5;
        internal const int BurstDelayMs = 10;
        internal const int UdpMatchedDelayMs = 20;
        internal const int RetryIntervalMs = 2500;
        internal const int MaxRetryAttempts = 10;
        internal const int InitialBackoffDelayMs = 2;
        internal const int MaxBackoffDelayMs = 50;
        internal const int MaxSocketWaitAttempts = 25;
        internal const int UdpSetupRetrySeconds = 8;
        internal const int UdpPingTimeoutSeconds = 15;
    }
}
