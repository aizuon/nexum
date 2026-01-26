using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.S2C
{
    [NetCoreMessage(MessageType.NotifyServerDeniedConnection)]
    internal partial class NotifyServerDeniedConnection
    {
    }
}
