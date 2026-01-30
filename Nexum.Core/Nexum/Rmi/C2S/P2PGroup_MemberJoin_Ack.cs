using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.C2S
{
    [NetRmi(NexumOpCode.P2PGroup_MemberJoin_Ack)]
    internal partial class P2PGroup_MemberJoin_Ack
    {
        [NetProperty(0)]
        public uint GroupHostId { get; set; }

        [NetProperty(1)]
        public uint AddedMemberHostId { get; set; }

        [NetProperty(2)]
        public uint EventId { get; set; }

        [NetProperty(3)]
        public bool LocalPortReuseSuccess { get; set; }
    }
}
