using Nexum.Core.Attributes;
using Nexum.Core.Configuration;
using Nexum.Core.Message.DTO;
using Nexum.Core.Serialization;

namespace Nexum.Core.Message.C2S
{
    [NetCoreMessage(MessageType.ReliableRelay1)]
    internal partial class ReliableRelay1
    {
        [NetProperty(0)]
        public RelayDestination[] Destinations { get; set; }

        [NetProperty(1)]
        public ByteArray Data { get; set; }
    }
}
