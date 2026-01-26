using System;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.S2C
{
    [NetCoreMessage(MessageType.NotifyClientServerUdpMatched)]
    internal partial class NotifyClientServerUdpMatched
    {
        [NetProperty(0)]
        public Guid MagicNumber { get; set; }
    }
}
