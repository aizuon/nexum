namespace Nexum.Core
{
    public class NetSettings
    {
        public bool AllowServerAsP2PGroupMember;
        public DirectP2PStartCondition DirectP2PStartCondition;
        public uint EmergencyLogLineCount;
        public bool EnableLookaheadP2PSend;
        public bool EnableNagleAlgorithm;
        public bool EnableP2PEncryptedMessaging;
        public bool EnablePingTest;
        public bool EnableServerLog;
        public uint EncryptedMessageKeyLength;
        public FallbackMethod FallbackMethod;
        public uint FastEncryptedMessageKeyLength;
        public double IdleTimeout;
        public uint MessageMaxLength;
        public uint OverSendSuspectingThresholdInBytes;
        public bool UpnpDetectNatDevice;
        public bool UpnpTcpAddrPortMapping;
    }
}
