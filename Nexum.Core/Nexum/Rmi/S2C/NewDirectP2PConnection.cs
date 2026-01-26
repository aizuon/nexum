using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.S2C
{
    [NetRmi(NexumOpCode.NewDirectP2PConnection)]
    internal partial class NewDirectP2PConnection
    {
        [NetProperty(0)]
        public uint HostId { get; set; }
    }
}
