using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.C2S
{
    [NetRmi(NexumOpCode.NotifyJitDirectP2PTriggered)]
    internal partial class NotifyJitDirectP2PTriggered
    {
        [NetProperty(0)]
        public uint HostId { get; set; }
    }
}
