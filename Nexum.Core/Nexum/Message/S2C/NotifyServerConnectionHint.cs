using Nexum.Core.Attributes;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.Message.S2C
{
    [NetCoreMessage(MessageType.NotifyServerConnectionHint)]
    internal partial class NotifyServerConnectionHint
    {
        [NetProperty(0)]
        public bool EnableServerLog { get; set; }

        [NetProperty(1)]
        public FallbackMethod FallbackMethod { get; set; }

        [NetProperty(2)]
        public uint MessageMaxLength { get; set; }

        [NetProperty(3)]
        public double IdleTimeout { get; set; }

        [NetProperty(4)]
        public DirectP2PStartCondition DirectP2PStartCondition { get; set; }

        [NetProperty(5)]
        public uint OverSendSuspectingThresholdInBytes { get; set; }

        [NetProperty(6)]
        public bool EnableNagleAlgorithm { get; set; }

        [NetProperty(7)]
        public uint EncryptedMessageKeyLength { get; set; }

        [NetProperty(8)]
        public uint FastEncryptedMessageKeyLength { get; set; }

        [NetProperty(9)]
        public bool AllowServerAsP2PGroupMember { get; set; }

        [NetProperty(10)]
        public bool EnableP2PEncryptedMessaging { get; set; }

        [NetProperty(11)]
        public bool UpnpDetectNatDevice { get; set; }

        [NetProperty(12)]
        public bool UpnpTcpAddrPortMapping { get; set; }

        [NetProperty(13)]
        public bool EnableLookaheadP2PSend { get; set; }

        [NetProperty(14)]
        public bool EnablePingTest { get; set; }

        [NetProperty(15)]
        public uint EmergencyLogLineCount { get; set; }

        [NetProperty(16)]
        public ByteArray RsaPublicKey { get; set; }
    }
}
