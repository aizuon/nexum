using Nexum.Core.Attributes;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.Message.S2C
{
    [NetCoreMessage(MessageType.ReliableRelay2)]
    internal partial class ReliableRelay2
    {
        [NetProperty(0)]
        public uint HostId { get; set; }

        [NetProperty(1)]
        public uint FrameNumber { get; set; }

        [NetProperty(2)]
        public ByteArray Data { get; set; }
    }
}
