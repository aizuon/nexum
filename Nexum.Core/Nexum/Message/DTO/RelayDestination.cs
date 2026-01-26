using Nexum.Core.Attributes;

namespace Nexum.Core.Message.DTO
{
    [NetSerializable]
    internal partial class RelayDestination
    {
        [NetProperty(0)]
        public uint HostId { get; set; }

        [NetProperty(1)]
        public uint FrameNumber { get; set; }
    }
}
