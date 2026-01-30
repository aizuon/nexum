using System;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BaseLib.Extensions;
using Nexum.Client.P2P;
using Nexum.Client.Utilities;
using Nexum.Core;
using Nexum.Core.Configuration;
using Nexum.Core.Crypto;
using Nexum.Core.Holepunching;
using Nexum.Core.Message.C2S;
using Nexum.Core.Message.S2C;
using Nexum.Core.Message.X2X;
using Nexum.Core.ReliableUdp;
using Nexum.Core.Rmi.C2C;
using Nexum.Core.Rmi.C2S;
using Nexum.Core.Rmi.S2C;
using Nexum.Core.Routing;
using Nexum.Core.Serialization;
using Org.BouncyCastle.Asn1;
using SysUtil = Nexum.Core.Utilities.SysUtil;

namespace Nexum.Client.Core
{
    internal static class NetClientHandler
    {
        internal static void ReadFrame(NetClient client, NetMessage message, ushort filterTag = 0,
            IPEndPoint udpEndPoint = null,
            bool bypass = false)
        {
            if (bypass)
            {
                ReadMessage(client, message, filterTag, udpEndPoint);
                return;
            }

            var packet = new ByteArray();
            if (!message.Read(ref packet))
                return;

            var innerMessage = new NetMessage(packet) { RelayFrom = message.RelayFrom };
            ReadMessage(client, innerMessage, filterTag, udpEndPoint);
        }

        internal static void ReadMessage(NetClient client, NetMessage message, ushort filterTag = 0,
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
                    RmiHandler(client, message, filterTag, udpEndPoint);
                    break;

                case MessageType.Encrypted:
                    EncryptedHandler(client, message, filterTag, udpEndPoint);
                    break;

                case MessageType.Compressed:
                    CompressedHandler(client, message, filterTag, udpEndPoint);
                    break;

                case MessageType.NotifyServerConnectionHint:
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                        NotifyServerConnectionHintHandler(client, message);
                    break;

                case MessageType.NotifyServerConnectSuccess:
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                        NotifyServerConnectSuccessHandler(client, message);
                    break;

                case MessageType.NotifyCSSessionKeySuccess:
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                        NotifyCSSessionKeySuccessHandler(client);
                    break;

                case MessageType.ReliableRelay2:
                    message.Reliable = true;
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                        ReliableRelay2Handler(client, message);
                    break;

                case MessageType.UnreliableRelay2:
                    message.Reliable = false;
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                        UnreliableRelay2Handler(client, message);
                    break;

                case MessageType.RequestStartServerHolepunch:
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                        RequestStartServerHolepunchHandler(client, message);
                    break;

                case MessageType.ServerHolepunchAck:
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                        ServerHolepunchAckHandler(client, message);
                    break;

                case MessageType.NotifyClientServerUdpMatched:
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                        NotifyClientServerUdpMatchedHandler(client, message);
                    break;

                case MessageType.PeerUdp_ServerHolepunchAck:
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                        PeerUdpServerHolepunchAckHandler(client, message);
                    break;

                case MessageType.PeerUdp_PeerHolepunch:
                    PeerUdpPeerHolepunchHandler(client, message, udpEndPoint);
                    break;

                case MessageType.PeerUdp_PeerHolepunchAck:
                    PeerUdpPeerHolepunchAckHandler(client, message, udpEndPoint);
                    break;

                case MessageType.P2PRequestIndirectServerTimeAndPing:
                    P2PRequestIndirectServerTimeAndPingHandler(client, message, filterTag, udpEndPoint);
                    break;

                case MessageType.P2PReplyIndirectServerTimeAndPong:
                    P2PReplyIndirectServerTimeAndPongHandler(client, message, filterTag, udpEndPoint);
                    break;

                case MessageType.ReliableUdp_Frame:
                    ReliableUdpFrameHandler(client, message, filterTag, udpEndPoint);
                    break;

                case MessageType.UnreliablePong:
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                        UnreliablePongHandler(client, message);
                    break;

                case MessageType.ConnectServerTimedout:
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                    {
                        client.Logger.Warning("Connection timed out");
                        _ = client.CloseAsync(false);
                    }

                    break;

                case MessageType.NotifyProtocolVersionMismatch:
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                    {
                        client.Logger.Warning("Protocol version mismatch - server rejected connection");
                        _ = client.CloseAsync(false);
                    }

                    break;

                case MessageType.NotifyServerDeniedConnection:
                    if (!client.IsFromRemoteClientPeer(udpEndPoint, filterTag, message.RelayFrom))
                    {
                        client.Logger.Warning("Server denied connection");
                        _ = client.CloseAsync(false);
                    }

                    break;

                case MessageType.S2CRoutedMulticast1:
                case MessageType.S2CRoutedMulticast2:
                    break;

                default:
                    client.Logger.Warning("Received unknown Core ID {MessageType}", messageType);
                    break;
            }
        }

        private static void ReliableUdpFrameHandler(NetClient client, NetMessage message, ushort filterTag,
            IPEndPoint udpEndPoint)
        {
            if (!ReliableUdpHelper.ParseFrame(message, out var frame))
                return;

            if ((client.ServerUdpSocket != null && client.ServerUdpSocket.Equals(udpEndPoint)) ||
                FilterTag.Create((uint)HostId.Server, client.HostId) == filterTag)
            {
                if (client.ServerReliableUdp != null)
                {
                    client.ServerReliableUdp.TakeReceivedFrame(frame);
                    ExtractMessagesFromServerReliableUdpStream(client, filterTag, udpEndPoint);
                }
                else
                {
                    if (frame.Type == ReliableUdpFrameType.Data && frame.Data != null)
                        if (ReliableUdpHelper.UnwrapPayload(frame.Data, out var payload))
                        {
                            var innerMessage = new NetMessage(payload.ToArray(), true);
                            ReadMessage(client, innerMessage, filterTag, udpEndPoint);
                        }
                }

                return;
            }

            var sourceMember = client.P2PGroup?.FindMember(client.HostId, udpEndPoint, filterTag, message.RelayFrom);
            if (sourceMember == null)
            {
                client.Logger.Verbose(
                    "ReliableUdp_Frame from unknown source, ignoring => udpEndPoint = {UdpEndPoint}, filterTag = {FilterTag}",
                    udpEndPoint, filterTag);
                return;
            }

            sourceMember.ProcessReceivedReliableUdpFrame(frame);

            ExtractMessagesFromP2PReliableUdpStream(client, sourceMember, filterTag, udpEndPoint);
        }

        private static void ExtractMessagesFromServerReliableUdpStream(NetClient client, ushort filterTag,
            IPEndPoint udpEndPoint)
        {
            var stream = client.ServerReliableUdp?.ReceivedStream;
            ReliableUdpHelper.ExtractMessagesFromStream(stream,
                msg => ReadMessage(client, msg, filterTag, udpEndPoint));
        }

        private static void ExtractMessagesFromP2PReliableUdpStream(NetClient client, P2PMember member,
            ushort filterTag,
            IPEndPoint udpEndPoint)
        {
            var stream = member.ToPeerReliableUdp?.ReceivedStream;
            ReliableUdpHelper.ExtractMessagesFromStream(stream,
                msg => ReadMessage(client, msg, filterTag, udpEndPoint));
        }

