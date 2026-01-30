using System;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.X2X
{
    [NetCoreMessage(MessageType.PeerUdp_ServerHolepunch)]
    internal partial class PeerUdpServerHolepunch
    {
        [NetProperty(0)]
        public Guid MagicNumber { get; set; }

        [NetProperty(1)]
        public uint TargetHostId { get; set; }
    }
}
