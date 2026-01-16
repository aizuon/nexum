using System.Collections.Concurrent;

namespace Nexum.Client
{
    public class P2PGroup
    {
        public uint HostId { get; internal set; }

        internal ConcurrentDictionary<uint, P2PMember> P2PMembers { get; } =
            new ConcurrentDictionary<uint, P2PMember>();
    }
}
