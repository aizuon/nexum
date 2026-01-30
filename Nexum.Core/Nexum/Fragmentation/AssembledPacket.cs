using System.Net;

namespace Nexum.Core.Fragmentation
{
    internal sealed class AssembledPacket
    {
        internal DefraggingPacket Packet { get; set; }

        internal IPEndPoint SenderEndPoint { get; set; }

        internal uint SrcHostId { get; set; }

        internal ushort FilterTag { get; set; }
    }
}
