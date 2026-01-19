namespace Nexum.Core
{
    internal static class NetConfig
    {
        internal const int MessageMaxLength = 1048576;
        internal const int DefaultOverSendSuspectingThresholdInBytes = 15360;
        internal const bool UpnpDetectNatDeviceByDefault = true;
        internal const bool UpnpTcpAddrPortMappingByDefault = true;
        internal const bool EnableNagleAlgorithm = true;
        internal const int TcpIssueRecvLength = 51200;
        internal const int UdpIssueRecvLength = 51200;
        internal const int TcpSendBufferLength = 8192;
        internal const int UdpSendBufferLength = 8192;
        internal const ushort UdpAimForPort = 58800;
        internal const double NoPingTimeoutTime = 900.0;
    }
}
