using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using BaseLib.Extensions;
using Nexum.Core;

namespace Nexum.Server
{
    internal static class NetServerHandler
    {
        internal static void ReadFrame(NetServer server, NetSession session, NetMessage message,
            IPEndPoint udpEndPoint = null, bool bypass = false)
        {
            lock (session.RecvLock)
            {
                if (bypass)
                {
                    ReadMessage(server, session, message, udpEndPoint);
                    return;
                }

                var packet = new ByteArray();
                if (!message.Read(ref packet))
                    return;

                var innerMessage = new NetMessage(packet);
                ReadMessage(server, session, innerMessage, udpEndPoint);
            }
        }

        internal static void ReadMessage(NetServer server, NetSession session, NetMessage message,
            IPEndPoint udpEndPoint = null)
        {
            message.WriteOffset = message.Length;

            var messageType = MessageType.None;
            if (!message.Read(ref messageType))
                return;

            if (udpEndPoint != null)
                message.Reliable = false;
            else
                message.Reliable = true;

            switch (messageType)
            {
                case MessageType.RMI:
                    RMIHandler(server, session, message);
                    break;

                case MessageType.Encrypted:
                    EncryptedHandler(server, session, message);
                    break;

                case MessageType.Compressed:
                    CompressedHandler(server, session, message);
                    break;

                case MessageType.NotifyCSEncryptedSessionKey:
                    NotifyCSEncryptedSessionKeyHandler(server, session, message);
                    break;

                case MessageType.NotifyServerConnectionRequestData:
                    NotifyServerConnectionRequestDataHandler(server, session, message);
                    break;

                case MessageType.UnreliablePing:
                    message.Reliable = false;
                    UnreliablePingHandler(server, session, message, udpEndPoint);
                    break;

                case MessageType.ReliableRelay1:
                    message.Reliable = true;
                    ReliableRelay1Handler(server, session, message);
                    break;

                case MessageType.UnreliableRelay1:
                    message.Reliable = false;
                    UnreliableRelay1Handler(server, session, message);
                    break;

                case MessageType.ServerHolepunch:
                    ServerHolepunchHandler(server, session, message);
                    break;

                case MessageType.NotifyHolepunchSuccess:
                    NotifyHolepunchSuccessHandler(server, session, message);
                    break;

                case MessageType.PeerUdp_ServerHolepunch:
                    PeerUdpServerHolepunchHandler(server, session, message, udpEndPoint);
                    break;

                case MessageType.PeerUdp_NotifyHolepunchSuccess:
                    PeerUdpNotifyHolepunchSuccessHandler(server, session, message);
                    break;

                case MessageType.ReliableUdp_Frame:
                    ReliableUdpFrameHandler(server, session, message, udpEndPoint);
                    break;

                default:
                    session.Logger.Warning("Received unknown Core ID {MessageType}", messageType);
                    break;
            }
        }

        private static void ReliableUdpFrameHandler(NetServer server, NetSession session, NetMessage message,
            IPEndPoint udpEndPoint)
        {
            if (!ReliableUdpHelper.ParseFrame(message, out var frame))
                return;

            if (session.ToClientReliableUdp != null)
            {
                session.ToClientReliableUdp.TakeReceivedFrame(frame);

                ExtractMessagesFromReliableUdpStream(server, session, udpEndPoint);
            }
            else
            {
                if (frame.Type == ReliableUdpFrameType.Data && frame.Data != null)
                    if (ReliableUdpHelper.UnwrapPayload(frame.Data, out byte[] payload))
                    {
                        var innerMessage = new NetMessage(payload, true);
                        ReadMessage(server, session, innerMessage, udpEndPoint);
                    }
            }
        }

        private static void ExtractMessagesFromReliableUdpStream(NetServer server, NetSession session,
            IPEndPoint udpEndPoint)
        {
            var stream = session.ToClientReliableUdp?.ReceivedStream;
            if (stream == null || stream.Length == 0)
                return;

            while (stream.Length > 0)
            {
                byte[] streamData = stream.PeekAll();
                var tempMsg = new NetMessage(streamData, true);

                if (!tempMsg.Read(out ushort magic) || magic != Constants.TcpSplitter)
                    break;

                var streamPayload = new ByteArray();
                if (!tempMsg.Read(ref streamPayload))
                    break;

                int consumedBytes = tempMsg.ReadOffset;
                stream.PopFront(consumedBytes);

                var innerMessage = new NetMessage(streamPayload);
                ReadMessage(server, session, innerMessage, udpEndPoint);
            }
        }

