namespace Nexum.Core
{
    internal static class NetConfig
    {
        internal const int MessageMaxLength = 1048576;
        internal const int DefaultOverSendSuspectingThresholdInBytes = 15360;
        internal const bool UpnpDetectNatDeviceByDefault = true;
        internal const bool UpnpTcpAddrPortMappingByDefault = true;
        internal const bool EnableNagleAlgorithm = true;
    }
}
