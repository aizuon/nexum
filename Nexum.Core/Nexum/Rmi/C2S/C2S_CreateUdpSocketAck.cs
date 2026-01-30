using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.C2S
{
    [NetRmi(NexumOpCode.C2S_CreateUdpSocketAck)]
    internal partial class C2SCreateUdpSocketAck
    {
        [NetProperty(0)]
        public bool Success { get; set; }
    }
}
