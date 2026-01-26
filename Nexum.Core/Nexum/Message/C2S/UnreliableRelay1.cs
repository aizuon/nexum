using Nexum.Core.Attributes;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.Message.C2S
{
    [NetCoreMessage(MessageType.UnreliableRelay1)]
    internal partial class UnreliableRelay1
    {
        [NetProperty(0)]
        public MessagePriority Priority { get; set; }

        [NetProperty(1, typeof(ScalarSerializer))]
        public long UniqueId { get; set; }

        [NetProperty(2)]
        public uint[] DestinationHostIds { get; set; }

        [NetProperty(3)]
        public ByteArray Data { get; set; }
    }
}