        private static void RequestStartServerHolepunchHandler(NetClient client, NetMessage message)
        {
            if (!RequestStartServerHolepunch.Deserialize(message, out var packet))
                return;

            lock (client.UdpHolepunchLock)
            {
                if (client.UdpEnabled)
                    return;

                client.SelfUdpSocket = null;
                client.UdpMagicNumber = packet.MagicNumber;
            }

            client.Logger.Debug("RequestStartServerHolepunch => guid = {MagicNumber}", packet.MagicNumber);

            var serverHolepunchMsg = new ServerHolepunch { MagicNumber = packet.MagicNumber }.Serialize();
            client.NexumToServerUdpIfAvailable(serverHolepunchMsg, true);
            HolepunchHelper.SendBurstMessagesWithCheck(
                serverHolepunchMsg,
                msg => client.NexumToServerUdpIfAvailable(msg, true),
                () => client.SelfUdpSocket == null,
                HolepunchConfig.UdpMatchedDelayMs
            );
        }

        private static void ServerHolepunchAckHandler(NetClient client, NetMessage message)
        {
            if (!ServerHolepunchAck.Deserialize(message, out var packet))
                return;

            var udpChannel = client.UdpChannel;
            if (udpChannel == null)
                return;

            Guid capturedMagicNumber;
            IPEndPoint capturedSelfUdpSocket;

            lock (client.UdpHolepunchLock)
            {
                if (!packet.MagicNumber.Equals(client.UdpMagicNumber))
                {
                    client.Logger.Warning(
                        "ServerHolepunchAck => magic number mismatch, expected {Expected}, got {Actual}",
                        client.UdpMagicNumber, packet.MagicNumber);
                    return;
                }

                if (client.SelfUdpSocket != null)
                {
                    client.Logger.Verbose("ServerHolepunchAck => SelfUdpSocket already set, ignoring duplicate");
                    return;
                }

                client.SelfUdpSocket = packet.EndPoint;

                capturedMagicNumber = client.UdpMagicNumber;
                capturedSelfUdpSocket = client.SelfUdpSocket;
            }

            client.Logger.Debug("ServerHolepunchAck => guid = {MagicNumber}, endpoint = {Endpoint}", packet.MagicNumber,
                packet.EndPoint);

            var notifyHolepunchSuccess = new NotifyHolepunchSuccess
            {
                MagicNumber = capturedMagicNumber,
                LocalEndPoint = new IPEndPoint(client.LocalIP, ((IPEndPoint)udpChannel.LocalAddress).Port),
                PublicEndPoint = capturedSelfUdpSocket
            }.Serialize();

            client.NexumToServer(notifyHolepunchSuccess);
        }

        private static void NotifyClientServerUdpMatchedHandler(NetClient client, NetMessage message)
        {
            if (!NotifyClientServerUdpMatched.Deserialize(message, out var packet))
                return;

            lock (client.UdpHolepunchLock)
            {
                if (client.UdpEnabled)
                    return;

                client.UdpMagicNumber = packet.MagicNumber;
                client.UdpEnabled = true;
                client.ServerUdpLastReceivedTime = client.GetAbsoluteTime();
            }

            client.InitializeServerReliableUdp(client.P2PFirstFrameNumber);
            client.StartReliableUdpLoop();
            client.StartUnreliablePingLoop();

            client.Logger.Debug("UDP connection established with server, guid = {MagicNumber}", packet.MagicNumber);
            client.OnUdpConnected();

            ProcessPendingP2PConnections(client);
        }

        private static void ProcessPendingP2PConnections(NetClient client)
        {
            Guid magicNumber;
            lock (client.UdpHolepunchLock)
            {
                if (!client.UdpEnabled)
                    return;

                magicNumber = client.UdpMagicNumber;
            }

            foreach (uint hostId in client.PendingP2PConnections.Keys)
            {
                if (!client.PendingP2PConnections.TryRemove(hostId, out _))
                    continue;
                if (!client.P2PGroup.P2PMembersInternal.TryGetValue(hostId, out var p2pMember))
                    continue;

                using (p2pMember.P2PMutex.Enter())
                {
                    if (p2pMember.P2PHolepunchInitiated)
                        continue;

                    p2pMember.P2PHolepunchInitiated = true;
                }

                client.Logger.Debug("Processing pending P2P connection => hostId = {HostId}", hostId);

                var capturedMember = p2pMember;
                var peerUdpServerHolepunchMsg = new PeerUdp_ServerHolepunch
                {
                    MagicNumber = magicNumber,
                    TargetHostId = hostId
                }.Serialize();
                client.NexumToServerUdpIfAvailable(peerUdpServerHolepunchMsg, true);
                HolepunchHelper.SendBurstMessagesWithCheck(
                    peerUdpServerHolepunchMsg,
                    msg => client.NexumToServerUdpIfAvailable(msg, true),
                    () => !capturedMember.DirectP2P && !capturedMember.IsClosed
                );
            }
        }

        private static void PeerUdpServerHolepunchAckHandler(NetClient client, NetMessage message)
        {
            if (!PeerUdp_ServerHolepunchAck.Deserialize(message, out var packet))
                return;

            client.Logger.Debug(
                "PeerUdpServerHolepunchAck => guid = {MagicNumber}, endpoint = {Endpoint}, hostId = {HostId}",
                packet.MagicNumber,
                packet.EndPoint,
                packet.TargetHostId
            );

            IPEndPoint capturedSelfUdpSocket;
            lock (client.UdpHolepunchLock)
            {
                if (!packet.MagicNumber.Equals(client.UdpMagicNumber))
                {
                    client.Logger.Warning(
                        "PeerUdpServerHolepunchAck => magic number mismatch, expected {Expected}, got {Actual}",
                        client.UdpMagicNumber, packet.MagicNumber);
                    return;
                }

                if (client.SelfUdpSocket == null)
                {
                    client.Logger.Warning(
                        "PeerUdpServerHolepunchAck => SelfUdpSocket is null, this should not happen");
                    return;
                }

                capturedSelfUdpSocket = client.SelfUdpSocket;
            }

            if (!client.P2PGroup.P2PMembersInternal.TryGetValue(packet.TargetHostId, out var p2pMember))
            {
                if (packet.TargetHostId != client.HostId)
                    client.Logger.Warning("PeerUdpServerHolepunchAck => P2P member not found for hostId = {HostId}",
                        packet.TargetHostId);
                return;
            }

            Task.Run(async () =>
            {
                await using (await p2pMember.P2PMutex.EnterAsync())
                {
                    if (p2pMember.IsClosed)
                        return;

                    int port;
                    if (p2pMember.PeerUdpChannel != null)
                    {
                        port = ((IPEndPoint)p2pMember.PeerUdpChannel.LocalAddress).Port;
                        client.Logger.Verbose(
                            "PeerUdpServerHolepunchAck => reusing existing P2P UDP socket on port {Port}", port);
                    }
                    else
                    {
                        (var channel, int newPort, _) =
                            await client.ConnectUdpAsync();
                        p2pMember.PeerUdpChannel = channel;
                        port = newPort;
                    }

                    p2pMember.SelfUdpLocalSocket = new IPEndPoint(client.LocalIP, port);
                    p2pMember.SelfUdpSocket = new IPEndPoint(capturedSelfUdpSocket.Address, port);

                    var peerUdpNotifyHolepunchSuccess = new PeerUdp_NotifyHolepunchSuccess
                    {
                        LocalEndPoint = p2pMember.SelfUdpLocalSocket,
                        PublicEndPoint = p2pMember.SelfUdpSocket,
                        HostId = packet.TargetHostId
                    }.Serialize();
                    client.NexumToServer(peerUdpNotifyHolepunchSuccess);
                }
            });
        }

