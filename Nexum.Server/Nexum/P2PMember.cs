using System;
using System.Collections.Concurrent;

namespace Nexum.Server
{
    public class P2PMember
    {
        private static readonly Random Random = new Random();

        internal P2PMember(uint groupId, NetSession session)
        {
            GroupId = groupId;
            Session = session;
            P2PFirstFrameNumber = (uint)Random.Next(1, int.MaxValue);
        }

        public Guid ConnectionMagicNumber { get; } = Guid.NewGuid();

        public uint GroupId { get; }

        public NetSession Session { get; }

        public uint P2PFirstFrameNumber { get; }

        internal ConcurrentDictionary<uint, P2PConnectionState> ConnectionStates { get; } =
            new ConcurrentDictionary<uint, P2PConnectionState>();
    }
}
