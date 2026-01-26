using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Message.S2C
{
    [NetCoreMessage(MessageType.UnreliablePong)]
    internal partial class UnreliablePong
    {
        [NetProperty(0)]
        public double SentTime { get; set; }

        [NetProperty(1)]
        public double ServerTime { get; set; }

        [NetProperty(2)]
        public int PaddingSize { get; set; }
    }
}