        private static void PeerUdpPeerHolepunchHandler(NetClient client, NetMessage message, IPEndPoint udpEndPoint)
        {
            if (!PeerUdp_PeerHolepunch.Deserialize(message, out var packet))
                return;

            if (!packet.ServerInstanceGuid.Equals(client.ServerInstanceGuid))
            {
                client.Logger.Warning(
                    "PeerUdpPeerHolepunch => server instance GUID mismatch, expected {Expected}, got {Actual}",
                    client.ServerInstanceGuid, packet.ServerInstanceGuid);
                return;
            }

            if (!client.P2PGroup.P2PMembersInternal.TryGetValue(packet.HostId, out var p2pMember))
            {
                if (packet.HostId != client.HostId)
                    client.Logger.Warning("PeerUdpPeerHolepunch => P2P member not found for hostId = {HostId}",
                        packet.HostId);
                return;
            }

            using (p2pMember.P2PMutex.Enter())
            {
                if (!packet.PeerMagicNumber.Equals(client.PeerUdpMagicNumber))
                {
                    client.Logger.Warning(
                        "PeerUdpPeerHolepunch => peer magic number mismatch for hostId = {HostId}, expected {Expected}, got {Actual}",
                        packet.HostId, client.PeerUdpMagicNumber, packet.PeerMagicNumber);
                    return;
                }

                if (p2pMember.IsClosed || p2pMember.DirectP2P)
                {
                    client.Logger.Verbose(
                        "PeerUdpPeerHolepunch => skipping for hostId = {HostId}, IsClosed = {IsClosed}, DirectP2P = {DirectP2P}",
                        packet.HostId, p2pMember.IsClosed, p2pMember.DirectP2P);
                    return;
                }
            }

            client.Logger.Debug(
                "PeerUdpPeerHolepunch => hostId = {HostId}, guid = {MagicNumber}, serverInstanceGuid = {ServerInstanceGuid}, endpoint = {Endpoint}",
                packet.HostId,
                packet.PeerMagicNumber,
                packet.ServerInstanceGuid,
                packet.TargetEndpoint
            );

            var capturedMember = p2pMember;
            Task.Run(async () =>
            {
                bool socketsReady = await HolepunchHelper.WaitForConditionWithBackoffAsync(
                    () => capturedMember.PeerUdpSocket != null
                          && capturedMember.PeerUdpLocalSocket != null
                          && capturedMember.SelfUdpSocket != null
                          && capturedMember.SelfUdpLocalSocket != null,
                    () => capturedMember.IsClosed || capturedMember.DirectP2P
                );

                if (!socketsReady)
                    return;

                IPEndPoint peerUdpSocket;
                IPEndPoint peerUdpLocalSocket;

                using (capturedMember.P2PMutex.Enter())
                {
                    if (capturedMember.IsClosed || capturedMember.DirectP2P)
                        return;

                    peerUdpSocket = capturedMember.PeerUdpSocket;
                    peerUdpLocalSocket = capturedMember.PeerUdpLocalSocket;
                }

                bool hasLocalEndpoint = NetUtil.IsUnicastEndpoint(peerUdpLocalSocket);
                bool hasPublicEndpoint = NetUtil.IsUnicastEndpoint(peerUdpSocket);

                var msgToUdpEndPoint = new PeerUdp_PeerHolepunchAck
                {
                    MagicNumber = packet.PeerMagicNumber, HostId = client.HostId, SelfUdpSocket = packet.TargetEndpoint,
                    ReceivedEndPoint = udpEndPoint, TargetEndPoint = udpEndPoint
                }.Serialize();

                var msgToPeerUdpLocalSocket = hasLocalEndpoint
                    ? new PeerUdp_PeerHolepunchAck
                    {
                        MagicNumber = packet.PeerMagicNumber, HostId = client.HostId,
                        SelfUdpSocket = packet.TargetEndpoint,
                        ReceivedEndPoint = udpEndPoint, TargetEndPoint = peerUdpLocalSocket
                    }.Serialize()
                    : null;

                NetMessage msgToPeerUdpSocket = null;
                IPEndPoint[] predictedEndpoints = null;
                NetMessage[] predictedMessages = null;

                if (hasPublicEndpoint)
                {
                    msgToPeerUdpSocket = new PeerUdp_PeerHolepunchAck
                    {
                        MagicNumber = packet.PeerMagicNumber, HostId = client.HostId,
                        SelfUdpSocket = packet.TargetEndpoint,
                        ReceivedEndPoint = udpEndPoint, TargetEndPoint = peerUdpSocket
                    }.Serialize();

                    predictedEndpoints = HolepunchHelper.GeneratePredictedEndpoints(
                        peerUdpSocket,
                        HolepunchConfig.NatPortShotgunTrialCount,
                        HolepunchConfig.NatPortShotgunRange);

                    predictedMessages = new NetMessage[predictedEndpoints.Length];
                    for (int i = 0; i < predictedEndpoints.Length; i++)
                        predictedMessages[i] = new PeerUdp_PeerHolepunchAck
                        {
                            MagicNumber = packet.PeerMagicNumber, HostId = client.HostId,
                            SelfUdpSocket = packet.TargetEndpoint,
                            ReceivedEndPoint = udpEndPoint, TargetEndPoint = predictedEndpoints[i]
                        }.Serialize();
                }

                for (int burst = 0; burst < HolepunchConfig.BurstCount; burst++)
                {
                    if (capturedMember.IsClosed || capturedMember.DirectP2P)
                        return;

                    capturedMember.NexumToPeer(msgToUdpEndPoint, udpEndPoint);

                    if (hasLocalEndpoint)
                        capturedMember.NexumToPeer(msgToPeerUdpLocalSocket, peerUdpLocalSocket);

                    if (hasPublicEndpoint)
                    {
                        capturedMember.NexumToPeer(msgToPeerUdpSocket, peerUdpSocket);

                        for (int i = 0; i < predictedEndpoints.Length; i++)
                            capturedMember.NexumToPeer(predictedMessages[i], predictedEndpoints[i]);
                    }

                    if (burst < HolepunchConfig.BurstCount - 1)
                        await Task.Delay(HolepunchConfig.BurstDelayMs);
                }
            });
        }

        private static void PeerUdpPeerHolepunchAckHandler(NetClient client, NetMessage message, IPEndPoint udpEndPoint)
        {
            if (!PeerUdp_PeerHolepunchAck.Deserialize(message, out var packet))
                return;

            if (!client.P2PGroup.P2PMembersInternal.TryGetValue(packet.HostId, out var p2pMember))
            {
                if (packet.HostId != client.HostId)
                    client.Logger.Warning("PeerUdpPeerHolepunchAck => P2P member not found for hostId = {HostId}",
                        packet.HostId);
                return;
            }

            using (p2pMember.P2PMutex.Enter())
            {
                if (!packet.MagicNumber.Equals(p2pMember.PeerUdpMagicNumber))
                {
                    client.Logger.Warning(
                        "PeerUdpPeerHolepunchAck => magic number mismatch for hostId = {HostId}, expected {Expected}, got {Actual}",
                        packet.HostId, p2pMember.PeerUdpMagicNumber, packet.MagicNumber);
                    return;
                }

                if (p2pMember.SelfUdpLocalSocket != null &&
                    packet.ReceivedEndPoint.Port != p2pMember.SelfUdpLocalSocket.Port)
                {
                    client.Logger.Warning(
                        "PeerUdpPeerHolepunchAck => selfRecvAddr port mismatch for hostId = {HostId}, expected {Expected}, got {Actual}",
                        packet.HostId, p2pMember.SelfUdpLocalSocket.Port, packet.ReceivedEndPoint.Port);
                    return;
                }

                if (p2pMember.P2PHolepunchNotified)
                {
                    client.Logger.Verbose("PeerUdpPeerHolepunchAck => already notified for hostId = {HostId}",
                        packet.HostId);
                    return;
                }

                p2pMember.P2PHolepunchNotified = true;
            }

            client.Logger.Debug(
                "PeerUdpPeerHolepunchAck => guid = {MagicNumber}, hostId = {HostId}, peerSendAddr = {PeerSendAddr}, selfRecvAddr = {SelfRecvAddr}, selfSendAddr = {SelfSendAddr}",
                packet.MagicNumber,
                packet.HostId,
                packet.SelfUdpSocket,
                packet.ReceivedEndPoint,
                packet.TargetEndPoint
            );

            p2pMember.RmiToPeer(new HolsterP2PHolepunchTrial(), reliable: true);

            client.RmiToServer(new NotifyP2PHolepunchSuccess
            {
                HostIdA = client.HostId,
                HostIdB = packet.HostId,
                ASendAddrToB = packet.SelfUdpSocket,
                BRecvAddrFromA = packet.ReceivedEndPoint,
                BSendAddrToA = packet.TargetEndPoint,
                ARecvAddrFromB = udpEndPoint
            });
        }

