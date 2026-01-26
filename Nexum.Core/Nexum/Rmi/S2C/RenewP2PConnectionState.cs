using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.S2C
{
    [NetRmi(NexumOpCode.RenewP2PConnectionState)]
    internal partial class RenewP2PConnectionState
    {
        [NetProperty(0)]
        public uint HostId { get; set; }
    }
}
