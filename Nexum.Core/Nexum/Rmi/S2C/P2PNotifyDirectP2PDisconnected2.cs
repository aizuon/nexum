using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.S2C
{
    [NetRmi(NexumOpCode.P2P_NotifyDirectP2PDisconnected2)]
    internal partial class P2PNotifyDirectP2PDisconnected2
    {
        [NetProperty(0)]
        public uint HostId { get; set; }

        [NetProperty(1)]
        public ErrorType Reason { get; set; }
    }
}
