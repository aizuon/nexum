using System.Net;

namespace Nexum.Core
{
    internal sealed class AssembledPacket
    {
        public DefraggingPacket Packet { get; set; }

        public IPEndPoint SenderEndPoint { get; set; }

        public uint SrcHostId { get; set; }
    }
}
