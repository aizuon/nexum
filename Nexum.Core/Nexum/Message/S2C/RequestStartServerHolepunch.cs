using System;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.S2C
{
    [NetCoreMessage(MessageType.RequestStartServerHolepunch)]
    internal partial class RequestStartServerHolepunch
    {
        [NetProperty(0)]
        public Guid MagicNumber { get; set; }
    }
}