        private static void P2PRequestIndirectServerTimeAndPingHandler(NetClient client, NetMessage message,
            ushort filterTag,
            IPEndPoint udpEndPoint)
        {
            if (!message.Read(out double time))
                return;

            message.Read(out int paddingSize);

            var p2pMember = client.P2PGroup?.FindMember(client.HostId, udpEndPoint, filterTag, message.RelayFrom);
            if (p2pMember != null)
            {
                p2pMember.LastUdpReceivedTime = client.GetAbsoluteTime();

                var p2pReplyIndirectServerTimeAndPong = new NetMessage();
                p2pReplyIndirectServerTimeAndPong.Write(MessageType.P2PReplyIndirectServerTimeAndPong);
                p2pReplyIndirectServerTimeAndPong.Write(time);

                p2pReplyIndirectServerTimeAndPong.Write(paddingSize);
                if (paddingSize > 0)
                    p2pReplyIndirectServerTimeAndPong.WriteZeroes(paddingSize);

                p2pMember.NexumToPeer(p2pReplyIndirectServerTimeAndPong);
            }
            else
            {
                client.Logger.Verbose(
                    "Received P2PRequestIndirectServerTimeAndPing from unknown source => udpEndPoint = {UdpEndPoint}",
                    udpEndPoint
                );
            }
        }

        private static void P2PReplyIndirectServerTimeAndPongHandler(NetClient client, NetMessage message,
            ushort filterTag,
            IPEndPoint udpEndPoint)
        {
            if (!message.Read(out double sentTime))
                return;

            message.Read(out int paddingSize);

            var p2pMember = client.P2PGroup?.FindMember(client.HostId, udpEndPoint, filterTag, message.RelayFrom);
            if (p2pMember != null)
            {
                double currentTime = client.GetAbsoluteTime();
                double lastPing = (currentTime - sentTime) / 2.0;

                if (lastPing < 0)
                    lastPing = 0;

                if (p2pMember.LastPingInternal > 0)
                    p2pMember.JitterInternal = Nexum.Core.Utilities.NetUtil.CalculateJitter(p2pMember.JitterInternal,
                        lastPing,
                        p2pMember.LastPingInternal);

                p2pMember.LastPingInternal = lastPing;
                p2pMember.LastUdpReceivedTime = currentTime;

                p2pMember.RecentPing = p2pMember.RecentPing != 0.0
                    ? SysUtil.Lerp(p2pMember.RecentPing, lastPing, ReliableUdpConfig.LagLinearProgrammingFactor)
                    : lastPing;

                if (paddingSize > 0)
                    p2pMember.MtuDiscovery.OnPongReceived(paddingSize, currentTime);
            }
            else
            {
                client.Logger.Verbose(
                    "Received P2PReplyIndirectServerTimeAndPong from unknown source => udpEndPoint = {UdpEndPoint}",
                    udpEndPoint
                );
            }
        }

        private static void HandleP2PGroupMemberJoin(NetClient client, NetMessage message, NexumOpCode opCode)
        {
            if (opCode == NexumOpCode.P2PGroup_MemberJoin)
            {
                if (!P2PGroup_MemberJoin.Deserialize(message, out var packet))
                    return;

                ProcessP2PGroupMemberJoin(client, packet.GroupHostId, packet.HostId, packet.EventId,
                    packet.P2PFirstFrameNumber, packet.PeerUdpMagicNumber, packet.EnableDirectP2P, packet.BindPort,
                    "P2PGroup_MemberJoin", packet.SessionKey, packet.FastSessionKey);
            }
            else
            {
                if (!P2PGroup_MemberJoin_Unencrypted.Deserialize(message, out var packet))
                    return;

                ProcessP2PGroupMemberJoin(client, packet.GroupHostId, packet.HostId, packet.EventId,
                    packet.P2PFirstFrameNumber, packet.PeerUdpMagicNumber, packet.EnableDirectP2P, packet.BindPort,
                    "P2PGroup_MemberJoin_Unencrypted");
            }
        }

        private static void ProcessP2PGroupMemberJoin(NetClient client, uint groupHostId, uint hostId, uint eventId,
            uint p2pFirstFrameNumber, Guid peerUdpMagicNumber, bool enableDirectP2P, int bindPort, string logName,
            ByteArray sessionKey = null, ByteArray fastSessionKey = null)
        {
            client.Logger.Debug(
                "{LogName} => hostId = {HostId}, groupHostId = {GroupHostId}, eventId = {EventId}, enableDirectP2P = {EnableDirectP2P}, bindPort = {BindPort}",
                logName, hostId, groupHostId, eventId, enableDirectP2P, bindPort);

            if (hostId == client.HostId)
            {
                client.P2PGroup.HostId = groupHostId;
                client.PeerUdpMagicNumber = peerUdpMagicNumber;
                client.P2PFirstFrameNumber = p2pFirstFrameNumber;

                client.StartReliableUdpLoop();
            }
            else
            {
                var newMember = new P2PMember(client, client.P2PGroup.HostId, hostId)
                {
                    PeerUdpMagicNumber = peerUdpMagicNumber,
                    EnableDirectP2P = enableDirectP2P,
                    PeerBindPort = bindPort
                };
                if (sessionKey != null && fastSessionKey != null)
                {
                    newMember.PeerCrypt = new NetCrypt(sessionKey.GetBuffer());
                    newMember.PeerCrypt.InitializeFastEncryption(fastSessionKey.GetBuffer());
                }

                newMember.InitializeReliableUdp(client.P2PFirstFrameNumber, p2pFirstFrameNumber);

                client.P2PGroup.P2PMembersInternal.TryAdd(hostId, newMember);
                client.OnP2PMemberJoin(hostId);
                client.OnP2PMemberRelayConnected(hostId);

                if (!enableDirectP2P)
                {
                    client.Logger.Verbose(
                        "ProcessP2PGroupMemberJoin => skipping P2P UDP socket creation for hostId = {HostId}, enableDirectP2P = false",
                        hostId);

                    client.RmiToServer(new P2PGroup_MemberJoin_Ack
                    {
                        GroupHostId = groupHostId,
                        AddedMemberHostId = hostId,
                        EventId = eventId,
                        LocalPortReuseSuccess = false
                    });
                }
                else
                {
                    Task.Run(async () =>
                    {
                        bool localPortReuseSuccess = false;
                        await using (await newMember.P2PMutex.EnterAsync())
                        {
                            try
                            {
                                int? targetPort = bindPort > 0 ? bindPort : null;

                                if (newMember.IsClosed || newMember.PeerUdpChannel != null)
                                {
                                    client.RmiToServer(new P2PGroup_MemberJoin_Ack
                                    {
                                        GroupHostId = groupHostId,
                                        AddedMemberHostId = hostId,
                                        EventId = eventId,
                                        LocalPortReuseSuccess = false
                                    });
                                    return;
                                }

                                (var channel, int port, _) =
                                    await client.ConnectUdpAsync(targetPort);
                                newMember.PeerUdpChannel = channel;
                                newMember.SelfUdpLocalSocket = new IPEndPoint(client.LocalIP, port);

                                if (bindPort != 0 && port == bindPort)
                                {
                                    localPortReuseSuccess = true;
                                    newMember.LocalPortReuseSuccess = true;
                                }

                                client.Logger.Debug(
                                    "ProcessP2PGroupMemberJoin => created P2P UDP socket for hostId = {HostId}, port = {Port}, localPortReuseSuccess = {LocalPortReuseSuccess}",
                                    hostId, port, localPortReuseSuccess);
                            }
                            catch (Exception ex)
                            {
                                client.Logger.Error(ex,
                                    "ProcessP2PGroupMemberJoin => failed to create P2P UDP socket for hostId = {HostId}",
                                    hostId);
                            }

                            client.RmiToServer(new P2PGroup_MemberJoin_Ack
                            {
                                GroupHostId = groupHostId,
                                AddedMemberHostId = hostId,
                                EventId = eventId,
                                LocalPortReuseSuccess = localPortReuseSuccess
                            });
                        }
                    });
                }
            }

            if (hostId == client.HostId)
                client.RmiToServer(new P2PGroup_MemberJoin_Ack
                {
                    GroupHostId = groupHostId,
                    AddedMemberHostId = hostId,
                    EventId = eventId,
                    LocalPortReuseSuccess = false
                });
        }

