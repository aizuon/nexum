using System.Net;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.C2S
{
    [NetCoreMessage(MessageType.PeerUdp_NotifyHolepunchSuccess)]
    internal partial class PeerUdpNotifyHolepunchSuccess
    {
        [NetProperty(0)]
        public IPEndPoint LocalEndPoint { get; set; }

        [NetProperty(1)]
        public IPEndPoint PublicEndPoint { get; set; }

        [NetProperty(2)]
        public uint HostId { get; set; }
    }
}
