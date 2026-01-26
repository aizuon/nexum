using Nexum.Core.Attributes;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.Message.X2X
{
    [NetCoreMessage(MessageType.Rmi)]
    internal partial class RmiMessage
    {
        [NetProperty(0)]
        public ushort RmiId { get; set; }

        [NetProperty(2, typeof(RawByteArraySerializer))]
        public ByteArray Data { get; set; }
    }
}
