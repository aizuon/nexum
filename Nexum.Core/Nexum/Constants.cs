using System.Text;

namespace Nexum.Core
{
    internal static class Constants
    {
        internal const ushort TcpSplitter = 0x5713;
        internal const ushort UdpFullPacketSplitter = 0xABCD;
        internal const ushort UdpFragmentSplitter = 0xABCE;
        internal const uint NetVersion = 196980;
        internal static readonly Encoding Encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252);
    }

    internal static class HolepunchConstants
    {
        internal const int BurstCount = 3;

        internal const int BurstDelayMs = 15;

        internal const int UdpMatchedDelayMs = 25;

        internal const int RetryIntervalMs = 4000;

        internal const int MaxRetryAttempts = 10;

        internal const int InitialBackoffDelayMs = 5;

        internal const int MaxBackoffDelayMs = 100;

        internal const int MaxSocketWaitAttempts = 20;

        internal const int UdpSetupRetrySeconds = 8;

        internal const int UdpPingTimeoutSeconds = 20;
    }
}
