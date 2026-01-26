using System.Net;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.S2C
{
    [NetRmi(NexumOpCode.NotifyDirectP2PEstablish)]
    internal partial class NotifyDirectP2PEstablish
    {
        [NetProperty(0)]
        public uint HostIdA { get; set; }

        [NetProperty(1)]
        public uint HostIdB { get; set; }

        [NetProperty(2)]
        public IPEndPoint ASendAddrToB { get; set; }

        [NetProperty(3)]
        public IPEndPoint BRecvAddrFromA { get; set; }

        [NetProperty(4)]
        public IPEndPoint BSendAddrToA { get; set; }

        [NetProperty(5)]
        public IPEndPoint ARecvAddrFromB { get; set; }
    }
}