        private static void NotifyCSEncryptedSessionKeyHandler(NetServer server, NetSession session, NetMessage message)
        {
            session.Logger.Debug("NotifyCSEncryptedSessionKey");

            var encryptedSessionKey = new ByteArray();
            var encryptedFastSessionKey = new ByteArray();

            if (!message.Read(ref encryptedSessionKey) || !message.Read(ref encryptedFastSessionKey))
                return;

            byte[] decryptedSessionKey = server.RSA.Decrypt(encryptedSessionKey.GetBuffer(), true);

            session.Crypt = new NetCrypt(decryptedSessionKey);
            session.Crypt.InitializeFastEncryption(
                session.Crypt.DecryptKey(encryptedFastSessionKey.GetBuffer())
            );

            var notifyCSSessionKeySuccess = new NetMessage();
            notifyCSSessionKeySuccess.WriteEnum(MessageType.NotifyCSSessionKeySuccess);

            session.NexumToClient(notifyCSSessionKeySuccess);
        }

        private static void NotifyServerConnectionRequestDataHandler(NetServer server, NetSession session,
            NetMessage message)
        {
            session.Logger.Debug("NotifyServerConnectionRequestData");

            var connectionRequestPayload = new ByteArray();
            if (!message.Read(ref connectionRequestPayload) || !message.Read(out Guid version) ||
                !message.Read(out uint netVersion))
                return;

            if (netVersion != Constants.NetVersion)
            {
                session.Logger.Warning(
                    "Protocol version mismatch => client={ClientVersion}, server={ServerVersion}",
                    netVersion, Constants.NetVersion);

                var notifyProtocolVersionMismatch = new NetMessage();
                notifyProtocolVersionMismatch.WriteEnum(MessageType.NotifyProtocolVersionMismatch);

                session.NexumToClient(notifyProtocolVersionMismatch);
                return;
            }

            var notifyServerConnectSuccess = new NetMessage();
            notifyServerConnectSuccess.WriteEnum(MessageType.NotifyServerConnectSuccess);
            notifyServerConnectSuccess.Write(session.HostId);
            notifyServerConnectSuccess.Write(server.ServerGuid);
            notifyServerConnectSuccess.Write(new ByteArray());
            notifyServerConnectSuccess.Write(session.RemoteEndPoint);

            session.SetConnectionState(ConnectionState.Connected);
            session.NexumToClient(notifyServerConnectSuccess);
        }

        private static void UnreliablePingHandler(NetServer server, NetSession session, NetMessage message,
            IPEndPoint udpEndPoint)
        {
            if (!message.Read(out double clientTime) || !message.Read(out double clientRecentPing))
                return;

            message.Read(out int paddingSize);

            if (udpEndPoint != null)
                session.LastUdpPing = DateTimeOffset.Now;

            if (clientRecentPing > 0)
            {
                if (session.ClientUdpLastPing > 0)
                    session.ClientUdpJitter = NetUtil.CalculateJitter(session.ClientUdpJitter, clientRecentPing,
                        session.ClientUdpLastPing);

                session.ClientUdpLastPing = clientRecentPing;
                session.ClientUdpRecentPing = session.ClientUdpRecentPing != 0.0
                    ? SysUtil.Lerp(session.ClientUdpRecentPing, clientRecentPing,
                        ReliableUdpConfig.LagLinearProgrammingFactor)
                    : clientRecentPing;
            }

            var unreliablePong = new NetMessage();
            unreliablePong.WriteEnum(MessageType.UnreliablePong);
            unreliablePong.Write(clientTime);
            unreliablePong.Write(session.GetAbsoluteTime());

            unreliablePong.Write(paddingSize);
            if (paddingSize > 0)
                unreliablePong.WriteZeroes(paddingSize);

            session.NexumToClientUdpIfAvailable(unreliablePong);
        }