        private static void RmiHandler(NetClient client, NetMessage message, ushort filterTag, IPEndPoint udpEndPoint)
        {
            if (!message.Read<NexumOpCode>(out var rmiId))
                return;

            switch (rmiId)
            {
                case NexumOpCode.P2PGroup_MemberJoin:
                case NexumOpCode.P2PGroup_MemberJoin_Unencrypted:
                {
                    HandleP2PGroupMemberJoin(client, message, rmiId);
                    break;
                }

                case NexumOpCode.P2PGroup_MemberLeave:
                {
                    if (!P2PGroup_MemberLeave.Deserialize(message, out var packet))
                        return;

                    client.Logger.Debug(
                        "P2PGroup_MemberLeave => hostId = {HostId}, groupHostId = {GroupHostId}",
                        packet.HostId, packet.GroupHostId);

                    if (packet.GroupHostId == client.P2PGroup.HostId)
                    {
                        client.P2PGroup.P2PMembersInternal.TryRemove(packet.HostId, out var p2pMember);
                        if (p2pMember != null)
                        {
                            p2pMember.Close(p2pMember.DirectP2P);

                            if (p2pMember.DirectP2P)
                                client.OnP2PMemberDirectDisconnected(packet.HostId);
                            else
                                client.OnP2PMemberRelayDisconnected(packet.HostId);

                            client.OnP2PMemberLeave(packet.HostId);
                        }
                    }

                    break;
                }

                case NexumOpCode.P2P_NotifyDirectP2PDisconnected2:
                {
                    if (!P2P_NotifyDirectP2PDisconnected2.Deserialize(message, out var packet))
                        return;

                    client.Logger.Debug(
                        "P2P_NotifyDirectP2PDisconnected2 => hostId = {HostId}, reason = {Reason}",
                        packet.HostId,
                        packet.Reason
                    );

                    if (client.P2PGroup.P2PMembersInternal.TryGetValue(packet.HostId, out var p2pMember))
                    {
                        p2pMember.HandleRemoteDisconnect();
                    }
                    else
                    {
                        if (packet.HostId != client.HostId)
                            client.Logger.Warning(
                                "P2P_NotifyDirectP2PDisconnected2 => P2P member not found for hostId = {HostId}",
                                packet.HostId);
                    }

                    break;
                }

                case NexumOpCode.S2C_RequestCreateUdpSocket:
                {
                    if (!message.ReadStringEndPoint(out var udpSocket))
                        return;

                    var serverTcpAddress = ((IPEndPoint)client.Channel.RemoteAddress).Address.MapToIPv4();
                    var actualUdpEndpoint = new IPEndPoint(serverTcpAddress, udpSocket.Port);

                    client.Logger.Debug("S2C_RequestCreateUdpSocket => server sent {SentSocket}, using {ActualSocket}",
                        udpSocket, actualUdpEndpoint);

                    client.ServerUdpSocket = actualUdpEndpoint;

                    Task.Run(async () =>
                    {
                        try
                        {
                            if (client.UdpChannel != null)
                            {
                                client.Logger.Verbose(
                                    "S2C_RequestCreateUdpSocket => UDP socket already exists, sending ack");
                                client.RmiToServer(new C2S_CreateUdpSocketAck { Success = true });
                                return;
                            }

                            var (channel, _, _) = await client.ConnectUdpAsync();
                            client.UdpChannel = channel;

                            client.RmiToServer(new C2S_CreateUdpSocketAck { Success = true });
                        }
                        catch (Exception ex)
                        {
                            client.Logger.Error(ex, "S2C_RequestCreateUdpSocket => failed to create UDP socket");
                            client.RmiToServer(new C2S_CreateUdpSocketAck { Success = false });
                        }
                    });

                    break;
                }

                case NexumOpCode.S2C_CreateUdpSocketAck:
                {
                    if (!S2C_CreateUdpSocketAck.Deserialize(message, out var packet) || !packet.Result)
                        return;

                    var serverTcpAddress = ((IPEndPoint)client.Channel.RemoteAddress).Address;
                    var actualUdpEndpoint = new IPEndPoint(serverTcpAddress, packet.UdpSocket.Port);

                    client.Logger.Debug(
                        "S2C_CreateUdpSocketAck => server sent {SentSocket}, using {ActualSocket}", packet.UdpSocket,
                        actualUdpEndpoint);

                    client.ServerUdpSocket = actualUdpEndpoint;

                    Task.Run(async () =>
                    {
                        if (client.UdpChannel == null)
                            try
                            {
                                var (channel, _, _) = await client.ConnectUdpAsync();
                                client.UdpChannel = channel;

                                client.Logger.Debug("S2C_CreateUdpSocketAck => UDP socket created");
                            }
                            catch (Exception ex)
                            {
                                client.Logger.Error(ex, "S2C_CreateUdpSocketAck => failed to create UDP socket");
                            }
                    });

                    break;
                }

                case NexumOpCode.P2PRecycleComplete:
                {
                    if (!P2PRecycleComplete.Deserialize(message, out var packet))
                        return;

                    client.Logger.Debug("P2PRecycleComplete => hostId = {HostId}, recycled = {Recycled}", packet.HostId,
                        packet.Recycled);

                    if (packet.Recycled)
                    {
                        if (!client.P2PGroup.P2PMembersInternal.TryGetValue(packet.HostId, out var p2pMember))
                        {
                            if (packet.HostId != client.HostId)
                                client.Logger.Warning(
                                    "P2PRecycleComplete => P2P member not found for hostId = {HostId}", packet.HostId);
                            break;
                        }

                        var internalAddr = packet.InternalAddr;
                        var externalAddr = packet.ExternalAddr;
                        var sendAddr = packet.SendAddr;
                        var recvAddr = packet.RecvAddr;
                        uint hostId = packet.HostId;

                        Task.Run(async () =>
                        {
                            await using (await p2pMember.P2PMutex.EnterAsync())
                            {
                                if (p2pMember.IsClosed)
                                    return;

                                if (p2pMember.PeerUdpChannel == null || !p2pMember.PeerUdpChannel.Active)
                                {
                                    int? targetPort = p2pMember.SelfUdpLocalSocket?.Port;
                                    if (!targetPort.HasValue && p2pMember.PeerBindPort > 0)
                                        targetPort = p2pMember.PeerBindPort;

                                    (var channel, int port, bool portReuseSuccess) =
                                        await client.ConnectUdpAsync(targetPort);
                                    p2pMember.PeerUdpChannel = channel;
                                    p2pMember.SelfUdpLocalSocket = new IPEndPoint(client.LocalIP, port);
                                    p2pMember.LocalPortReuseSuccess = portReuseSuccess;
                                }

                                p2pMember.DirectP2P = true;
                                p2pMember.LastUdpReceivedTime = client.GetAbsoluteTime();
                                p2pMember.P2PHolepunchInitiated = false;
                                p2pMember.P2PHolepunchNotified = false;
                                p2pMember.P2PHolepunchStarted = false;

                                p2pMember.PeerUdpLocalSocket = internalAddr;
                                p2pMember.PeerUdpSocket = externalAddr;
                                p2pMember.PeerLocalToRemoteSocket = sendAddr;
                                p2pMember.PeerRemoteToLocalSocket = recvAddr;

                                if (p2pMember.ToPeerReliableUdp == null)
                                    p2pMember.ReinitializeReliableUdp();

                                p2pMember.StartReliableUdpScheduler();

                                client.Logger.Debug(
                                    "P2PRecycleComplete => recycled=true, Direct P2P restored for hostId = {HostId}, internalAddr = {InternalAddr}, externalAddr = {ExternalAddr}, sendAddr = {SendAddr}, recvAddr = {RecvAddr}",
                                    hostId, internalAddr, externalAddr, sendAddr, recvAddr);

                                client.OnP2PMemberRelayDisconnected(hostId);
                                client.OnP2PMemberDirectConnected(hostId);
                            }
                        });
                    }
                    else
                    {
                        if (client.NetSettings?.DirectP2PStartCondition == DirectP2PStartCondition.Jit)
                            client.Logger.Verbose(
                                "P2PRecycleComplete => JIT mode, not auto-triggering direct P2P for hostId = {HostId}",
                                packet.HostId);
                        else
                            client.RmiToServer(new NotifyJitDirectP2PTriggered { HostId = packet.HostId });
                    }

                    break;
                }

                case NexumOpCode.NewDirectP2PConnection:
                {
                    if (!NewDirectP2PConnection.Deserialize(message, out var packet))
                        return;

                    Guid magicNumber;
                    lock (client.UdpHolepunchLock)
                    {
                        if (!client.UdpEnabled)
                        {
                            if (client.PendingP2PConnections.TryAdd(packet.HostId, 0))
                                client.Logger.Debug(
                                    "NewDirectP2PConnection => hostId = {HostId}, queueing - UDP not enabled yet",
                                    packet.HostId);

                            return;
                        }

                        magicNumber = client.UdpMagicNumber;
                    }

                    if (!client.P2PGroup.P2PMembersInternal.TryGetValue(packet.HostId, out var targetMember))
                    {
                        if (packet.HostId != client.HostId)
                            client.Logger.Warning(
                                "NewDirectP2PConnection => P2P member not found for hostId = {HostId}",
                                packet.HostId);
                        return;
                    }

                    client.Logger.Debug("NewDirectP2PConnection => hostId = {HostId}", packet.HostId);

                    using (targetMember.P2PMutex.Enter())
                    {
                        if (targetMember.P2PHolepunchInitiated)
                        {
                            client.Logger.Verbose(
                                "NewDirectP2PConnection => hostId = {HostId}, skipping - holepunch already in progress",
                                packet.HostId);
                            return;
                        }

                        targetMember.P2PHolepunchInitiated = true;
                    }

                    var peerUdpServerHolepunch = new PeerUdp_ServerHolepunch
                    {
                        MagicNumber = magicNumber,
                        TargetHostId = packet.HostId
                    }.Serialize();

                    client.NexumToServerUdpIfAvailable(peerUdpServerHolepunch, true);
                    var capturedMember = targetMember;
                    HolepunchHelper.SendBurstMessagesWithCheck(
                        peerUdpServerHolepunch,
                        burstMsg => client.NexumToServerUdpIfAvailable(burstMsg, true),
                        () => !capturedMember.DirectP2P && !capturedMember.IsClosed
                    );
                    break;
                }

                case NexumOpCode.RequestP2PHolepunch:
                {
                    if (!RequestP2PHolepunch.Deserialize(message, out var packet))
                        return;

                    var localEndpoint = packet.LocalEndPoint;
                    var remoteEndpoint = packet.ExternalEndPoint;

                    client.Logger.Debug(
                        "RequestP2PHolepunch => hostId = {HostId}, localEndpoint = {LocalEndpoint}, remoteEndpoint = {RemoteEndpoint}",
                        packet.HostId, localEndpoint, remoteEndpoint);

                    if (!client.P2PGroup.P2PMembersInternal.TryGetValue(packet.HostId, out var p2pMember))
                    {
                        if (packet.HostId != client.HostId)
                            client.Logger.Warning("RequestP2PHolepunch => P2P member not found for hostId = {HostId}",
                                packet.HostId);
                        break;
                    }

                    Guid capturedPeerMagicNumber;

                    using (p2pMember.P2PMutex.Enter())
                    {
                        if (p2pMember.P2PHolepunchStarted || p2pMember.DirectP2P)
                        {
                            client.Logger.Verbose(
                                "RequestP2PHolepunch => skipping for hostId = {HostId}, P2PHolepunchStarted = {Started}, DirectP2P = {DirectP2P}",
                                packet.HostId, p2pMember.P2PHolepunchStarted, p2pMember.DirectP2P);
                            break;
                        }

                        p2pMember.P2PHolepunchStarted = true;
                        p2pMember.PeerUdpSocket = remoteEndpoint;
                        p2pMember.PeerUdpLocalSocket = localEndpoint;
                        capturedPeerMagicNumber = p2pMember.PeerUdpMagicNumber;
                    }

                    var capturedMember = p2pMember;
                    Task.Run(async () =>
                    {
                        bool channelReady = await HolepunchHelper.WaitForConditionWithBackoffAsync(
                            () => capturedMember.PeerUdpChannel != null,
                            () => capturedMember.IsClosed || capturedMember.DirectP2P
                        );

                        if (!channelReady)
                            return;

                        bool hasLocalEndpoint = NetUtil.IsUnicastEndpoint(localEndpoint);
                        bool hasPublicEndpoint = NetUtil.IsUnicastEndpoint(remoteEndpoint);

                        var msgToLocalEndpoint = hasLocalEndpoint
                            ? new PeerUdp_PeerHolepunch
                            {
                                HostId = client.HostId, PeerMagicNumber = capturedPeerMagicNumber,
                                ServerInstanceGuid = client.ServerInstanceGuid, TargetEndpoint = localEndpoint
                            }.Serialize()
                            : null;

                        NetMessage msgToRemoteEndpoint = null;
                        IPEndPoint[] predictedEndpoints = null;
                        NetMessage[] predictedMessages = null;

                        if (hasPublicEndpoint)
                        {
                            msgToRemoteEndpoint = new PeerUdp_PeerHolepunch
                            {
                                HostId = client.HostId, PeerMagicNumber = capturedPeerMagicNumber,
                                ServerInstanceGuid = client.ServerInstanceGuid, TargetEndpoint = remoteEndpoint
                            }.Serialize();

                            predictedEndpoints = HolepunchHelper.GeneratePredictedEndpoints(
                                remoteEndpoint,
                                HolepunchConfig.NatPortShotgunTrialCount,
                                HolepunchConfig.NatPortShotgunRange);

                            predictedMessages = new NetMessage[predictedEndpoints.Length];
                            for (int i = 0; i < predictedEndpoints.Length; i++)
                                predictedMessages[i] = new PeerUdp_PeerHolepunch
                                {
                                    HostId = client.HostId, PeerMagicNumber = capturedPeerMagicNumber,
                                    ServerInstanceGuid = client.ServerInstanceGuid,
                                    TargetEndpoint = predictedEndpoints[i]
                                }.Serialize();
                        }

                        for (int burst = 0; burst < HolepunchConfig.BurstCount; burst++)
                        {
                            if (capturedMember.IsClosed || capturedMember.DirectP2P)
                                return;

                            if (hasLocalEndpoint)
                                capturedMember.NexumToPeer(msgToLocalEndpoint, localEndpoint);

                            if (hasPublicEndpoint)
                            {
                                capturedMember.NexumToPeer(msgToRemoteEndpoint, remoteEndpoint);

                                for (int i = 0; i < predictedEndpoints.Length; i++)
                                    capturedMember.NexumToPeer(predictedMessages[i], predictedEndpoints[i]);
                            }

                            if (burst < HolepunchConfig.BurstCount - 1)
                                await Task.Delay(HolepunchConfig.BurstDelayMs);
                        }
                    });

                    break;
                }

                case NexumOpCode.NotifyDirectP2PEstablish:
                {
                    if (!NotifyDirectP2PEstablish.Deserialize(message, out var packet))
                        return;

                    uint hostIdA = packet.HostIdA;
                    uint hostIdB = packet.HostIdB;
                    var aSendAddrToB = packet.ASendAddrToB;
                    var bRecvAddrFromA = packet.BRecvAddrFromA;
                    var bSendAddrToA = packet.BSendAddrToA;
                    var aRecvAddrFromB = packet.ARecvAddrFromB;

                    if (client.HostId == hostIdB)
                    {
                        Utilities.SysUtil.Swap(ref hostIdA, ref hostIdB);
                        Utilities.SysUtil.Swap(ref aSendAddrToB, ref bSendAddrToA);
                        Utilities.SysUtil.Swap(ref aRecvAddrFromB, ref bRecvAddrFromA);
                    }

                    var peerLocalToRemoteSocket = aSendAddrToB;
                    var peerRemoteToLocalSocket = aRecvAddrFromB;
                    var selfLocalToRemoteSocket = bSendAddrToA;
                    var selfRemoteToLocalSocket = bRecvAddrFromA;

                    if (client.P2PGroup.P2PMembersInternal.TryGetValue(hostIdB, out var p2pMember))
                    {
                        using (p2pMember.P2PMutex.Enter())
                        {
                            if (p2pMember.DirectP2P)
                            {
                                client.Logger.Verbose(
                                    "NotifyDirectP2PEstablish => already established with hostId = {HostId}", hostIdB);
                                return;
                            }

                            p2pMember.DirectP2P = true;
                            p2pMember.LastUdpReceivedTime = client.GetAbsoluteTime();
                            p2pMember.PeerLocalToRemoteSocket = peerLocalToRemoteSocket;
                            p2pMember.PeerRemoteToLocalSocket = peerRemoteToLocalSocket;
                            p2pMember.SelfLocalToRemoteSocket = selfLocalToRemoteSocket;
                            p2pMember.SelfRemoteToLocalSocket = selfRemoteToLocalSocket;

                            if (p2pMember.ToPeerReliableUdp == null)
                                p2pMember.ReinitializeReliableUdp();

                            p2pMember.StartReliableUdpScheduler();
                        }

                        client.Logger.Debug(
                            "Direct P2P established => hostId = {HostIdB}, peerLocalToRemoteSocket = {PeerLocalToRemoteSocket}, selfLocalToRemoteSocket = {SelfLocalToRemoteSocket}",
                            hostIdB,
                            peerLocalToRemoteSocket,
                            selfLocalToRemoteSocket
                        );

                        client.OnP2PMemberRelayDisconnected(hostIdB);
                        client.OnP2PMemberDirectConnected(hostIdB);
                    }

                    break;
                }

                case NexumOpCode.NotifyUdpToTcpFallbackByServer:
                {
                    client.CloseUdp();
                    client.RmiToServer(new NotifyUdpToTcpFallbackByClient());
                    break;
                }

                case NexumOpCode.RenewP2PConnectionState:
                {
                    if (!RenewP2PConnectionState.Deserialize(message, out var packet))
                        return;

                    if (client.P2PGroup.P2PMembersInternal.TryGetValue(packet.HostId, out var p2pMember))
                    {
                        client.Logger.Debug("RenewP2PConnectionState => hostId = {HostId}", packet.HostId);

                        uint hostId = packet.HostId;
                        Task.Run(async () =>
                        {
                            await using (await p2pMember.P2PMutex.EnterAsync())
                            {
                                p2pMember.Close(p2pMember.DirectP2P);
                                p2pMember.IsClosed = false;

                                if (p2pMember.PeerUdpChannel == null)
                                {
                                    (var channel, int port, _) =
                                        await client.ConnectUdpAsync();
                                    p2pMember.PeerUdpChannel = channel;
                                    client.Logger.Debug(
                                        "RenewP2PConnectionState => pre-created P2P UDP socket on port {Port}", port);
                                }

                                client.RmiToServer(new NotifyJitDirectP2PTriggered { HostId = hostId });
                            }
                        });
                    }

                    break;
                }

                case NexumOpCode.ShutdownTcpAck:
                {
                    client.Logger.Debug("ShutdownTcpAck");
                    client.RmiToServer(new ShutdownTcpHandshake());
                    break;
                }

                case NexumOpCode.ReliablePong:
                {
                    break;
                }

                case NexumOpCode.HolsterP2PHolepunchTrial:
                {
                    var p2pMember =
                        client.P2PGroup?.FindMember(client.HostId, udpEndPoint, filterTag, message.RelayFrom);
                    if (p2pMember != null)
                    {
                        client.Logger.Debug(
                            "HolsterP2PHolepunchTrial => peer {HostId} requested to stop holepunch trial",
                            p2pMember.HostId);

                        using (p2pMember.P2PMutex.Enter())
                        {
                            p2pMember.P2PHolepunchStarted = false;
                            p2pMember.P2PHolepunchInitiated = false;
                        }
                    }

                    break;
                }

                case NexumOpCode.ReportServerTimeAndFrameRateAndPing:
                {
                    if (!ReportServerTimeAndFrameRatePing.Deserialize(message, out var packet))
                        break;

                    var p2pMember =
                        client.P2PGroup?.FindMember(client.HostId, udpEndPoint, filterTag, message.RelayFrom);
                    if (p2pMember != null)
                    {
                        p2pMember.PeerFrameRateInternal = packet.PeerFrameRate;

                        p2pMember.RmiToPeer(new ReportServerTimeAndFrameRatePong
                            {
                                OriginalClientLocalTime = packet.ClientLocalTime,
                                PeerLocalTime = client.GetAbsoluteTime(),
                                PeerServerPing = client.ServerUdpRecentPing,
                                PeerFrameRate = client.RecentFrameRate
                            },
                            reliable: true);
                    }

                    break;
                }

                case NexumOpCode.ReportServerTimeAndFrameRateAndPong:
                {
                    if (!ReportServerTimeAndFrameRatePong.Deserialize(message, out var packet))
                        break;

                    var p2pMember =
                        client.P2PGroup?.FindMember(client.HostId, udpEndPoint, filterTag, message.RelayFrom);
                    if (p2pMember != null)
                    {
                        double currentTime = client.GetAbsoluteTime();
                        double peerToServerPing = Math.Max(packet.PeerServerPing, 0.0);

                        if (p2pMember.LastPeerServerPing > 0)
                            p2pMember.PeerServerJitterInternal = Nexum.Core.Utilities.NetUtil.CalculateJitter(
                                p2pMember.PeerServerJitterInternal, peerToServerPing, p2pMember.LastPeerServerPing);

                        p2pMember.LastPeerServerPing = peerToServerPing;
                        p2pMember.PeerServerPingInternal = peerToServerPing;
                        p2pMember.PeerFrameRateInternal = packet.PeerFrameRate;

                        double estimatedPeerTime = packet.PeerLocalTime + p2pMember.RecentPing;
                        p2pMember.IndirectServerTimeDiff = currentTime - estimatedPeerTime;
                    }

                    break;
                }

                default:
                    client.OnRmiReceive(message, (ushort)rmiId);
                    break;
            }
        }

