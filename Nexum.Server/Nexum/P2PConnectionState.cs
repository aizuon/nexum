using System;
using System.Net;

namespace Nexum.Server
{
    internal readonly struct PendingPeerHolepunchRequest
    {
        internal PendingPeerHolepunchRequest(NetSession senderSession, Guid magicNumber)
        {
            SenderSession = senderSession;
            MagicNumber = magicNumber;
        }

        internal NetSession SenderSession { get; }
        internal Guid MagicNumber { get; }
    }

    internal sealed class P2PConnectionState
    {
        internal P2PConnectionState(P2PMember remotePeer)
        {
            RemotePeer = remotePeer;
            EventId = (uint)Guid.NewGuid().GetHashCode();
        }

        internal object StateLock { get; } = new object();

        internal P2PMember RemotePeer { get; }

        internal uint EventId { get; }

        internal bool IsInitialized { get; set; }
        internal bool IsJoined { get; set; }
        internal bool LocalPortReuseSuccess { get; set; }
        internal bool JitTriggered { get; set; }
        internal bool PeerUdpHolepunchSuccess { get; set; }
        internal bool HolepunchSuccess { get; set; }
        internal bool NewConnectionSent { get; set; }
        internal bool EstablishSent { get; set; }

        internal DateTimeOffset LastHolepunch { get; set; }
        internal uint RetryCount { get; set; }

        internal IPEndPoint EndPoint { get; set; }
        internal IPEndPoint LocalEndPoint { get; set; }

        internal int LastSuccessfulLocalPort { get; set; }
    }
}