        private static void ReliableRelay1Handler(NetServer server, NetSession session, NetMessage message)
        {
            long destinationCount = 0;
            if (!message.ReadScalar(ref destinationCount))
                return;

            if (destinationCount <= 0 || destinationCount > 1024)
                return;

            var destinations = new Dictionary<uint, uint>();
            for (int i = 0; i < destinationCount; i++)
            {
                if (!message.Read(out uint hostId) || !message.Read(out uint frameNumber))
                    return;

                destinations[hostId] = frameNumber;
            }

            var relayPayload = new ByteArray();
            if (!message.Read(ref relayPayload))
                return;

            var group = session.P2PGroup;
            if (group == null)
                return;

            foreach ((uint hostId, uint frameNumber) in destinations)
                if (group.P2PMembersInternal.TryGetValue(hostId, out var p2pMember))
                {
                    var reliableRelay2 = new NetMessage();
                    reliableRelay2.WriteEnum(MessageType.ReliableRelay2);
                    reliableRelay2.Write(session.HostId);
                    reliableRelay2.Write(frameNumber);
                    reliableRelay2.Write(relayPayload);
                    p2pMember.Session.NexumToClient(reliableRelay2);
                }
        }

        private static void UnreliableRelay1Handler(NetServer server, NetSession session, NetMessage message)
        {
            if (!message.Read(out byte priority))
                return;

            long uniqueId = 0;
            if (!message.ReadScalar(ref uniqueId))
                return;

            long destinationCount = 0;
            if (!message.ReadScalar(ref destinationCount))
                return;

            if (destinationCount <= 0 || destinationCount > 1024)
                return;

            uint[] destinationHostIds = ArrayPool<uint>.Shared.Rent((int)destinationCount);
            try
            {
                for (int i = 0; i < destinationCount; i++)
                {
                    if (!message.Read(out uint hostId))
                        return;

                    destinationHostIds[i] = hostId;
                }

                var relayPayload = new ByteArray();
                if (!message.Read(ref relayPayload))
                    return;

                var unreliableRelay2 = new NetMessage();
                unreliableRelay2.WriteEnum(MessageType.UnreliableRelay2);
                unreliableRelay2.Write(session.HostId);
                unreliableRelay2.Write(relayPayload);
                var group = session.P2PGroup;
                if (group == null)
                    return;

                for (int i = 0; i < destinationCount; i++)
                    if (group.P2PMembersInternal.TryGetValue(destinationHostIds[i], out var p2pMember))
                        p2pMember.Session.NexumToClientUdpIfAvailable(unreliableRelay2);
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(destinationHostIds);
            }
        }

        private static void ServerHolepunchHandler(NetServer server, NetSession session, NetMessage message)
        {
            if (!server.UdpEnabled)
                return;

            if (!message.Read(out Guid magicNumber))
                return;

            Guid capturedMagicNumber;
            IPEndPoint capturedUdpEndPoint;

            lock (session.UdpHolepunchLock)
            {
                if (!magicNumber.Equals(session.HolepunchMagicNumber))
                {
                    session.Logger.Warning(
                        "ServerHolepunch => magic number mismatch, expected {Expected}, got {Actual}",
                        session.HolepunchMagicNumber, magicNumber);
                    return;
                }

                if (session.UdpEnabled)
                {
                    session.Logger.Debug("ServerHolepunch => UDP already enabled, ignoring duplicate");
                    return;
                }

                capturedMagicNumber = session.HolepunchMagicNumber;
                capturedUdpEndPoint = session.UdpEndPoint;
            }

            session.Logger.Debug("ServerHolepunch => guid = {MagicNumber}", magicNumber);

            session.NexumToClientUdpIfAvailable(
                HolepunchHelper.CreateServerHolepunchAckMessage(capturedMagicNumber, capturedUdpEndPoint),
                true);
            HolepunchHelper.SendBurstMessagesWithCheck(
                () => HolepunchHelper.CreateServerHolepunchAckMessage(capturedMagicNumber, capturedUdpEndPoint),
                msg => session.NexumToClientUdpIfAvailable(msg, true),
                () => !session.UdpEnabled
            );
        }

        private static void NotifyHolepunchSuccessHandler(NetServer server, NetSession session, NetMessage message)
        {
            if (!server.UdpEnabled)
                return;

            if (!message.Read(out Guid magicNumber))
                return;

            var localUdpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
            var publicUdpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);

            if (!message.ReadIPEndPoint(ref localUdpSocket) || !message.ReadIPEndPoint(ref publicUdpSocket))
                return;

            Guid capturedMagicNumber;

