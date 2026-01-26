using System.Net;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.S2C
{
    [NetRmi(NexumOpCode.RequestP2PHolepunch)]
    internal partial class RequestP2PHolepunch
    {
        [NetProperty(0)]
        public uint HostId { get; set; }

        [NetProperty(1)]
        public IPEndPoint LocalEndPoint { get; set; }

        [NetProperty(2)]
        public IPEndPoint ExternalEndPoint { get; set; }
    }
}
