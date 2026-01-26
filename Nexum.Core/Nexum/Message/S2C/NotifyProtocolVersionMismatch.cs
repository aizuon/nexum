using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.S2C
{
    [NetCoreMessage(MessageType.NotifyProtocolVersionMismatch)]
    internal partial class NotifyProtocolVersionMismatch
    {
    }
}