        private static void NotifyServerConnectionHintHandler(NetClient client, NetMessage message)
        {
            client.Logger.Debug("NotifyServerConnectionHint");

            if (!NotifyServerConnectionHint.Deserialize(message, out var packet))
                return;

            var settings = new NetSettings
            {
                EnableServerLog = packet.EnableServerLog,
                FallbackMethod = packet.FallbackMethod,
                MessageMaxLength = packet.MessageMaxLength,
                IdleTimeout = packet.IdleTimeout,
                DirectP2PStartCondition = packet.DirectP2PStartCondition,
                OverSendSuspectingThresholdInBytes = packet.OverSendSuspectingThresholdInBytes,
                EnableNagleAlgorithm = packet.EnableNagleAlgorithm,
                EncryptedMessageKeyLength = packet.EncryptedMessageKeyLength,
                FastEncryptedMessageKeyLength = packet.FastEncryptedMessageKeyLength,
                AllowServerAsP2PGroupMember = packet.AllowServerAsP2PGroupMember,
                EnableP2PEncryptedMessaging = packet.EnableP2PEncryptedMessaging,
                UpnpDetectNatDevice = packet.UpnpDetectNatDevice,
                UpnpTcpAddrPortMapping = packet.UpnpTcpAddrPortMapping,
                EnableLookaheadP2PSend = packet.EnableLookaheadP2PSend,
                EnablePingTest = packet.EnablePingTest,
                EmergencyLogLineCount = packet.EmergencyLogLineCount
            };

            client.NetSettings = settings;

            client.Crypt = new NetCrypt(settings.EncryptedMessageKeyLength, settings.FastEncryptedMessageKeyLength);

            byte[] encodedRsaKey = packet.RsaPublicKey.GetBuffer();

            if (client.PinnedServerPublicKey != null)
            {
                if (!client.ValidateServerPublicKey(encodedRsaKey))
                {
                    client.Logger.Error(
                        "Certificate pinning validation failed - server public key does not match pinned key. Possible MITM attack!");
                    client.Channel?.CloseAsync();
                    return;
                }

                client.Logger.Verbose("Certificate pinning validation successful");
            }

            var rsaSequence = (DerSequence)Asn1Object.FromByteArray(encodedRsaKey);

            var rsaParameters = new RSAParameters
            {
                Exponent = ((DerInteger)rsaSequence[1]).Value.ToByteArrayUnsigned(),
                Modulus = ((DerInteger)rsaSequence[0]).Value.ToByteArrayUnsigned()
            };

            client.RSA = RSA.Create();
            client.RSA.ImportParameters(rsaParameters);

            client.NexumToServer(new NotifyCSEncryptedSessionKey
            {
                EncryptedSessionKey =
                    new ByteArray(client.RSA.Encrypt(client.Crypt.GetKey(), RSAEncryptionPadding.OaepSHA256), true),
                EncryptedFastSessionKey = new ByteArray(client.Crypt.EncryptKey(client.Crypt.GetFastKey()), true)
            });
        }

