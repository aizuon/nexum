using Nexum.Core.Attributes;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.Message.C2S
{
    [NetCoreMessage(MessageType.NotifyCSEncryptedSessionKey)]
    internal partial class NotifyCSEncryptedSessionKey
    {
        [NetProperty(0)]
        public ByteArray EncryptedSessionKey { get; set; }

        [NetProperty(1)]
        public ByteArray EncryptedFastSessionKey { get; set; }
    }
}
