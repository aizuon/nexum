using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.C2S
{
    [NetCoreMessage(MessageType.UnreliablePing)]
    internal partial class UnreliablePing
    {
        [NetProperty(0)]
        public double ClientTime { get; set; }

        [NetProperty(1)]
        public double ClientRecentPing { get; set; }

        [NetProperty(2)]
        public int PaddingSize { get; set; }
    }
}
