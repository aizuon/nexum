using System.Net;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.S2C
{
    [NetRmi(NexumOpCode.S2C_RequestCreateUdpSocket)]
    internal partial class S2C_RequestCreateUdpSocket
    {
        [NetProperty(0, typeof(StringEndPointSerializer))]
        public IPEndPoint UdpSocket { get; set; }
    }
}
