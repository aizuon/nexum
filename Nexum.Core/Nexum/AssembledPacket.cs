using System.Net;

namespace Nexum.Core
{
    internal sealed class AssembledPacket
    {
        internal DefraggingPacket Packet { get; set; }

        internal IPEndPoint SenderEndPoint { get; set; }

        internal uint SrcHostId { get; set; }
    }
}
