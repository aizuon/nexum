using System.Net;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.S2C
{
    [NetRmi(NexumOpCode.S2C_CreateUdpSocketAck)]
    internal partial class S2CCreateUdpSocketAck
    {
        [NetProperty(0)]
        public bool Result { get; set; }

        [NetProperty(1, typeof(StringEndPointSerializer))]
        public IPEndPoint UdpSocket { get; set; }
    }
}