            lock (session.UdpHolepunchLock)
            {
                if (!magicNumber.Equals(session.HolepunchMagicNumber))
                {
                    session.Logger.Warning(
                        "NotifyHolepunchSuccess => magic number mismatch, expected {Expected}, got {Actual}",
                        session.HolepunchMagicNumber, magicNumber);
                    return;
                }

                if (session.UdpEnabled)
                {
                    session.Logger.Debug("NotifyHolepunchSuccess => UDP already enabled, ignoring duplicate");
                    return;
                }

                session.Logger.Debug(
                    "NotifyHolepunchSuccess => guid = {MagicNumber}, localEndpoint = {LocalEndpoint}, publicEndpoint = {PublicEndpoint}",
                    magicNumber, localUdpSocket, publicUdpSocket);

                session.UdpEnabled = true;
                session.LastUdpPing = DateTimeOffset.Now;
                session.UdpLocalEndPointInternal = localUdpSocket;

                capturedMagicNumber = session.HolepunchMagicNumber;

                if (session.P2PGroup != null &&
                    session.P2PGroup.P2PMembersInternal.TryGetValue(session.HostId, out var member))
                    session.InitializeToClientReliableUdp(member.P2PFirstFrameNumber);

                server.StartReliableUdpLoop();
            }

            session.NexumToClientUdpIfAvailable(
                HolepunchHelper.CreateNotifyClientServerUdpMatchedMessage(capturedMagicNumber), true);
            HolepunchHelper.SendBurstMessages(
                () => HolepunchHelper.CreateNotifyClientServerUdpMatchedMessage(capturedMagicNumber),
                msg => session.NexumToClientUdpIfAvailable(msg, true),
                HolepunchConfig.UdpMatchedDelayMs
            );

            ProcessPendingPeerHolepunchRequests(session);

            server.InitiateP2PConnections(session);
        }

        private static void ProcessPendingPeerHolepunchRequests(NetSession session)
        {
            while (session.PendingPeerHolepunchRequests.TryDequeue(out var request))
            {
                if (request.SenderSession.IsDisposed)
                    continue;

                lock (request.SenderSession.UdpHolepunchLock)
                {
                    if (!request.SenderSession.UdpEnabled)
                        continue;
                }

                if (request.SenderSession.UdpSocket?.Channel == null)
                    continue;

                var senderUdpEndpoint = request.SenderSession.UdpEndPoint;
                if (senderUdpEndpoint == null)
                    continue;

                session.Logger.Debug(
                    "ProcessPendingPeerHolepunchRequests => processing queued request from hostId = {SenderHostId}, magicNumber = {MagicNumber}",
                    request.SenderSession.HostId,
                    request.MagicNumber);

                request.SenderSession.NexumToClient(
                    HolepunchHelper.CreatePeerUdpServerHolepunchAckMessage(request.MagicNumber, senderUdpEndpoint,
                        session.HostId));
                HolepunchHelper.SendBurstMessages(
                    () => HolepunchHelper.CreatePeerUdpServerHolepunchAckMessage(request.MagicNumber, senderUdpEndpoint,
                        session.HostId),
                    msg => request.SenderSession.NexumToClient(msg)
                );
            }
        }

        private static void PeerUdpServerHolepunchHandler(NetServer server, NetSession session, NetMessage message,
            IPEndPoint udpEndPoint)
        {
            if (!server.UdpEnabled)
                return;

            if (!message.Read(out Guid magicNumber) || !message.Read(out uint hostId))
                return;

            var group = session.P2PGroup;
            if (group == null)
            {
                session.Logger.Warning("PeerUdpServerHolepunch => session has no P2P group");
                return;
            }

            if (!group.P2PMembersInternal.TryGetValue(hostId, out var targetPeer))
            {
                session.Logger.Warning("PeerUdpServerHolepunch => target peer not found for hostId = {HostId}", hostId);
                return;
            }

            lock (session.UdpHolepunchLock)
            {
                if (!session.UdpEnabled)
                {
                    session.Logger.Debug("PeerUdpServerHolepunch => session UDP not enabled, cannot process");
                    return;
                }
            }

            session.Logger.Debug(
                "PeerUdpServerHolepunch => guid = {MagicNumber}, targetHostId = {TargetHostId}",
                magicNumber,
                hostId);

            lock (targetPeer.Session.UdpHolepunchLock)
            {
                if (!targetPeer.Session.UdpEnabled)
                {
                    session.Logger.Debug(
                        "PeerUdpServerHolepunch => guid = {MagicNumber}, targetHostId = {TargetHostId} - target UDP not ready, queueing",
                        magicNumber,
                        hostId);
                    targetPeer.Session.PendingPeerHolepunchRequests.Enqueue(
                        new PendingPeerHolepunchRequest(session, magicNumber));
                    return;
                }
            }

            session.NexumToClient(
                HolepunchHelper.CreatePeerUdpServerHolepunchAckMessage(magicNumber, udpEndPoint,
                    targetPeer.Session.HostId));
            HolepunchHelper.SendBurstMessages(
                () => HolepunchHelper.CreatePeerUdpServerHolepunchAckMessage(magicNumber, udpEndPoint,
                    targetPeer.Session.HostId),
                msg => session.NexumToClient(msg)
            );
        }