        private static void NotifyCSSessionKeySuccessHandler(NetClient client)
        {
            client.Logger.Debug("NotifyCSSessionKeySuccess");

            client.NexumToServer(new NotifyServerConnectionRequestData
            {
                UserData = new ByteArray(),
                ProtocolVersion = client.ServerGuid,
                InternalVersion = Constants.NetVersion
            });
        }

        private static void NotifyServerConnectSuccessHandler(NetClient client, NetMessage message)
        {
            if (!NotifyServerConnectSuccess.Deserialize(message, out var packet))
                return;

            client.Logger.Debug(
                "NotifyServerConnectSuccess => hostId = {HostId}, serverInstanceGuid = {ServerInstanceGuid}, endpoint = {Endpoint}",
                packet.HostId, packet.ServerInstanceGuid, packet.ServerEndPoint);

            client.HostId = packet.HostId;
            client.ServerInstanceGuid = packet.ServerInstanceGuid;
            client.UpdateLoggerContext($"{client.ServerName}Client({packet.HostId})");
            client.Channel.Pipeline.Get<NetClientAdapter>()
                ?.UpdateLoggerContext($"{client.ServerName}ClientAdapter({packet.HostId})");
            client.SetConnectionState(ConnectionState.Connected);

            double reliablePingInterval = (client.NetSettings?.IdleTimeout ?? NetConfig.NoPingTimeoutTime) * 0.3;
            client.StartReliablePingLoop(reliablePingInterval);

            client.OnConnected();
        }

