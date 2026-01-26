using Nexum.Core.Attributes;
using Nexum.Core.Configuration;

namespace Nexum.Core.Rmi.C2C
{
    [NetRmi(NexumOpCode.ReportServerTimeAndFrameRateAndPing)]
    internal partial class ReportServerTimeAndFrameRatePing
    {
        [NetProperty(0)]
        public double ClientLocalTime { get; set; }

        [NetProperty(1)]
        public double PeerFrameRate { get; set; }
    }
}