        private static void PeerUdpNotifyHolepunchSuccessHandler(NetServer server, NetSession session,
            NetMessage message)
        {
            if (!server.UdpEnabled)
                return;

            var localUdpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
            var publicUdpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);

            if (!message.ReadIPEndPoint(ref localUdpSocket) || !message.ReadIPEndPoint(ref publicUdpSocket))
                return;

            if (!message.Read(out uint hostId))
                return;

            var group = session.P2PGroup;
            if (group == null)
            {
                session.Logger.Warning("PeerUdpNotifyHolepunchSuccess => session has no P2P group");
                return;
            }

            if (!group.P2PMembersInternal.TryGetValue(session.HostId, out var peer) ||
                !peer.ConnectionStates.TryGetValue(hostId, out var state) ||
                !state.RemotePeer.ConnectionStates.TryGetValue(session.HostId, out var remoteState))
            {
                session.Logger.Warning(
                    "PeerUdpNotifyHolepunchSuccess => connection state not found for hostId = {HostId}", hostId);
                return;
            }

            session.Logger.Debug(
                "PeerUdpNotifyHolepunchSuccess => localEndpoint = {LocalEndpoint}, publicEndpoint = {PublicEndpoint}, targetHostId = {TargetHostId}",
                localUdpSocket, publicUdpSocket, hostId);

            bool shouldSendRequestP2PHolepunch = HolepunchHelper.WithOrderedLocks(
                session.HostId, hostId, state.StateLock, remoteState.StateLock, () =>
                {
                    if (state.PeerUdpHolepunchSuccess)
                        return false;

                    state.PeerUdpHolepunchSuccess = true;
                    state.LocalEndPoint = localUdpSocket;
                    state.EndPoint = publicUdpSocket;
                    return remoteState.PeerUdpHolepunchSuccess;
                });

            if (!shouldSendRequestP2PHolepunch)
                return;

            var requestP2PHolepunchA = new NetMessage();
            requestP2PHolepunchA.Write(hostId);
            requestP2PHolepunchA.Write(remoteState.LocalEndPoint);
            requestP2PHolepunchA.Write(remoteState.EndPoint);
            peer.Session.RmiToClient((ushort)NexumOpCode.RequestP2PHolepunch, requestP2PHolepunchA);

