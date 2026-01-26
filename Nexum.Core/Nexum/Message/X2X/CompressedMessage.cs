using Nexum.Core.Attributes;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.Message.X2X
{
    [NetCoreMessage(MessageType.Compressed)]
    internal partial class CompressedMessage
    {
        [NetProperty(0, typeof(ScalarSerializer))]
        public long CompressedSize { get; set; }

        [NetProperty(1, typeof(ScalarSerializer))]
        public long OriginalSize { get; set; }

        [NetProperty(2, typeof(RawByteArraySerializer))]
        public ByteArray CompressedData { get; set; }
    }
}
