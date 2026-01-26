using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.C2C
{
    [NetRmi(NexumOpCode.ReportServerTimeAndFrameRateAndPong)]
    internal partial class ReportServerTimeAndFrameRatePong
    {
        [NetProperty(0)]
        public double OriginalClientLocalTime { get; set; }

        [NetProperty(1)]
        public double PeerLocalTime { get; set; }

        [NetProperty(2)]
        public double PeerServerPing { get; set; }

        [NetProperty(3)]
        public double PeerFrameRate { get; set; }
    }
}
