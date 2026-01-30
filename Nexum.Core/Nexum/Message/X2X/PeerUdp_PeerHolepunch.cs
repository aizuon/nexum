using System;
using System.Net;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.X2X
{
    [NetCoreMessage(MessageType.PeerUdp_PeerHolepunch)]
    internal partial class PeerUdp_PeerHolepunch
    {
        [NetProperty(0)]
        public uint HostId { get; set; }

        [NetProperty(1)]
        public Guid PeerMagicNumber { get; set; }

        [NetProperty(2)]
        public Guid ServerInstanceGuid { get; set; }

        [NetProperty(3)]
        public IPEndPoint TargetEndpoint { get; set; }
    }
}
