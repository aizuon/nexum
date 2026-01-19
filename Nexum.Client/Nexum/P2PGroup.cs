using System.Collections.Concurrent;
using System.Net;
using Nexum.Core;

namespace Nexum.Client
{
    public class P2PGroup
    {
        public uint HostId { get; internal set; }

        internal ConcurrentDictionary<uint, P2PMember> P2PMembers { get; } =
            new ConcurrentDictionary<uint, P2PMember>();

        internal P2PMember FindMember(uint clientHostId, IPEndPoint udpEndPoint = null, ushort filterTag = 0,
            uint relayFrom = 0)
        {
            if (relayFrom != 0 && P2PMembers.TryGetValue(relayFrom, out var relayMember))
                return relayMember;

            foreach (var member in P2PMembers.Values)
            {
                if (udpEndPoint != null)
                {
                    if (member.PeerLocalToRemoteSocket != null &&
                        member.PeerLocalToRemoteSocket.Equals(udpEndPoint))
                        return member;

                    if (member.PeerRemoteToLocalSocket != null &&
                        member.PeerRemoteToLocalSocket.Equals(udpEndPoint))
                        return member;
                }

                if (filterTag != 0 && FilterTag.Create(member.HostId, clientHostId) == filterTag)
                    return member;
            }

            return null;
        }
    }
}
