using System.Net;
using Nexum.Core.ReliableUdp;

namespace Nexum.Core.Udp
{
    internal sealed class InboundReliableUdpFrame
    {
        internal ReliableUdpFrame Frame { get; set; }

        internal IPEndPoint SenderEndPoint { get; set; }

        internal ushort FilterTag { get; set; }

        internal uint SrcHostId { get; set; }
    }

    internal sealed class OutboundReliableUdpFrame
    {
        internal ReliableUdpFrame Frame { get; set; }

        internal IPEndPoint DestEndPoint { get; set; }

        internal ushort FilterTag { get; set; }

        internal int Mtu { get; set; }
    }
}
