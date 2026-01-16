using System.Net;
using DotNetty.Buffers;

namespace Nexum.Core
{
    internal class UdpMessage
    {
        internal ushort SplitterFlag { get; set; }

        internal ushort FilterTag { get; set; }

        internal int PacketLength { get; set; }

        internal uint PacketId { get; set; }

        internal uint FragmentId { get; set; }


        internal IByteBuffer Content { get; set; }

        internal IPEndPoint EndPoint { get; set; }
    }
}
