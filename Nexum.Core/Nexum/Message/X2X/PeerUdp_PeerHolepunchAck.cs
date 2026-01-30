using System;
using System.Net;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.X2X
{
    [NetCoreMessage(MessageType.PeerUdp_PeerHolepunchAck)]
    internal partial class PeerUdpPeerHolepunchAck
    {
        [NetProperty(0)]
        public Guid MagicNumber { get; set; }

        [NetProperty(1)]
        public uint HostId { get; set; }

        [NetProperty(2)]
        public IPEndPoint SelfUdpSocket { get; set; }

        [NetProperty(3)]
        public IPEndPoint ReceivedEndPoint { get; set; }

        [NetProperty(4)]
        public IPEndPoint TargetEndPoint { get; set; }
    }
}
