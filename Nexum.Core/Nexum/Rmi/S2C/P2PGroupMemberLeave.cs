using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.S2C
{
    [NetRmi(NexumOpCode.P2PGroup_MemberLeave)]
    internal partial class P2PGroupMemberLeave
    {
        [NetProperty(0)]
        public uint HostId { get; set; }

        [NetProperty(1)]
        public uint GroupHostId { get; set; }
    }
}
