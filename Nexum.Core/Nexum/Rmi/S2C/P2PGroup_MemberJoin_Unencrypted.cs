using System;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.Rmi.S2C
{
    [NetRmi(NexumOpCode.P2PGroup_MemberJoin_Unencrypted)]
    internal partial class P2PGroup_MemberJoin_Unencrypted
    {
        [NetProperty(0)]
        public uint GroupHostId { get; set; }

        [NetProperty(1)]
        public uint HostId { get; set; }

        [NetProperty(2)]
        public ByteArray CustomField { get; set; }

        [NetProperty(3)]
        public uint EventId { get; set; }

        [NetProperty(4)]
        public uint P2PFirstFrameNumber { get; set; }

        [NetProperty(5)]
        public Guid PeerUdpMagicNumber { get; set; }

        [NetProperty(6)]
        public bool EnableDirectP2P { get; set; }

        [NetProperty(7)]
        public int BindPort { get; set; }
    }
}
