using System;
using System.Net;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.C2S
{
    [NetCoreMessage(MessageType.NotifyHolepunchSuccess)]
    internal partial class NotifyHolepunchSuccess
    {
        [NetProperty(0)]
        public Guid MagicNumber { get; set; }

        [NetProperty(1)]
        public IPEndPoint LocalEndPoint { get; set; }

        [NetProperty(2)]
        public IPEndPoint PublicEndPoint { get; set; }
    }
}
