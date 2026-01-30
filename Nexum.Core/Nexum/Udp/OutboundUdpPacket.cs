using System.Net;
using DotNetty.Buffers;

namespace Nexum.Core.Udp
{
    internal sealed class OutboundUdpPacket
    {
        internal IByteBuffer Content { get; set; }

        internal IPEndPoint EndPoint { get; set; }

        internal ushort FilterTag { get; set; }

        internal int Mtu { get; set; }
    }
}