        private static void UnreliableRelay2Handler(NetClient client, NetMessage message)
        {
            if (!UnreliableRelay2.Deserialize(message, out var packet))
                return;

            var relayedMessage = new NetMessage(packet.Data) { RelayFrom = packet.HostId };
            ReadMessage(client, relayedMessage);
        }

        private static void ReliableRelay2Handler(NetClient client, NetMessage message)
        {
            if (!ReliableRelay2.Deserialize(message, out var packet))
                return;

            if (!ReliableUdpHelper.UnwrapPayload(packet.Data.GetBufferUnsafe(), out var unwrappedData))
                return;

            var relayedMessage = new NetMessage(unwrappedData.ToArray(), true) { RelayFrom = packet.HostId };
            ReadMessage(client, relayedMessage);
        }

        private static void UnreliablePongHandler(NetClient client, NetMessage message)
        {
            if (!message.Read(out double sentTime) || !message.Read(out double serverTime))
                return;

            double currentTime = client.GetAbsoluteTime();
            double lastPing = (currentTime - sentTime) / 2.0;

            if (lastPing < 0)
                lastPing = 0;

            if (client.ServerUdpLastPing > 0)
                client.ServerUdpJitter =
                    Nexum.Core.Utilities.NetUtil.CalculateJitter(client.ServerUdpJitter, lastPing,
                        client.ServerUdpLastPing);

            client.ServerUdpLastPing = lastPing;
            client.ServerUdpLastReceivedTime = currentTime;
            client.ServerUdpRecentPing = client.ServerUdpRecentPing != 0.0
                ? SysUtil.Lerp(client.ServerUdpRecentPing, lastPing, ReliableUdpConfig.LagLinearProgrammingFactor)
                : lastPing;

            double estimatedServerTime = serverTime + client.ServerUdpRecentPing;
            client.ServerTimeDiff = currentTime - estimatedServerTime;

            if (message.Read(out int paddingSize) && paddingSize > 0)
                client.ServerMtuDiscovery.OnPongReceived(paddingSize, currentTime);
        }

        private static void EncryptedHandler(NetClient client, NetMessage message, ushort filterTag,
            IPEndPoint udpEndPoint)
        {
            var crypt = client.Crypt;

            var p2PMember = client.P2PGroup?.FindMember(client.HostId, udpEndPoint, filterTag, message.RelayFrom);
            if (p2PMember?.PeerCrypt != null)
                crypt = p2PMember.PeerCrypt;

            NetCoreHandler.HandleEncrypted(
                message,
                crypt,
                decryptedMsg => ReadMessage(client, decryptedMsg, filterTag, udpEndPoint)
            );
        }

        private static void CompressedHandler(NetClient client, NetMessage message, ushort filterTag,
            IPEndPoint udpEndPoint)
        {
            NetCoreHandler.HandleCompressed(
                message,
                client.Logger,
                decompressedMsg => ReadMessage(client, decompressedMsg, filterTag, udpEndPoint)
            );
        }
    }
}
