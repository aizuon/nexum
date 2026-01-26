using System;
using Nexum.Core.Attributes;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.Message.C2S
{
    [NetCoreMessage(MessageType.NotifyServerConnectionRequestData)]
    internal partial class NotifyServerConnectionRequestData
    {
        [NetProperty(0)]
        public ByteArray UserData { get; set; }

        [NetProperty(1)]
        public Guid ProtocolVersion { get; set; }

        [NetProperty(2)]
        public uint InternalVersion { get; set; }
    }
}
