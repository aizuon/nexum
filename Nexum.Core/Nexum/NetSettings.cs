namespace Nexum.Core
{
    public sealed class NetSettings
    {
        public bool AllowServerAsP2PGroupMember = false;
        public DirectP2PStartCondition DirectP2PStartCondition = DirectP2PStartCondition.Always;
        public uint EmergencyLogLineCount = 0;
        public bool EnableLookaheadP2PSend = false;
        public bool EnableNagleAlgorithm = true;
        public bool EnableP2PEncryptedMessaging = false;
        public bool EnablePingTest = false;
        public bool EnableServerLog = false;
        public uint EncryptedMessageKeyLength = NetCrypt.DefaultKeyLength;
        public FallbackMethod FallbackMethod = FallbackMethod.None;
        public uint FastEncryptedMessageKeyLength = NetCrypt.DefaultFastKeyLength;
        public double IdleTimeout = NetConfig.NoPingTimeoutTime;
        public uint MessageMaxLength = NetConfig.MessageMaxLength;
        public uint OverSendSuspectingThresholdInBytes = NetConfig.DefaultOverSendSuspectingThresholdInBytes;
        public bool UpnpDetectNatDevice = NetConfig.UpnpDetectNatDeviceByDefault;
        public bool UpnpTcpAddrPortMapping = NetConfig.UpnpTcpAddrPortMappingByDefault;
    }
}
