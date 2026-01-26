using System;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.X2X
{
    [NetCoreMessage(MessageType.ServerHolepunch)]
    internal partial class ServerHolepunch
    {
        [NetProperty(0)]
        public Guid MagicNumber { get; set; }
    }
}
