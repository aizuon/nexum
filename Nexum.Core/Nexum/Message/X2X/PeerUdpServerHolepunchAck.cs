using System;
using System.Net;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.X2X
{
    [NetCoreMessage(MessageType.PeerUdp_ServerHolepunchAck)]
    internal partial class PeerUdpServerHolepunchAck
    {
        [NetProperty(0)]
        public Guid MagicNumber { get; set; }

        [NetProperty(1)]
        public IPEndPoint EndPoint { get; set; }

        [NetProperty(2)]
        public uint TargetHostId { get; set; }
    }
}