            var requestP2PHolepunchB = new NetMessage();
            requestP2PHolepunchB.Write(session.HostId);
            requestP2PHolepunchB.Write(state.LocalEndPoint);
            requestP2PHolepunchB.Write(state.EndPoint);
            state.RemotePeer.Session.RmiToClient((ushort)NexumOpCode.RequestP2PHolepunch, requestP2PHolepunchB);
        }

        private static void RMIHandler(NetServer server, NetSession session, NetMessage message)
        {
            ushort rmiId = 0;
            if (!message.Read(ref rmiId))
                return;

            switch ((NexumOpCode)rmiId)
            {
                case NexumOpCode.ReliablePing:
                    session.RmiToClient((ushort)NexumOpCode.ReliablePong, new NetMessage());
                    break;

                case NexumOpCode.P2P_NotifyDirectP2PDisconnected:
                {
                    if (!message.Read(out uint hostId) || !message.Read(out uint reasonValue))
                        return;

                    var reason = (ErrorType)reasonValue;
                    var group = session.P2PGroup;
                    if (group == null)
                    {
                        session.Logger.Warning("P2P_NotifyDirectP2PDisconnected => session has no P2P group");
                        return;
                    }

                    if (!group.P2PMembersInternal.TryGetValue(session.HostId, out var peer) ||
                        !peer.ConnectionStates.TryGetValue(hostId, out var stateA) ||
                        !stateA.RemotePeer.ConnectionStates.TryGetValue(session.HostId, out var stateB))
                    {
                        session.Logger.Warning(
                            "P2P_NotifyDirectP2PDisconnected => connection state not found for hostId = {HostId}",
                            hostId);
                        break;
                    }

                    session.Logger.Debug("P2P_NotifyDirectP2PDisconnected => hostId = {HostId}, reason = {Reason}",
                        hostId, reason);

                    bool shouldNotify = HolepunchHelper.WithOrderedLocks(
                        session.HostId, hostId, stateA.StateLock, stateB.StateLock, () =>
                        {
                            bool notify = stateA.HolepunchSuccess;
                            stateA.HolepunchSuccess = stateB.HolepunchSuccess = false;
                            stateA.PeerUdpHolepunchSuccess = stateB.PeerUdpHolepunchSuccess = false;
                            stateA.JitTriggered = stateB.JitTriggered = false;
                            stateA.NewConnectionSent = stateB.NewConnectionSent = false;
                            stateA.EstablishSent = stateB.EstablishSent = false;
                            stateA.RetryCount = stateB.RetryCount = 0;
                            var now = DateTime.UtcNow;
                            stateA.LastHolepunch = stateB.LastHolepunch = now;
                            return notify;
                        });

                    if (shouldNotify)
                    {
                        var notifyDisconnected = new NetMessage();
                        notifyDisconnected.Write(session.HostId);
                        notifyDisconnected.WriteEnum(reason);
                        stateA.RemotePeer.Session.RmiToClient((ushort)NexumOpCode.P2P_NotifyDirectP2PDisconnected2,
                            notifyDisconnected);
                    }

                    break;
                }

                case NexumOpCode.NotifyUdpToTcpFallbackByClient:
                    session.Logger.Debug("NotifyUdpToTcpFallbackByClient => falling back to TCP relay");
                    lock (session.UdpHolepunchLock)
                    {
                        session.ResetUdp();
                    }

                    server.UdpSessions.TryRemove(FilterTag.Create(session.HostId, (uint)HostId.Server), out _);
                    break;

                case NexumOpCode.P2PGroup_MemberJoin_Ack:
                {
                    if (!message.Read(out uint groupHostId) ||
                        !message.Read(out uint addedMemberHostId) ||
                        !message.Read(out uint eventId) ||
                        !message.Read(out bool localPortReuseSuccess))
                        return;

                    if (session.HostId == addedMemberHostId)
                        return;
                    var group = session.P2PGroup;
                    if (group == null)
                    {
                        session.Logger.Warning("P2PGroup_MemberJoin_Ack => session has no P2P group");
                        return;
                    }

                    if (group.HostId != groupHostId)
                    {
                        session.Logger.Warning(
                            "P2PGroup_MemberJoin_Ack => group hostId mismatch, expected {Expected}, got {Actual}",
                            group.HostId, groupHostId);
                        return;
                    }

                    session.Logger.Debug(
                        "P2PGroup_MemberJoin_Ack => groupHostId = {GroupHostId}, addedMemberHostId = {AddedMemberHostId}, eventId = {EventId}",
                        groupHostId, addedMemberHostId, eventId);

                    if (group.P2PMembersInternal.TryGetValue(session.HostId, out var peer) &&
                        peer.ConnectionStates.TryGetValue(addedMemberHostId, out var stateA) &&
                        stateA.EventId == eventId &&
                        stateA.RemotePeer.ConnectionStates.ContainsKey(session.HostId))
                    {
                        stateA.IsJoined = true;
                        server.InitiateP2PConnections(session);
                        server.InitiateP2PConnections(stateA.RemotePeer.Session);
                    }

                    break;
                }

                case NexumOpCode.NotifyP2PHolepunchSuccess:
                {
                    var endpointA = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
                    var endpointB = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
                    var endpointC = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
                    var endpointD = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);

                    if (!message.Read(out uint hostIdA) ||
                        !message.Read(out uint hostIdB) ||
                        !message.ReadIPEndPoint(ref endpointA) ||
                        !message.ReadIPEndPoint(ref endpointB) ||
                        !message.ReadIPEndPoint(ref endpointC) ||
                        !message.ReadIPEndPoint(ref endpointD))
                        return;

                    var group = session.P2PGroup;
                    if (group == null)
                    {
                        session.Logger.Warning("NotifyP2PHolepunchSuccess => session has no P2P group");
                        return;
                    }

                    if (session.HostId != hostIdA && session.HostId != hostIdB)
                    {
                        session.Logger.Warning(
                            "NotifyP2PHolepunchSuccess => session hostId {SessionHostId} doesn't match hostIdA {HostIdA} or hostIdB {HostIdB}",
                            session.HostId, hostIdA, hostIdB);
                        return;
                    }

                    if (!group.P2PMembersInternal.TryGetValue(hostIdA, out var peerA) ||
                        !group.P2PMembersInternal.TryGetValue(hostIdB, out var peerB) ||
                        !peerA.ConnectionStates.TryGetValue(peerB.Session.HostId, out var stateA) ||
                        !peerB.ConnectionStates.TryGetValue(peerA.Session.HostId, out var stateB))
                    {
                        session.Logger.Warning(
                            "NotifyP2PHolepunchSuccess => connection state not found for hostIdA = {HostIdA}, hostIdB = {HostIdB}",
                            hostIdA, hostIdB);
                        break;
                    }

                    session.Logger.Debug(
                        "NotifyP2PHolepunchSuccess => hostIdA = {HostIdA}, hostIdB = {HostIdB}, endpointA = {EndpointA}, endpointB = {EndpointB}, endpointC = {EndpointC}, endpointD = {EndpointD}",
                        hostIdA, hostIdB, endpointA, endpointB, endpointC, endpointD);

                    bool shouldSendEstablish = HolepunchHelper.WithOrderedLocks(
                        hostIdA, hostIdB, stateA.StateLock, stateB.StateLock, () =>
                        {
                            if (session.HostId == peerA.Session.HostId)
                                stateA.HolepunchSuccess = true;
                            else
                                stateB.HolepunchSuccess = true;

                            bool canSend = (stateA.HolepunchSuccess || stateB.HolepunchSuccess) &&
                                           !stateA.EstablishSent;
                            if (canSend)
                            {
                                stateA.EstablishSent = true;
                                stateB.EstablishSent = true;
                            }

                            return canSend;
                        });

                    if (shouldSendEstablish)
                    {
                        var notifyEstablished = new NetMessage();
                        notifyEstablished.Write(hostIdA);
                        notifyEstablished.Write(hostIdB);
                        notifyEstablished.Write(endpointA);
                        notifyEstablished.Write(endpointB);
                        notifyEstablished.Write(endpointC);
                        notifyEstablished.Write(endpointD);

                        peerA.Session.RmiToClient((ushort)NexumOpCode.NotifyDirectP2PEstablish, notifyEstablished);
                        peerB.Session.RmiToClient((ushort)NexumOpCode.NotifyDirectP2PEstablish, notifyEstablished);
                    }

                    break;
                }

                case NexumOpCode.NotifyJitDirectP2PTriggered:
                {
                    if (!message.Read(out uint targetHostId))
                        return;

                    var group = session.P2PGroup;
                    if (group == null)
                    {
                        session.Logger.Warning("NotifyJitDirectP2PTriggered => session has no P2P group");
                        return;
                    }

                    if (!group.P2PMembersInternal.TryGetValue(session.HostId, out var peerA) ||
                        !group.P2PMembersInternal.TryGetValue(targetHostId, out var peerB) ||
                        !peerA.ConnectionStates.TryGetValue(peerB.Session.HostId, out var stateA) ||
                        !peerB.ConnectionStates.TryGetValue(peerA.Session.HostId, out var stateB))
                    {
                        session.Logger.Warning(
                            "NotifyJitDirectP2PTriggered => connection state not found for targetHostId = {TargetHostId}",
                            targetHostId);
                        break;
                    }

                    session.Logger.Debug("NotifyJitDirectP2PTriggered => targetHostId = {TargetHostId}",
                        targetHostId);

                    bool shouldSendNewConnection = HolepunchHelper.WithOrderedLocks(
                        session.HostId, targetHostId, stateA.StateLock, stateB.StateLock, () =>
                        {
                            if (session.HostId == peerA.Session.HostId)
                                stateA.JitTriggered = true;
                            else
                                stateB.JitTriggered = true;

                            bool canSend = (stateA.JitTriggered || stateB.JitTriggered) &&
                                           !stateA.NewConnectionSent;
                            if (canSend)
                            {
                                stateA.NewConnectionSent = true;
                                stateB.NewConnectionSent = true;
                            }

                            return canSend;
                        });

                    if (shouldSendNewConnection)
                    {
                        var newConnectionA = new NetMessage();
                        newConnectionA.Write(peerB.Session.HostId);
                        peerA.Session.RmiToClient((ushort)NexumOpCode.NewDirectP2PConnection, newConnectionA);

                        var newConnectionB = new NetMessage();
                        newConnectionB.Write(peerA.Session.HostId);
                        peerB.Session.RmiToClient((ushort)NexumOpCode.NewDirectP2PConnection, newConnectionB);
                    }

                    break;
                }

                case NexumOpCode.C2S_RequestCreateUdpSocket:
                {
                    if (!server.UdpEnabled)
                    {
                        session.Logger.Debug("C2S_RequestCreateUdpSocket => UDP not enabled on server");
                        return;
                    }

                    session.Logger.Debug("C2S_RequestCreateUdpSocket => hostId = {HostId}", session.HostId);

                    server.MagicNumberSessions.TryRemove(session.HolepunchMagicNumber);

                    lock (session.UdpHolepunchLock)
                    {
                        if (session.UdpSessionInitialized)
                        {
                            session.Logger.Debug(
                                "C2S_RequestCreateUdpSocket => closing existing UDP connection");
                            server.UdpSessions.TryRemove(FilterTag.Create(session.HostId, (uint)HostId.Server),
                                out _);
                        }

                        session.ResetUdp();
                    }

                    var udpSocket = server.GetRandomUdpSocket();

                    session.UdpSocket = udpSocket;
                    session.HolepunchMagicNumber = Guid.NewGuid();
                    server.MagicNumberSessions.TryAdd(session.HolepunchMagicNumber, session);

                    var udpEndpoint =
                        new IPEndPoint(server.IPAddress, ((IPEndPoint)udpSocket.Channel.LocalAddress).Port);
                    session.Logger.Debug(
                        "C2S_RequestCreateUdpSocket => assigned UDP endpoint {UdpEndpoint}, guid = {MagicNumber}",
                        udpEndpoint, session.HolepunchMagicNumber);

                    var createUdpAck = new NetMessage();
                    createUdpAck.Write(true);
                    createUdpAck.WriteStringEndPoint(udpEndpoint);

                    session.RmiToClient((ushort)NexumOpCode.S2C_CreateUdpSocketAck, createUdpAck);
                    break;
                }

                case NexumOpCode.C2S_CreateUdpSocketAck:
                {
                    if (!server.UdpEnabled)
                    {
                        session.Logger.Debug("C2S_CreateUdpSocketAck => UDP not enabled on server");
                        return;
                    }

                    session.Logger.Debug("C2S_CreateUdpSocketAck => hostId = {HostId}", session.HostId);
                    session.Logger.Debug("C2S_CreateUdpSocketAck => requesting holepunch with guid = {MagicNumber}",
                        session.HolepunchMagicNumber);

                    var requestStartHolepunch = new NetMessage();
                    requestStartHolepunch.WriteEnum(MessageType.RequestStartServerHolepunch);
                    requestStartHolepunch.Write(session.HolepunchMagicNumber);

                    session.NexumToClient(requestStartHolepunch);
                    break;
                }

                case NexumOpCode.ShutdownTcp:
                    session.Logger.Debug("ShutdownTcp");
                    session.RmiToClient((ushort)NexumOpCode.ShutdownTcpAck, new NetMessage());
                    break;

                case NexumOpCode.ShutdownTcpHandshake:
                    session.Logger.Debug("ShutdownTcpHandshake");
                    session.Dispose();
                    break;

                default:
                    server.OnRMIReceive(session, message, rmiId);
                    break;
            }
        }

        private static void EncryptedHandler(NetServer server, NetSession session, NetMessage message)
        {
            NetCoreHandler.HandleEncrypted(
                message,
                session.Crypt,
                decryptedMsg => ReadMessage(server, session, decryptedMsg)
            );
        }

        private static void CompressedHandler(NetServer server, NetSession session, NetMessage message)
        {
            NetCoreHandler.HandleCompressed(
                message,
                session.Logger,
                decompressedMsg => ReadMessage(server, session, decompressedMsg)
            );
        }
    }
}
