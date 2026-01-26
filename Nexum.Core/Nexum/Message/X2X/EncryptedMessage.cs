using Nexum.Core.Attributes;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.Message.X2X
{
    [NetCoreMessage(MessageType.Encrypted)]
    internal partial class EncryptedMessage
    {
        [NetProperty(0)]
        public EncryptMode EncryptMode { get; set; }

        [NetProperty(1)]
        public ByteArray EncryptedData { get; set; }
    }
}
