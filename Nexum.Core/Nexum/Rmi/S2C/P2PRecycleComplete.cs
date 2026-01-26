using System.Net;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.S2C
{
    [NetRmi(NexumOpCode.P2PRecycleComplete)]
    internal partial class P2PRecycleComplete
    {
        [NetProperty(0)]
        public uint HostId { get; set; }

        [NetProperty(1)]
        public bool Recycled { get; set; }

        [NetProperty(2)]
        public IPEndPoint InternalAddr { get; set; }

        [NetProperty(3)]
        public IPEndPoint ExternalAddr { get; set; }

        [NetProperty(4)]
        public IPEndPoint SendAddr { get; set; }

        [NetProperty(5)]
        public IPEndPoint RecvAddr { get; set; }
    }
}
