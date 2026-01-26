using System;
using System.Net;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.Message.S2C
{
    [NetCoreMessage(MessageType.NotifyServerConnectSuccess)]
    internal partial class NotifyServerConnectSuccess
    {
        [NetProperty(0)]
        public uint HostId { get; set; }

        [NetProperty(1)]
        public Guid ServerInstanceGuid { get; set; }

        [NetProperty(2)]
        public ByteArray UserData { get; set; }

        [NetProperty(3)]
        public IPEndPoint ServerEndPoint { get; set; }
    }
}
