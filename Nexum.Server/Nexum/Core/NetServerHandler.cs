using System;
using System.Net;
using System.Security.Cryptography;
using BaseLib.Extensions;
using Nexum.Core;
using Nexum.Core.Configuration;
using Nexum.Core.Crypto;
using Nexum.Core.Holepunching;
using Nexum.Core.Message.C2S;
using Nexum.Core.Message.S2C;
using Nexum.Core.Message.X2X;
using Nexum.Core.ReliableUdp;
using Nexum.Core.Rmi.C2S;
using Nexum.Core.Rmi.S2C;
using Nexum.Core.Routing;
using Nexum.Core.Serialization;
using Nexum.Core.Utilities;
using Nexum.Server.P2P;
using Nexum.Server.Sessions;

namespace Nexum.Server.Core
{
    internal static class NetServerHandler
    {
        internal static void ReadFrame(NetServer server, NetSession session, NetMessage message,
            IPEndPoint udpEndPoint = null, bool bypass = false)
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

        internal static void ReadMessage(NetServer server, NetSession session, NetMessage message,
            IPEndPoint udpEndPoint = null)
        {
            message.WriteOffset = message.Length;

            if (!message.Read<MessageType>(out var messageType))
                return;

            if (udpEndPoint != null)
                message.Reliable = false;
            else
                message.Reliable = true;

            switch (messageType)
            {
                case MessageType.Rmi:
                    RmiHandler(server, session, message);
                    break;

                case MessageType.Encrypted:
                    EncryptedHandler(server, session, message, udpEndPoint);
                    break;

                case MessageType.Compressed:
                    CompressedHandler(server, session, message, udpEndPoint);
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

            if (!NotifyCSEncryptedSessionKey.Deserialize(message, out var packet))
                return;

            byte[] decryptedSessionKey =
                server.RSA.Decrypt(packet.EncryptedSessionKey.GetBuffer(), RSAEncryptionPadding.OaepSHA256);

            session.Crypt = new NetCrypt(decryptedSessionKey);
            session.Crypt.InitializeFastEncryption(
                session.Crypt.DecryptKey(packet.EncryptedFastSessionKey.GetBuffer())
            );

            session.NexumToClient(new NotifyCSSessionKeySuccess());
        }

        private static void NotifyServerConnectionRequestDataHandler(NetServer server, NetSession session,
            NetMessage message)
        {
            session.Logger.Debug("NotifyServerConnectionRequestData");

            if (!NotifyServerConnectionRequestData.Deserialize(message, out var packet))
                return;

            if (packet.InternalVersion != Constants.NetVersion)
            {
                session.Logger.Warning(
                    "Internal version mismatch => client={ClientVersion}, server={ServerVersion}",
                    packet.InternalVersion, Constants.NetVersion);

                session.NexumToClient(new NotifyServerDeniedConnection());
                return;
            }

            if (packet.ProtocolVersion != server.ServerGuid)
            {
                session.Logger.Warning(
                    "Protocol version mismatch => client={ClientProtocolVersion}, server={ServerProtocolVersion}",
                    packet.ProtocolVersion, server.ServerGuid);

                session.NexumToClient(new NotifyProtocolVersionMismatch());
                return;
            }

            session.SetConnectionState(ConnectionState.Connected);
            session.NexumToClient(new NotifyServerConnectSuccess
            {
                HostId = session.HostId,
                ServerInstanceGuid = server.ServerInstanceGuid,
                UserData = new ByteArray(),
                ServerEndPoint = session.RemoteEndPoint
            });
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
            unreliablePong.Write(MessageType.UnreliablePong);
            unreliablePong.Write(clientTime);
            unreliablePong.Write(session.GetAbsoluteTime());

            unreliablePong.Write(paddingSize);
            if (paddingSize > 0)
                unreliablePong.WriteZeroes(paddingSize);

            session.NexumToClientUdpIfAvailable(unreliablePong);
        }

        private static void ReliableRelay1Handler(NetServer server, NetSession session, NetMessage message)
        {
            if (!ReliableRelay1.Deserialize(message, out var packet))
                return;

            if (packet.Destinations == null)
                return;

            var group = session.P2PGroup;
            if (group == null)
                return;

            foreach (var destination in packet.Destinations)
                if (group.P2PMembersInternal.TryGetValue(destination.HostId, out var p2pMember))
                {
                    var reliableRelay2 = new ReliableRelay2
                    {
                        HostId = session.HostId,
                        FrameNumber = destination.FrameNumber,
                        Data = packet.Data
                    }.Serialize();
                    p2pMember.Session.NexumToClient(reliableRelay2);
                }
        }

        private static void UnreliableRelay1Handler(NetServer server, NetSession session, NetMessage message)
        {
            if (!UnreliableRelay1.Deserialize(message, out var packet))
                return;

            if (packet.DestinationHostIds == null)
                return;

            var unreliableRelay2 = new UnreliableRelay2
            {
                HostId = session.HostId,
                Data = packet.Data
            }.Serialize();

            var group = session.P2PGroup;
            if (group == null)
                return;

            foreach (uint hostId in packet.DestinationHostIds)
                if (group.P2PMembersInternal.TryGetValue(hostId, out var p2pMember))
                    p2pMember.Session.NexumToClientUdpIfAvailable(unreliableRelay2);
        }

        private static void ServerHolepunchHandler(NetServer server, NetSession session, NetMessage message)
        {
            if (!server.UdpEnabled)
                return;

            if (!ServerHolepunch.Deserialize(message, out var packet))
                return;

            Guid capturedMagicNumber;
            IPEndPoint capturedUdpEndPoint;
            lock (session.UdpHolepunchLock)
            {
                if (!packet.MagicNumber.Equals(session.HolepunchMagicNumber))
                {
                    session.Logger.Warning(
                        "ServerHolepunch => magic number mismatch, expected {Expected}, got {Actual}",
                        session.HolepunchMagicNumber, packet.MagicNumber);
                    return;
                }

                if (session.UdpEnabled)
                {
                    session.Logger.Verbose("ServerHolepunch => UDP already enabled, ignoring duplicate");
                    return;
                }

                capturedMagicNumber = session.HolepunchMagicNumber;
                capturedUdpEndPoint = session.UdpEndPoint;
            }

            session.Logger.Debug("ServerHolepunch => guid = {MagicNumber}", packet.MagicNumber);

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

            if (!NotifyHolepunchSuccess.Deserialize(message, out var packet))
                return;

            Guid capturedMagicNumber;
            lock (session.UdpHolepunchLock)
            {
                if (!packet.MagicNumber.Equals(session.HolepunchMagicNumber))
                {
                    session.Logger.Warning(
                        "NotifyHolepunchSuccess => magic number mismatch, expected {Expected}, got {Actual}",
                        session.HolepunchMagicNumber, packet.MagicNumber);
                    return;
                }

                if (session.UdpEnabled)
                {
                    session.Logger.Verbose("NotifyHolepunchSuccess => UDP already enabled, ignoring duplicate");
                    return;
                }

                session.Logger.Debug(
                    "NotifyHolepunchSuccess => guid = {MagicNumber}, localEndpoint = {LocalEndpoint}, publicEndpoint = {PublicEndpoint}",
                    packet.MagicNumber, packet.LocalEndPoint, packet.PublicEndPoint);

                session.UdpEnabled = true;
                session.LastUdpPing = DateTimeOffset.Now;
                session.UdpLocalEndPointInternal = packet.LocalEndPoint;

                capturedMagicNumber = session.HolepunchMagicNumber;
            }

            if (session.P2PGroup != null &&
                session.P2PGroup.P2PMembersInternal.TryGetValue(session.HostId, out var member))
                session.InitializeToClientReliableUdp(member.P2PFirstFrameNumber);

            session.NexumToClientUdpIfAvailable(
                HolepunchHelper.CreateNotifyClientServerUdpMatchedMessage(capturedMagicNumber), true);
            HolepunchHelper.SendBurstMessages(
                () => HolepunchHelper.CreateNotifyClientServerUdpMatchedMessage(capturedMagicNumber),
                msg => session.NexumToClientUdpIfAvailable(msg, true),
                HolepunchConfig.UdpMatchedDelayMs
            );

            server.ProcessPendingPeerHolepunchRequests(session);
            server.InitiateP2PConnections(session);
        }

        private static void PeerUdpServerHolepunchHandler(NetServer server, NetSession session, NetMessage message,
            IPEndPoint udpEndPoint)
        {
            if (!server.UdpEnabled)
                return;

            if (!PeerUdpServerHolepunch.Deserialize(message, out var packet))
                return;

            var group = session.P2PGroup;
            if (group == null)
            {
                session.Logger.Warning("PeerUdpServerHolepunch => session has no P2P group");
                return;
            }

            if (!group.P2PMembersInternal.TryGetValue(packet.TargetHostId, out var targetPeer))
            {
                session.Logger.Warning("PeerUdpServerHolepunch => target peer not found for hostId = {HostId}",
                    packet.TargetHostId);
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

            lock (targetPeer.Session.UdpHolepunchLock)
            {
                if (!targetPeer.Session.UdpEnabled)
                {
                    session.Logger.Debug(
                        "PeerUdpServerHolepunch => guid = {MagicNumber}, targetHostId = {TargetHostId} - target UDP not ready, queueing",
                        packet.MagicNumber,
                        packet.TargetHostId);
                    targetPeer.Session.PendingPeerHolepunchRequests.Enqueue(
                        new PendingPeerHolepunchRequest(session, packet.MagicNumber));
                    return;
                }
            }

            session.Logger.Debug(
                "PeerUdpServerHolepunch => guid = {MagicNumber}, targetHostId = {TargetHostId}",
                packet.MagicNumber,
                packet.TargetHostId);

            session.NexumToClient(
                HolepunchHelper.CreatePeerUdpServerHolepunchAckMessage(packet.MagicNumber, udpEndPoint,
                    targetPeer.Session.HostId));
            HolepunchHelper.SendBurstMessages(
                () => HolepunchHelper.CreatePeerUdpServerHolepunchAckMessage(packet.MagicNumber, udpEndPoint,
                    targetPeer.Session.HostId),
                msg => session.NexumToClient(msg)
            );
        }

        private static void PeerUdpNotifyHolepunchSuccessHandler(NetServer server, NetSession session,
            NetMessage message)
        {
            if (!server.UdpEnabled)
                return;

            if (!PeerUdpNotifyHolepunchSuccess.Deserialize(message, out var packet))
                return;

            var group = session.P2PGroup;
            if (group == null)
            {
                session.Logger.Warning("PeerUdpNotifyHolepunchSuccess => session has no P2P group");
                return;
            }

            if (!group.P2PMembersInternal.TryGetValue(session.HostId, out var peer) ||
                !peer.ConnectionStates.TryGetValue(packet.HostId, out var state) ||
                !state.RemotePeer.ConnectionStates.TryGetValue(session.HostId, out var remoteState))
            {
                session.Logger.Warning(
                    "PeerUdpNotifyHolepunchSuccess => connection state not found for hostId = {HostId}", packet.HostId);
                return;
            }

            session.Logger.Debug(
                "PeerUdpNotifyHolepunchSuccess => localEndpoint = {LocalEndpoint}, publicEndpoint = {PublicEndpoint}, targetHostId = {TargetHostId}",
                packet.LocalEndPoint, packet.PublicEndPoint, packet.HostId);

            bool shouldSendRequestP2PHolepunch = HolepunchHelper.WithOrderedLocks(
                session.HostId, packet.HostId, state.StateLock, remoteState.StateLock, () =>
                {
                    if (state.PeerUdpHolepunchSuccess)
                        return false;

                    state.PeerUdpHolepunchSuccess = true;
                    state.LocalEndPoint = packet.LocalEndPoint;
                    state.EndPoint = packet.PublicEndPoint;
                    return remoteState.PeerUdpHolepunchSuccess;
                });

            if (!shouldSendRequestP2PHolepunch)
                return;

            peer.Session.RmiToClient(new RequestP2PHolepunch
            {
                HostId = packet.HostId,
                LocalEndPoint = remoteState.LocalEndPoint,
                ExternalEndPoint = remoteState.EndPoint
            });

            state.RemotePeer.Session.RmiToClient(new RequestP2PHolepunch
            {
                HostId = session.HostId,
                LocalEndPoint = state.LocalEndPoint,
                ExternalEndPoint = state.EndPoint
            });
        }

        private static void RmiHandler(NetServer server, NetSession session, NetMessage message)
        {
            if (!message.Read<NexumOpCode>(out var rmiId))
                return;

            switch (rmiId)
            {
                case NexumOpCode.ReliablePing:
                    session.RmiToClient(new ReliablePong());
                    break;

                case NexumOpCode.P2P_NotifyDirectP2PDisconnected:
                {
                    if (!P2PNotifyDirectP2PDisconnected.Deserialize(message, out var packet))
                        return;

                    var reason = packet.Reason;
                    var group = session.P2PGroup;
                    if (group == null)
                    {
                        session.Logger.Warning("P2P_NotifyDirectP2PDisconnected => session has no P2P group");
                        return;
                    }

                    if (!group.P2PMembersInternal.TryGetValue(session.HostId, out var peer) ||
                        !peer.ConnectionStates.TryGetValue(packet.HostId, out var stateA) ||
                        !stateA.RemotePeer.ConnectionStates.TryGetValue(session.HostId, out var stateB))
                    {
                        session.Logger.Warning(
                            "P2P_NotifyDirectP2PDisconnected => connection state not found for hostId = {HostId}",
                            packet.HostId);
                        break;
                    }

                    session.Logger.Debug("P2P_NotifyDirectP2PDisconnected => hostId = {HostId}, reason = {Reason}",
                        packet.HostId, reason);

                    bool shouldNotify = HolepunchHelper.WithOrderedLocks(
                        session.HostId, packet.HostId, stateA.StateLock, stateB.StateLock, () =>
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
                        stateA.RemotePeer.Session.RmiToClient(new P2PNotifyDirectP2PDisconnected2
                        {
                            HostId = session.HostId,
                            Reason = reason
                        });

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
                    if (!P2PGroupMemberJoinAck.Deserialize(message, out var packet))
                        return;

                    if (session.HostId == packet.AddedMemberHostId)
                        return;
                    var group = session.P2PGroup;
                    if (group == null)
                    {
                        session.Logger.Warning("P2PGroup_MemberJoin_Ack => session has no P2P group");
                        return;
                    }

                    if (group.HostId != packet.GroupHostId)
                    {
                        session.Logger.Warning(
                            "P2PGroup_MemberJoin_Ack => group hostId mismatch, expected {Expected}, got {Actual}",
                            group.HostId, packet.GroupHostId);
                        return;
                    }

                    session.Logger.Debug(
                        "P2PGroup_MemberJoin_Ack => groupHostId = {GroupHostId}, addedMemberHostId = {AddedMemberHostId}, eventId = {EventId}",
                        packet.GroupHostId, packet.AddedMemberHostId, packet.EventId);

                    if (group.P2PMembersInternal.TryGetValue(session.HostId, out var peer) &&
                        peer.ConnectionStates.TryGetValue(packet.AddedMemberHostId, out var stateA) &&
                        stateA.EventId == packet.EventId &&
                        stateA.RemotePeer.ConnectionStates.ContainsKey(session.HostId))
                    {
                        stateA.IsJoined = true;
                        stateA.LocalPortReuseSuccess = packet.LocalPortReuseSuccess;
                        server.InitiateP2PConnections(session);
                        server.InitiateP2PConnections(stateA.RemotePeer.Session);
                    }

                    break;
                }

                case NexumOpCode.NotifyP2PHolepunchSuccess:
                {
                    if (!NotifyP2PHolepunchSuccess.Deserialize(message, out var packet))
                        return;

                    var group = session.P2PGroup;
                    if (group == null)
                    {
                        session.Logger.Warning("NotifyP2PHolepunchSuccess => session has no P2P group");
                        return;
                    }

                    if (session.HostId != packet.HostIdA && session.HostId != packet.HostIdB)
                    {
                        session.Logger.Warning(
                            "NotifyP2PHolepunchSuccess => session hostId {SessionHostId} doesn't match hostIdA {HostIdA} or hostIdB {HostIdB}",
                            session.HostId, packet.HostIdA, packet.HostIdB);
                        return;
                    }

                    if (!group.P2PMembersInternal.TryGetValue(packet.HostIdA, out var peerA) ||
                        !group.P2PMembersInternal.TryGetValue(packet.HostIdB, out var peerB) ||
                        !peerA.ConnectionStates.TryGetValue(peerB.Session.HostId, out var stateA) ||
                        !peerB.ConnectionStates.TryGetValue(peerA.Session.HostId, out var stateB))
                    {
                        session.Logger.Warning(
                            "NotifyP2PHolepunchSuccess => connection state not found for hostIdA = {HostIdA}, hostIdB = {HostIdB}",
                            packet.HostIdA, packet.HostIdB);
                        break;
                    }

                    session.Logger.Debug(
                        "NotifyP2PHolepunchSuccess => hostIdA = {HostIdA}, hostIdB = {HostIdB}, aSendAddrToB = {ASendAddrToB}, aRecvAddrFromB = {ARecvAddrFromB}, bSendAddrToA = {BSendAddrToA}, bRecvAddrFromA = {BRecvAddrFromA}",
                        packet.HostIdA, packet.HostIdB, packet.ASendAddrToB, packet.ARecvAddrFromB, packet.BSendAddrToA,
                        packet.BRecvAddrFromA);

                    bool shouldSendEstablish = HolepunchHelper.WithOrderedLocks(
                        packet.HostIdA, packet.HostIdB, stateA.StateLock, stateB.StateLock, () =>
                        {
                            if (session.HostId == peerA.Session.HostId)
                            {
                                stateA.HolepunchSuccess = true;
                                stateA.LastSuccessfulLocalPort = stateA.LocalEndPoint?.Port ?? 0;
                            }
                            else
                            {
                                stateB.HolepunchSuccess = true;
                                stateB.LastSuccessfulLocalPort = stateB.LocalEndPoint?.Port ?? 0;
                            }

                            bool canSend = stateA.HolepunchSuccess && stateB.HolepunchSuccess &&
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
                        var recycleTimestamp = DateTimeOffset.UtcNow;
                        peerA.Session.LastSuccessfulP2PRecycleInfos[packet.HostIdB] =
                            new P2PRecycleInfo(packet.ASendAddrToB, packet.ARecvAddrFromB, recycleTimestamp);
                        peerB.Session.LastSuccessfulP2PRecycleInfos[packet.HostIdA] =
                            new P2PRecycleInfo(packet.BSendAddrToA, packet.BRecvAddrFromA, recycleTimestamp);

                        var establishMsg = new NotifyDirectP2PEstablish
                        {
                            HostIdA = packet.HostIdA,
                            HostIdB = packet.HostIdB,
                            ASendAddrToB = packet.ASendAddrToB,
                            BRecvAddrFromA = packet.BRecvAddrFromA,
                            BSendAddrToA = packet.BSendAddrToA,
                            ARecvAddrFromB = packet.ARecvAddrFromB
                        };

                        peerA.Session.RmiToClient(establishMsg);
                        peerB.Session.RmiToClient(establishMsg);
                    }

                    break;
                }

                case NexumOpCode.NotifyJitDirectP2PTriggered:
                {
                    if (!NotifyJitDirectP2PTriggered.Deserialize(message, out var packet))
                        return;

                    var group = session.P2PGroup;
                    if (group == null)
                    {
                        session.Logger.Warning("NotifyJitDirectP2PTriggered => session has no P2P group");
                        return;
                    }

                    if (!group.P2PMembersInternal.TryGetValue(session.HostId, out var peerA) ||
                        !group.P2PMembersInternal.TryGetValue(packet.HostId, out var peerB) ||
                        !peerA.ConnectionStates.TryGetValue(peerB.Session.HostId, out var stateA) ||
                        !peerB.ConnectionStates.TryGetValue(peerA.Session.HostId, out var stateB))
                    {
                        session.Logger.Warning(
                            "NotifyJitDirectP2PTriggered => connection state not found for targetHostId = {TargetHostId}",
                            packet.HostId);
                        break;
                    }

                    session.Logger.Debug("NotifyJitDirectP2PTriggered => targetHostId = {TargetHostId}",
                        packet.HostId);

                    server.EnsureP2PConnectionInitialized(session, stateA, stateB);

                    bool shouldSendNewConnection = HolepunchHelper.WithOrderedLocks(
                        session.HostId, packet.HostId, stateA.StateLock, stateB.StateLock, () =>
                        {
                            if (session.HostId == peerA.Session.HostId)
                                stateA.JitTriggered = true;
                            else
                                stateB.JitTriggered = true;

                            bool canSend = stateA.JitTriggered && stateB.JitTriggered &&
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
                        peerA.Session.RmiToClient(
                            new NewDirectP2PConnection { HostId = peerB.Session.HostId });

                        peerB.Session.RmiToClient(
                            new NewDirectP2PConnection { HostId = peerA.Session.HostId });
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

                    session.RmiToClient(new S2CCreateUdpSocketAck
                    {
                        Result = true,
                        UdpSocket = udpEndpoint
                    });
                    break;
                }

                case NexumOpCode.C2S_CreateUdpSocketAck:
                {
                    if (!server.UdpEnabled)
                    {
                        session.Logger.Debug("C2S_CreateUdpSocketAck => UDP not enabled on server");
                        return;
                    }

                    session.Logger.Debug("C2S_CreateUdpSocketAck => hostId = {HostId}, guid = {MagicNumber}",
                        session.HostId, session.HolepunchMagicNumber);

                    var requestStartHolepunch = new RequestStartServerHolepunch
                    {
                        MagicNumber = session.HolepunchMagicNumber
                    };

                    session.NexumToClient(requestStartHolepunch);
                    break;
                }

                case NexumOpCode.ShutdownTcp:
                    session.Logger.Debug("ShutdownTcp");
                    session.RmiToClient(new ShutdownTcpAck());
                    break;

                case NexumOpCode.ShutdownTcpHandshake:
                    session.Logger.Debug("ShutdownTcpHandshake");
                    session.Dispose();
                    break;

                default:
                    server.OnRmiReceive(session, message, (ushort)rmiId);
                    break;
            }
        }

        private static void EncryptedHandler(NetServer server, NetSession session, NetMessage message,
            IPEndPoint udpEndPoint)
        {
            NetCoreHandler.HandleEncrypted(
                message,
                session.Crypt,
                decryptedMsg => ReadMessage(server, session, decryptedMsg, udpEndPoint)
            );
        }

        private static void CompressedHandler(NetServer server, NetSession session, NetMessage message,
            IPEndPoint udpEndPoint)
        {
            NetCoreHandler.HandleCompressed(
                message,
                session.Logger,
                decompressedMsg => ReadMessage(server, session, decompressedMsg, udpEndPoint)
            );
        }
    }
}
