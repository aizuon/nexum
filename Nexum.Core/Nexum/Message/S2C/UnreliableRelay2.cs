using Nexum.Core.Attributes;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.Message.S2C
{
    [NetCoreMessage(MessageType.UnreliableRelay2)]
    internal partial class UnreliableRelay2
    {
        [NetProperty(0)]
        public uint HostId { get; set; }

        [NetProperty(1)]
        public ByteArray Data { get; set; }
    }
}
