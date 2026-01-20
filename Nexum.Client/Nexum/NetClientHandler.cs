using System;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BaseLib;
using Nexum.Core;
using Org.BouncyCastle.Asn1;

namespace Nexum.Client
{
    internal static class NetClientHandler
    {
        internal static void ReadFrame(NetClient client, NetMessage message, ushort filterTag = 0,
            IPEndPoint udpEndPoint = null,
            bool bypass = false)
        {
            lock (client.RecvLock)
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
        }

        internal static void ReadMessage(NetClient client, NetMessage message, ushort filterTag = 0,
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
                    RMIHandler(client, message, filterTag, udpEndPoint);
                    break;

                case MessageType.Encrypted:
                    EncryptedHandler(client, message);
                    break;

                case MessageType.Compressed:
                    CompressedHandler(client, message);
                    break;

                case MessageType.NotifyServerConnectionHint:
                    NotifyServerConnectionHintHandler(client, message);
                    break;

                case MessageType.NotifyServerConnectSuccess:
                    NotifyServerConnectSuccessHandler(client, message);
                    break;

                case MessageType.NotifyCSSessionKeySuccess:
                    NotifyCSSessionKeySuccessHandler(client);
                    break;

                case MessageType.ReliableRelay2:
                    message.Reliable = true;
                    ReliableRelay2Handler(client, message);
                    break;

                case MessageType.UnreliableRelay2:
                    message.Reliable = false;
                    UnreliableRelay2Handler(client, message);
                    break;

                case MessageType.RequestStartServerHolepunch:
                    RequestStartServerHolepunchHandler(client, message);
                    break;

                case MessageType.ServerHolepunchAck:
                    ServerHolepunchAckHandler(client, message);
                    break;

                case MessageType.NotifyClientServerUdpMatched:
                    NotifyClientServerUdpMatchedHandler(client, message);
                    break;

                case MessageType.PeerUdp_ServerHolepunchAck:
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
                    UnreliablePongHandler(client, message);
                    break;

                case MessageType.ConnectServerTimedout:
                    client.Logger.Warning("Connection timed out");
                    break;

                case MessageType.NotifyProtocolVersionMismatch:
                    client.Logger.Warning("Protocol version mismatch - server rejected connection");
                    break;

                case MessageType.NotifyServerDeniedConnection:
                    client.Logger.Warning("Server denied connection");
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
                if (client.ToServerReliableUdp != null)
                {
                    client.ToServerReliableUdp.TakeReceivedFrame(frame);
                    ExtractMessagesFromServerReliableUdpStream(client, filterTag, udpEndPoint);
                }
                else
                {
                    if (frame.Type == ReliableUdpFrameType.Data && frame.Data != null)
                        if (ReliableUdpHelper.UnwrapPayload(frame.Data, out byte[] payload))
                        {
                            var innerMessage = new NetMessage(payload, true);
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
            var stream = client.ToServerReliableUdp?.ReceivedStream;
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
                ReadMessage(client, innerMessage, filterTag, udpEndPoint);
            }
        }

        private static void ExtractMessagesFromP2PReliableUdpStream(NetClient client, P2PMember member,
            ushort filterTag,
            IPEndPoint udpEndPoint)
        {
            var stream = member.ToPeerReliableUdp?.ReceivedStream;
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
                ReadMessage(client, innerMessage, filterTag, udpEndPoint);
            }
        }

        private static void RequestStartServerHolepunchHandler(NetClient client, NetMessage message)
        {
            if (!message.Read(out Guid magicNumber))
                return;

            lock (client.UdpHolepunchLock)
            {
                if (client.UdpEnabled)
                    return;

                client.UdpMagicNumber = magicNumber;
            }

            client.Logger.Debug("RequestStartServerHolepunch => guid = {MagicNumber}", magicNumber);

            client.NexumToServerUdpIfAvailable(HolepunchHelper.CreateServerHolepunchMessage(magicNumber), true);
            HolepunchHelper.SendBurstMessagesWithCheck(
                () => HolepunchHelper.CreateServerHolepunchMessage(magicNumber),
                msg => client.NexumToServerUdpIfAvailable(msg, true),
                () => client.SelfUdpSocket == null,
                HolepunchConfig.UdpMatchedDelayMs
            );
        }

        private static void ServerHolepunchAckHandler(NetClient client, NetMessage message)
        {
            var selfUdpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);

            if (!message.Read(out Guid magicNumber) || !message.ReadIPEndPoint(ref selfUdpSocket))
                return;

            var udpChannel = client.UdpChannel;
            if (udpChannel == null)
                return;

            Guid capturedMagicNumber;
            IPEndPoint capturedSelfUdpSocket;

            lock (client.UdpHolepunchLock)
            {
                if (!magicNumber.Equals(client.UdpMagicNumber))
                {
                    client.Logger.Warning(
                        "ServerHolepunchAck => magic number mismatch, expected {Expected}, got {Actual}",
                        client.UdpMagicNumber, magicNumber);
                    return;
                }

                if (client.SelfUdpSocket != null)
                {
                    client.Logger.Debug("ServerHolepunchAck => SelfUdpSocket already set, ignoring duplicate");
                    return;
                }

                client.SelfUdpSocket = selfUdpSocket;

                capturedMagicNumber = client.UdpMagicNumber;
                capturedSelfUdpSocket = client.SelfUdpSocket;
            }

            client.Logger.Debug("ServerHolepunchAck => guid = {MagicNumber}, endpoint = {Endpoint}", magicNumber,
                selfUdpSocket);

            var notifyHolepunchSuccess = new NetMessage();
            notifyHolepunchSuccess.WriteEnum(MessageType.NotifyHolepunchSuccess);
            notifyHolepunchSuccess.Write(capturedMagicNumber);
            notifyHolepunchSuccess.Write(new IPEndPoint(client.LocalIP,
                ((IPEndPoint)udpChannel.LocalAddress).Port));
            notifyHolepunchSuccess.Write(capturedSelfUdpSocket);

            client.NexumToServer(notifyHolepunchSuccess);

            if (!client.PingLoop?.IsRunning ?? true)
            {
                if (client.PingLoop == null)
                    client.PingLoop = new ThreadLoop(TimeSpan.FromSeconds(ReliableUdpConfig.CsPingInterval),
                        client.SendUdpPing);

                client.PingLoop.Start();
            }
        }

        private static void NotifyClientServerUdpMatchedHandler(NetClient client, NetMessage message)
        {
            if (!message.Read(out Guid magicNumber))
                return;

            lock (client.UdpHolepunchLock)
            {
                if (client.UdpEnabled)
                    return;

                client.UdpMagicNumber = magicNumber;
                client.UdpEnabled = true;
                client.ServerUdpLastReceivedTime = client.GetAbsoluteTime();

                client.InitializeToServerReliableUdp(client.P2PFirstFrameNumber);

                client.StartReliableUdpLoop();
            }

            client.Logger.Information("UDP connection established with server, guid = {MagicNumber}", magicNumber);
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
                if (!client.P2PGroup.P2PMembers.TryGetValue(hostId, out var p2pMember))
                    continue;

                lock (p2pMember.P2PMutex)
                {
                    if (p2pMember.P2PHolepunchInitiated)
                        continue;

                    p2pMember.P2PHolepunchInitiated = true;
                }

                client.Logger.Debug("Processing pending P2P connection => hostId = {HostId}", hostId);

                var capturedMember = p2pMember;
                client.NexumToServerUdpIfAvailable(
                    HolepunchHelper.CreatePeerUdpServerHolepunchMessage(magicNumber, hostId), true);
                HolepunchHelper.SendBurstMessagesWithCheck(
                    () => HolepunchHelper.CreatePeerUdpServerHolepunchMessage(magicNumber, hostId),
                    msg => client.NexumToServerUdpIfAvailable(msg, true),
                    () => capturedMember.PeerUdpChannel == null && !capturedMember.IsClosed
                );
            }
        }

        private static void PeerUdpServerHolepunchAckHandler(NetClient client, NetMessage message)
        {
            var selfUdpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);

            if (!message.Read(out Guid magicNumber) || !message.ReadIPEndPoint(ref selfUdpSocket) ||
                !message.Read(out uint hostId))
                return;

            client.Logger.Debug(
                "PeerUdpServerHolepunchAck => guid = {MagicNumber}, endpoint = {Endpoint}, hostId = {HostId}",
                magicNumber,
                selfUdpSocket,
                hostId
            );

            IPEndPoint capturedSelfUdpSocket;
            lock (client.UdpHolepunchLock)
            {
                if (!magicNumber.Equals(client.UdpMagicNumber))
                {
                    client.Logger.Warning(
                        "PeerUdpServerHolepunchAck => magic number mismatch, expected {Expected}, got {Actual}",
                        client.UdpMagicNumber, magicNumber);
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

            if (!client.P2PGroup.P2PMembers.TryGetValue(hostId, out var p2pMember))
            {
                client.Logger.Warning("PeerUdpServerHolepunchAck => P2P member not found for hostId = {HostId}",
                    hostId);
                return;
            }

            Task.Run(() =>
            {
                lock (p2pMember.P2PMutex)
                {
                    if (p2pMember.IsClosed)
                        return;

                    int port;
                    if (p2pMember.PeerUdpChannel != null)
                    {
                        port = ((IPEndPoint)p2pMember.PeerUdpChannel.LocalAddress).Port;
                        client.Logger.Debug(
                            "PeerUdpServerHolepunchAck => reusing existing P2P UDP socket on port {Port}", port);
                    }
                    else
                    {
                        (var channel, var workerGroup, int newPort, _) = client.ConnectUdp();
                        p2pMember.PeerUdpChannel = channel;
                        p2pMember.PeerUdpEventLoopGroup = workerGroup;
                        port = newPort;
                    }

                    var peerUdpNotifyHolepunchSuccess = new NetMessage();
                    peerUdpNotifyHolepunchSuccess.WriteEnum(MessageType.PeerUdp_NotifyHolepunchSuccess);

                    p2pMember.SelfUdpLocalSocket = new IPEndPoint(client.LocalIP, port);
                    peerUdpNotifyHolepunchSuccess.Write(p2pMember.SelfUdpLocalSocket);

                    p2pMember.SelfUdpSocket = new IPEndPoint(capturedSelfUdpSocket.Address, port);
                    peerUdpNotifyHolepunchSuccess.Write(p2pMember.SelfUdpSocket);

                    peerUdpNotifyHolepunchSuccess.Write(hostId);
                    client.NexumToServer(peerUdpNotifyHolepunchSuccess);
                }
            });
        }

        private static void PeerUdpPeerHolepunchHandler(NetClient client, NetMessage message, IPEndPoint udpEndPoint)
        {
            var selfUdpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);

            if (!message.Read(out uint hostId)
                || !message.Read(out Guid magicNumber)
                || !message.Read(out Guid serverGuid)
                || !message.ReadIPEndPoint(ref selfUdpSocket))
                return;

            if (!serverGuid.Equals(client.ServerGuid))
            {
                client.Logger.Warning(
                    "PeerUdpPeerHolepunch => server GUID mismatch, expected {Expected}, got {Actual}",
                    client.ServerGuid, serverGuid);
                return;
            }

            if (!client.P2PGroup.P2PMembers.TryGetValue(hostId, out var p2pMember))
            {
                client.Logger.Warning("PeerUdpPeerHolepunch => P2P member not found for hostId = {HostId}", hostId);
                return;
            }

            lock (p2pMember.P2PMutex)
            {
                if (!magicNumber.Equals(client.PeerUdpMagicNumber))
                {
                    client.Logger.Debug(
                        "PeerUdpPeerHolepunch => peer magic number mismatch for hostId = {HostId}, expected {Expected}, got {Actual}",
                        hostId, client.PeerUdpMagicNumber, magicNumber);
                    return;
                }

                if (p2pMember.IsClosed || p2pMember.DirectP2P)
                {
                    client.Logger.Debug(
                        "PeerUdpPeerHolepunch => skipping for hostId = {HostId}, IsClosed = {IsClosed}, DirectP2P = {DirectP2P}",
                        hostId, p2pMember.IsClosed, p2pMember.DirectP2P);
                    return;
                }
            }

            client.Logger.Debug(
                "PeerUdpPeerHolepunch => hostId = {HostId}, guid = {MagicNumber}, serverGuid = {ServerGuid}, endpoint = {Endpoint}",
                hostId,
                magicNumber,
                serverGuid,
                selfUdpSocket
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
                IPEndPoint selfUdpSocketLocal;

                lock (capturedMember.P2PMutex)
                {
                    if (capturedMember.IsClosed || capturedMember.DirectP2P)
                        return;

                    peerUdpSocket = capturedMember.PeerUdpSocket;
                    peerUdpLocalSocket = capturedMember.PeerUdpLocalSocket;
                    selfUdpSocketLocal = capturedMember.SelfUdpLocalSocket;
                }

                bool hasLocalEndpoint = NetUtil.IsUnicastEndpoint(peerUdpLocalSocket);
                bool hasPublicEndpoint = NetUtil.IsUnicastEndpoint(peerUdpSocket);
                bool sameLan = hasLocalEndpoint
                               && NetUtil.IsUnicastEndpoint(selfUdpSocketLocal)
                               && NetUtil.IsSameLan(capturedMember.SelfUdpSocket, peerUdpSocket)
                               && NetUtil.IsSameLan(peerUdpLocalSocket, selfUdpSocketLocal);

                for (int burst = 0; burst < HolepunchConfig.BurstCount; burst++)
                {
                    if (capturedMember.IsClosed || capturedMember.DirectP2P)
                        return;

                    capturedMember.NexumToPeer(
                        HolepunchHelper.CreatePeerUdpPeerHolepunchAckMessage(
                            magicNumber, client.HostId, selfUdpSocket, udpEndPoint, udpEndPoint),
                        udpEndPoint);

                    if (sameLan && hasLocalEndpoint)
                        capturedMember.NexumToPeer(
                            HolepunchHelper.CreatePeerUdpPeerHolepunchAckMessage(
                                magicNumber, client.HostId, selfUdpSocket, udpEndPoint, peerUdpLocalSocket),
                            peerUdpLocalSocket);

                    if (hasPublicEndpoint)
                        capturedMember.NexumToPeer(
                            HolepunchHelper.CreatePeerUdpPeerHolepunchAckMessage(
                                magicNumber, client.HostId, selfUdpSocket, udpEndPoint, peerUdpSocket),
                            peerUdpSocket);

                    if (!sameLan && hasLocalEndpoint)
                        capturedMember.NexumToPeer(
                            HolepunchHelper.CreatePeerUdpPeerHolepunchAckMessage(
                                magicNumber, client.HostId, selfUdpSocket, udpEndPoint, peerUdpLocalSocket),
                            peerUdpLocalSocket);

                    if (burst < HolepunchConfig.BurstCount - 1)
                        await Task.Delay(HolepunchConfig.BurstDelayMs);
                }
            });
        }

        private static void PeerUdpPeerHolepunchAckHandler(NetClient client, NetMessage message, IPEndPoint udpEndPoint)
        {
            var endpointA = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
            var endpointB = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
            var endpointC = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);

            if (!message.Read(out Guid magicNumber)
                || !message.Read(out uint hostId)
                || !message.ReadIPEndPoint(ref endpointA)
                || !message.ReadIPEndPoint(ref endpointB)
                || !message.ReadIPEndPoint(ref endpointC))
                return;

            if (!client.P2PGroup.P2PMembers.TryGetValue(hostId, out var p2pMember))
            {
                client.Logger.Warning("PeerUdpPeerHolepunchAck => P2P member not found for hostId = {HostId}", hostId);
                return;
            }

            lock (p2pMember.P2PMutex)
            {
                if (!magicNumber.Equals(p2pMember.PeerUdpMagicNumber))
                {
                    client.Logger.Debug(
                        "PeerUdpPeerHolepunchAck => magic number mismatch for hostId = {HostId}, expected {Expected}, got {Actual}",
                        hostId, p2pMember.PeerUdpMagicNumber, magicNumber);
                    return;
                }

                if (p2pMember.P2PHolepunchNotified)
                {
                    client.Logger.Debug("PeerUdpPeerHolepunchAck => already notified for hostId = {HostId}", hostId);
                    return;
                }

                p2pMember.P2PHolepunchNotified = true;
            }

            client.Logger.Debug(
                "PeerUdpPeerHolepunchAck => guid = {MagicNumber}, hostId = {HostId}, endpointA = {EndpointA}, endpointB = {EndpointB}, endpointC = {EndpointC}",
                magicNumber,
                hostId,
                endpointA,
                endpointB,
                endpointC
            );

            var notifyP2PHolepunchSuccess = new NetMessage();
            notifyP2PHolepunchSuccess.Write(client.HostId);
            notifyP2PHolepunchSuccess.Write(hostId);
            notifyP2PHolepunchSuccess.Write(endpointA);
            notifyP2PHolepunchSuccess.Write(endpointB);
            notifyP2PHolepunchSuccess.Write(endpointC);
            notifyP2PHolepunchSuccess.Write(udpEndPoint);

            client.RmiToServer((ushort)NexumOpCode.NotifyP2PHolepunchSuccess, notifyP2PHolepunchSuccess);
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
                p2pReplyIndirectServerTimeAndPong.WriteEnum(MessageType.P2PReplyIndirectServerTimeAndPong);
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
                    p2pMember.JitterInternal = Core.NetUtil.CalculateJitter(p2pMember.JitterInternal, lastPing,
                        p2pMember.LastPingInternal);

                p2pMember.LastPingInternal = lastPing;
                p2pMember.LastUdpReceivedTime = currentTime;

                p2pMember.RecentPing = p2pMember.RecentPing != 0.0
                    ? Core.SysUtil.Lerp(p2pMember.RecentPing, lastPing, ReliableUdpConfig.LagLinearProgrammingFactor)
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
            var customField = new ByteArray();

            if (!message.Read(out uint groupHostId) || !message.Read(out uint memberId) ||
                !message.Read(ref customField))
                return;

            if (opCode == NexumOpCode.P2PGroup_MemberJoin)
            {
                var sessionKey = new ByteArray();
                var fastSessionKey = new ByteArray();
                if (!message.Read(out uint eventId)
                    || !message.Read(ref sessionKey)
                    || !message.Read(ref fastSessionKey)
                    || !message.Read(out uint p2pFirstFrameNumber)
                    || !message.Read(out Guid peerUdpMagicNumber)
                    || !message.Read(out bool enableDirectP2P)
                    || !message.Read(out int bindPort))
                    return;

                ProcessP2PGroupMemberJoin(client, groupHostId, memberId, eventId, p2pFirstFrameNumber,
                    peerUdpMagicNumber,
                    enableDirectP2P, bindPort, "P2PGroup_MemberJoin", sessionKey, fastSessionKey);
            }
            else
            {
                if (!message.Read(out uint eventId)
                    || !message.Read(out uint p2pFirstFrameNumber)
                    || !message.Read(out Guid peerUdpMagicNumber)
                    || !message.Read(out bool enableDirectP2P)
                    || !message.Read(out int bindPort))
                    return;

                ProcessP2PGroupMemberJoin(client, groupHostId, memberId, eventId, p2pFirstFrameNumber,
                    peerUdpMagicNumber,
                    enableDirectP2P, bindPort, "P2PGroup_MemberJoin_Unencrypted");
            }
        }

        private static void ProcessP2PGroupMemberJoin(NetClient client, uint groupHostId, uint memberId, uint eventId,
            uint p2pFirstFrameNumber, Guid peerUdpMagicNumber, bool enableDirectP2P, int bindPort, string logName,
            ByteArray sessionKey = null, ByteArray fastSessionKey = null)
        {
            client.Logger.Information(
                "{LogName} => memberId = {MemberId}, groupHostId = {GroupHostId}, eventId = {EventId}, enableDirectP2P = {EnableDirectP2P}, bindPort = {BindPort}",
                logName, memberId, groupHostId, eventId, enableDirectP2P, bindPort);

            if (memberId == client.HostId)
            {
                client.P2PGroup.HostId = groupHostId;
                client.PeerUdpMagicNumber = peerUdpMagicNumber;
                client.P2PFirstFrameNumber = p2pFirstFrameNumber;

                client.StartReliableUdpLoop();
            }
            else
            {
                var newMember = new P2PMember(client, client.P2PGroup.HostId, memberId)
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

                newMember.UdpDefragBoard.MaxMessageLength =
                    client.NetSettings?.MessageMaxLength ?? NetConfig.MessageMaxLength;

                if (!enableDirectP2P)
                {
                    client.Logger.Debug(
                        "ProcessP2PGroupMemberJoin => skipping P2P UDP socket creation for hostId = {HostId}, enableDirectP2P = false",
                        memberId);
                    client.P2PGroup.P2PMembers.TryAdd(memberId, newMember);

                    var joinAckNoP2P = new NetMessage();
                    joinAckNoP2P.Write(groupHostId);
                    joinAckNoP2P.Write(memberId);
                    joinAckNoP2P.Write(eventId);
                    joinAckNoP2P.Write(false);
                    client.RmiToServer((ushort)NexumOpCode.P2PGroup_MemberJoin_Ack, joinAckNoP2P);
                }
                else
                {
                    int? targetPort = bindPort > 0 ? bindPort : null;
                    if (targetPort.HasValue)
                        client.AimForPort = (ushort)targetPort.Value;

                    client.P2PGroup.P2PMembers.TryAdd(memberId, newMember);

                    Task.Run(() =>
                    {
                        bool localPortReuseSuccess = false;
                        try
                        {
                            lock (newMember.P2PMutex)
                            {
                                if (newMember.IsClosed || newMember.PeerUdpChannel != null)
                                {
                                    var joinAckEarly = new NetMessage();
                                    joinAckEarly.Write(groupHostId);
                                    joinAckEarly.Write(memberId);
                                    joinAckEarly.Write(eventId);
                                    joinAckEarly.Write(false);
                                    client.RmiToServer((ushort)NexumOpCode.P2PGroup_MemberJoin_Ack, joinAckEarly);
                                    return;
                                }

                                (var channel, var workerGroup, int port, _) = client.ConnectUdp(targetPort);
                                newMember.PeerUdpChannel = channel;
                                newMember.PeerUdpEventLoopGroup = workerGroup;
                                newMember.SelfUdpLocalSocket = new IPEndPoint(client.LocalIP, port);

                                if (bindPort != 0 && port == bindPort)
                                {
                                    localPortReuseSuccess = true;
                                    newMember.LocalPortReuseSuccess = true;
                                }

                                client.Logger.Debug(
                                    "ProcessP2PGroupMemberJoin => created P2P UDP socket for hostId = {HostId}, port = {Port}, localPortReuseSuccess = {LocalPortReuseSuccess}",
                                    memberId, port, localPortReuseSuccess);
                            }
                        }
                        catch (Exception ex)
                        {
                            client.Logger.Error(ex,
                                "ProcessP2PGroupMemberJoin => failed to create P2P UDP socket for hostId = {HostId}",
                                memberId);
                        }

                        var joinAck = new NetMessage();
                        joinAck.Write(groupHostId);
                        joinAck.Write(memberId);
                        joinAck.Write(eventId);
                        joinAck.Write(localPortReuseSuccess);
                        client.RmiToServer((ushort)NexumOpCode.P2PGroup_MemberJoin_Ack, joinAck);
                    });
                }
            }

            if (memberId == client.HostId)
            {
                var joinAck = new NetMessage();
                joinAck.Write(groupHostId);
                joinAck.Write(memberId);
                joinAck.Write(eventId);
                joinAck.Write(false);
                client.RmiToServer((ushort)NexumOpCode.P2PGroup_MemberJoin_Ack, joinAck);
            }
        }

        private static void RMIHandler(NetClient client, NetMessage message, ushort filterTag, IPEndPoint udpEndPoint)
        {
            ushort rmiId = 0;
            if (!message.Read(ref rmiId))
                return;

            switch ((NexumOpCode)rmiId)
            {
                case NexumOpCode.P2PGroup_MemberJoin:
                case NexumOpCode.P2PGroup_MemberJoin_Unencrypted:
                {
                    HandleP2PGroupMemberJoin(client, message, (NexumOpCode)rmiId);
                    break;
                }

                case NexumOpCode.P2PGroup_MemberLeave:
                {
                    if (!message.Read(out uint memberId) || !message.Read(out uint groupHostId))
                        return;

                    client.Logger.Information(
                        "P2PGroup_MemberLeave => memberId = {MemberId}, groupHostId = {GroupHostId}",
                        memberId, groupHostId);

                    if (groupHostId == client.P2PGroup.HostId)
                    {
                        client.P2PGroup.P2PMembers.TryRemove(memberId, out var p2pMember);
                        p2pMember?.Close();
                    }

                    break;
                }

                case NexumOpCode.P2P_NotifyDirectP2PDisconnected2:
                {
                    if (!message.Read(out uint memberId) || !message.Read(out uint reason))
                        return;

                    client.Logger.Debug(
                        "P2P_NotifyDirectP2PDisconnected2 => hostId = {HostId}, reason = {Reason}",
                        memberId,
                        (ErrorType)reason
                    );

                    if (client.P2PGroup.P2PMembers.TryGetValue(memberId, out var p2pMember))
                        p2pMember.HandleRemoteDisconnect();
                    else
                        client.Logger.Warning(
                            "P2P_NotifyDirectP2PDisconnected2 => P2P member not found for hostId = {HostId}",
                            memberId);

                    break;
                }

                case NexumOpCode.S2C_RequestCreateUdpSocket:
                {
                    var udpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
                    if (!message.ReadStringEndPoint(ref udpSocket))
                        return;

                    client.Logger.Debug("S2C_RequestCreateUdpSocket => {UdpSocket}", udpSocket);

                    client.ServerUdpSocket = udpSocket;

                    if (client.UdpChannel != null)
                    {
                        client.Logger.Debug("S2C_RequestCreateUdpSocket => UDP socket already exists, sending ack");
                        var createUdpSocketAck = new NetMessage();
                        createUdpSocketAck.Write(true);
                        client.RmiToServer((ushort)NexumOpCode.C2S_CreateUdpSocketAck, createUdpSocketAck);
                        return;
                    }

                    Task.Run(() =>
                    {
                        try
                        {
                            if (client.UdpChannel != null)
                            {
                                var existingSocketAck = new NetMessage();
                                existingSocketAck.Write(true);
                                client.RmiToServer((ushort)NexumOpCode.C2S_CreateUdpSocketAck, existingSocketAck);
                                return;
                            }

                            var (channel, workerGroup, _, _) = client.ConnectUdp();
                            client.UdpChannel = channel;
                            client.UdpEventLoopGroup = workerGroup;
                            client.ServerUdpReadyWaiting = false;

                            var newSocketAck = new NetMessage();
                            newSocketAck.Write(true);

                            client.RmiToServer((ushort)NexumOpCode.C2S_CreateUdpSocketAck, newSocketAck);
                        }
                        catch (Exception ex)
                        {
                            client.Logger.Error(ex, "S2C_RequestCreateUdpSocket => failed to create UDP socket");
                            client.ServerUdpSocketFailed = true;
                            client.ServerUdpReadyWaiting = false;
                        }
                    });

                    break;
                }

                case NexumOpCode.S2C_CreateUdpSocketAck:
                {
                    if (!message.Read(out bool result) || !result)
                    {
                        client.ServerUdpReadyWaiting = false;
                        return;
                    }

                    var udpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
                    if (!message.ReadStringEndPoint(ref udpSocket))
                    {
                        client.ServerUdpReadyWaiting = false;
                        return;
                    }

                    client.Logger.Information("S2C_CreateUdpSocketAck => {UdpSocket}", udpSocket);

                    client.ServerUdpSocket = udpSocket;

                    if (client.UdpChannel == null && !client.ServerUdpSocketFailed)
                        Task.Run(() =>
                        {
                            try
                            {
                                if (client.UdpChannel != null)
                                {
                                    client.ServerUdpReadyWaiting = false;
                                    return;
                                }

                                var (channel, workerGroup, _, _) = client.ConnectUdp();
                                client.UdpChannel = channel;
                                client.UdpEventLoopGroup = workerGroup;
                                client.ServerUdpReadyWaiting = false;

                                client.Logger.Debug("S2C_CreateUdpSocketAck => UDP socket created");
                            }
                            catch (Exception ex)
                            {
                                client.Logger.Error(ex, "S2C_CreateUdpSocketAck => failed to create UDP socket");
                                client.ServerUdpSocketFailed = true;
                                client.ServerUdpReadyWaiting = false;
                            }
                        });
                    else
                        client.ServerUdpReadyWaiting = false;

                    break;
                }

                case NexumOpCode.P2PRecycleComplete:
                {
                    if (!message.Read(out uint hostId))
                        return;

                    client.Logger.Debug("P2PRecycleComplete => hostId = {HostId}", hostId);

                    var notifyJitDirectP2PTriggered = new NetMessage();
                    notifyJitDirectP2PTriggered.Write(hostId);

                    client.RmiToServer((ushort)NexumOpCode.NotifyJitDirectP2PTriggered, notifyJitDirectP2PTriggered);
                    break;
                }

                case NexumOpCode.NewDirectP2PConnection:
                {
                    if (!message.Read(out uint hostId))
                        return;

                    Guid magicNumber;
                    lock (client.UdpHolepunchLock)
                    {
                        if (!client.UdpEnabled)
                        {
                            if (client.PendingP2PConnections.TryAdd(hostId, 0))
                                client.Logger.Debug(
                                    "NewDirectP2PConnection => hostId = {HostId}, queueing - UDP not enabled yet",
                                    hostId);

                            return;
                        }

                        magicNumber = client.UdpMagicNumber;
                    }

                    if (!client.P2PGroup.P2PMembers.TryGetValue(hostId, out var targetMember))
                    {
                        client.Logger.Warning("NewDirectP2PConnection => P2P member not found for hostId = {HostId}",
                            hostId);
                        return;
                    }

                    client.Logger.Debug("NewDirectP2PConnection => hostId = {HostId}", hostId);

                    lock (targetMember.P2PMutex)
                    {
                        if (targetMember.P2PHolepunchInitiated)
                        {
                            client.Logger.Debug(
                                "NewDirectP2PConnection => hostId = {HostId}, skipping - holepunch already in progress",
                                hostId);
                            return;
                        }

                        targetMember.P2PHolepunchInitiated = true;
                    }

                    var peerUdpServerHolepunch = new NetMessage();
                    peerUdpServerHolepunch.WriteEnum(MessageType.PeerUdp_ServerHolepunch);
                    peerUdpServerHolepunch.Write(magicNumber);
                    peerUdpServerHolepunch.Write(hostId);

                    client.NexumToServerUdpIfAvailable(peerUdpServerHolepunch, true);
                    uint capturedHostId = hostId;
                    var capturedMember = targetMember;
                    HolepunchHelper.SendBurstMessagesWithCheck(
                        () => HolepunchHelper.CreatePeerUdpServerHolepunchMessage(magicNumber, capturedHostId),
                        burstMsg => client.NexumToServerUdpIfAvailable(burstMsg, true),
                        () => capturedMember.PeerUdpChannel == null && !capturedMember.IsClosed
                    );
                    break;
                }

                case NexumOpCode.RequestP2PHolepunch:
                {
                    if (!message.Read(out uint hostId))
                        return;

                    var localEndpoint = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
                    var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);

                    if (!message.ReadIPEndPoint(ref localEndpoint) || !message.ReadIPEndPoint(ref remoteEndpoint))
                        return;

                    client.Logger.Debug(
                        "RequestP2PHolepunch => hostId = {HostId}, localEndpoint = {LocalEndpoint}, remoteEndpoint = {RemoteEndpoint}",
                        hostId, localEndpoint, remoteEndpoint);

                    if (!client.P2PGroup.P2PMembers.TryGetValue(hostId, out var p2pMember))
                    {
                        client.Logger.Warning("RequestP2PHolepunch => P2P member not found for hostId = {HostId}",
                            hostId);
                        break;
                    }

                    Guid capturedPeerMagicNumber;

                    lock (p2pMember.P2PMutex)
                    {
                        if (p2pMember.P2PHolepunchStarted || p2pMember.DirectP2P)
                        {
                            client.Logger.Debug(
                                "RequestP2PHolepunch => skipping for hostId = {HostId}, P2PHolepunchStarted = {Started}, DirectP2P = {DirectP2P}",
                                hostId, p2pMember.P2PHolepunchStarted, p2pMember.DirectP2P);
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
                        bool sameLan = hasLocalEndpoint && hasPublicEndpoint &&
                                       NetUtil.IsSameLan(localEndpoint, remoteEndpoint);

                        for (int burst = 0; burst < HolepunchConfig.BurstCount; burst++)
                        {
                            if (capturedMember.IsClosed || capturedMember.DirectP2P)
                                return;

                            if (sameLan && hasLocalEndpoint)
                                capturedMember.NexumToPeer(
                                    HolepunchHelper.CreatePeerUdpPeerHolepunchMessage(
                                        client.HostId, capturedPeerMagicNumber, client.ServerGuid,
                                        localEndpoint),
                                    localEndpoint);

                            if (hasPublicEndpoint)
                                capturedMember.NexumToPeer(
                                    HolepunchHelper.CreatePeerUdpPeerHolepunchMessage(
                                        client.HostId, capturedPeerMagicNumber, client.ServerGuid,
                                        remoteEndpoint),
                                    remoteEndpoint);

                            if (!sameLan && hasLocalEndpoint)
                                capturedMember.NexumToPeer(
                                    HolepunchHelper.CreatePeerUdpPeerHolepunchMessage(
                                        client.HostId, capturedPeerMagicNumber, client.ServerGuid,
                                        localEndpoint),
                                    localEndpoint);

                            if (burst < HolepunchConfig.BurstCount - 1)
                                await Task.Delay(HolepunchConfig.BurstDelayMs);
                        }
                    });

                    break;
                }

                case NexumOpCode.NotifyDirectP2PEstablish:
                {
                    var endpointA = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
                    var endpointB = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
                    var endpointC = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
                    var endpointD = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);

                    if (!message.Read(out uint hostIdA)
                        || !message.Read(out uint hostIdB)
                        || !message.ReadIPEndPoint(ref endpointA)
                        || !message.ReadIPEndPoint(ref endpointB)
                        || !message.ReadIPEndPoint(ref endpointC)
                        || !message.ReadIPEndPoint(ref endpointD))
                        return;

                    if (client.HostId == hostIdB)
                    {
                        SysUtil.Swap(ref hostIdA, ref hostIdB);
                        SysUtil.Swap(ref endpointA, ref endpointC);
                        SysUtil.Swap(ref endpointD, ref endpointB);
                    }

                    if (client.P2PGroup.P2PMembers.TryGetValue(hostIdB, out var p2pMember))
                    {
                        lock (p2pMember.P2PMutex)
                        {
                            if (p2pMember.DirectP2P)
                            {
                                client.Logger.Debug(
                                    "NotifyDirectP2PEstablish => already established with hostId = {HostId}", hostIdB);
                                return;
                            }

                            p2pMember.DirectP2P = true;
                            p2pMember.LastUdpReceivedTime = client.GetAbsoluteTime();
                            p2pMember.PeerLocalToRemoteSocket = endpointA;
                            p2pMember.PeerRemoteToLocalSocket = endpointD;
                            p2pMember.SelfLocalToRemoteSocket = endpointC;
                            p2pMember.SelfRemoteToLocalSocket = endpointB;
                        }

                        client.Logger.Information(
                            "Direct P2P established => hostId = {HostIdB}, peerEndpoint = {EndpointA}, selfEndpoint = {EndpointC}",
                            hostIdB,
                            endpointA,
                            endpointC
                        );
                    }

                    break;
                }

                case NexumOpCode.NotifyUdpToTcpFallbackByServer:
                {
                    client.CloseUdp();
                    client.RmiToServer((ushort)NexumOpCode.NotifyUdpToTcpFallbackByClient, new NetMessage());
                    break;
                }

                case NexumOpCode.RenewP2PConnectionState:
                {
                    if (!message.Read(out uint hostId))
                        return;

                    if (client.P2PGroup.P2PMembers.TryGetValue(hostId, out var p2pMember))
                    {
                        client.Logger.Debug("RenewP2PConnectionState => hostId = {HostId}", hostId);

                        lock (p2pMember.P2PMutex)
                        {
                            p2pMember.DirectP2P = false;
                            p2pMember.Close();
                            p2pMember.IsClosed = false;
                        }

                        Task.Run(() =>
                        {
                            lock (p2pMember.P2PMutex)
                            {
                                if (p2pMember.PeerUdpChannel == null)
                                {
                                    (var channel, var workerGroup, int port, _) = client.ConnectUdp();
                                    p2pMember.PeerUdpChannel = channel;
                                    p2pMember.PeerUdpEventLoopGroup = workerGroup;
                                    client.Logger.Debug(
                                        "RenewP2PConnectionState => pre-created P2P UDP socket on port {Port}", port);
                                }
                            }

                            var notifyJitDirectP2PTriggered = new NetMessage();
                            notifyJitDirectP2PTriggered.Write(hostId);
                            client.RmiToServer((ushort)NexumOpCode.NotifyJitDirectP2PTriggered,
                                notifyJitDirectP2PTriggered);
                        });
                    }

                    break;
                }

                case NexumOpCode.ShutdownTcpAck:
                {
                    client.Logger.Debug("ShutdownTcpAck");
                    client.RmiToServer((ushort)NexumOpCode.ShutdownTcpHandshake, new NetMessage());
                    break;
                }

                case NexumOpCode.ReliablePong:
                {
                    break;
                }

                case NexumOpCode.ReportServerTimeAndFrameRateAndPing:
                {
                    if (!message.Read(out double clientLocalTime) || !message.Read(out double peerFrameRate))
                        break;

                    var p2pMember =
                        client.P2PGroup?.FindMember(client.HostId, udpEndPoint, filterTag, message.RelayFrom);
                    if (p2pMember != null)
                    {
                        p2pMember.PeerFrameRateInternal = peerFrameRate;

                        var reportServerTimeAndFrameRatePong = new NetMessage();
                        reportServerTimeAndFrameRatePong.Write(clientLocalTime);
                        reportServerTimeAndFrameRatePong.Write(client.GetAbsoluteTime());
                        reportServerTimeAndFrameRatePong.Write(client.ServerUdpRecentPing);
                        reportServerTimeAndFrameRatePong.Write(client.RecentFrameRate);

                        p2pMember.RmiToPeer((ushort)NexumOpCode.ReportServerTimeAndFrameRateAndPong,
                            reportServerTimeAndFrameRatePong,
                            reliable: true);
                    }

                    break;
                }

                case NexumOpCode.ReportServerTimeAndFrameRateAndPong:
                {
                    if (!message.Read(out double _) ||
                        !message.Read(out double peerLocalTime) ||
                        !message.Read(out double peerServerPing) ||
                        !message.Read(out double peerFrameRate))
                        break;

                    var p2pMember =
                        client.P2PGroup?.FindMember(client.HostId, udpEndPoint, filterTag, message.RelayFrom);
                    if (p2pMember != null)
                    {
                        double currentTime = client.GetAbsoluteTime();
                        double peerToServerPing = Math.Max(peerServerPing, 0.0);

                        if (p2pMember.LastPeerServerPing > 0)
                            p2pMember.PeerServerJitterInternal = Core.NetUtil.CalculateJitter(
                                p2pMember.PeerServerJitterInternal, peerToServerPing, p2pMember.LastPeerServerPing);

                        p2pMember.LastPeerServerPing = peerToServerPing;
                        p2pMember.PeerServerPingInternal = peerToServerPing;
                        p2pMember.PeerFrameRateInternal = peerFrameRate;

                        double estimatedPeerTime = peerLocalTime + p2pMember.RecentPing;
                        p2pMember.IndirectServerTimeDiff = currentTime - estimatedPeerTime;
                    }

                    break;
                }

                default:
                    client.OnRMIReceive(message, rmiId);
                    break;
            }
        }

        private static void NotifyServerConnectionHintHandler(NetClient client, NetMessage message)
        {
            client.Logger.Debug("NotifyServerConnectionHint");

            var settings = new NetSettings();
            var rsaKeyBuffer = new ByteArray();

            if (message.Read(ref settings.EnableServerLog)
                && message.Read(out byte fallbackMethod)
                && message.Read(ref settings.MessageMaxLength)
                && message.Read(ref settings.IdleTimeout)
                && message.Read(out byte directP2PStartCondition)
                && message.Read(ref settings.OverSendSuspectingThresholdInBytes)
                && message.Read(ref settings.EnableNagleAlgorithm)
                && message.Read(ref settings.EncryptedMessageKeyLength)
                && message.Read(ref settings.FastEncryptedMessageKeyLength)
                && message.Read(ref settings.AllowServerAsP2PGroupMember)
                && message.Read(ref settings.EnableP2PEncryptedMessaging)
                && message.Read(ref settings.UpnpDetectNatDevice)
                && message.Read(ref settings.UpnpTcpAddrPortMapping)
                && message.Read(ref settings.EnableLookaheadP2PSend)
                && message.Read(ref settings.EnablePingTest)
                && message.Read(ref settings.EmergencyLogLineCount))
            {
                settings.FallbackMethod = (FallbackMethod)fallbackMethod;
                settings.DirectP2PStartCondition = (DirectP2PStartCondition)directP2PStartCondition;
                client.NetSettings = settings;
                client.UdpDefragBoard.MaxMessageLength = settings.MessageMaxLength;

                client.Crypt = new NetCrypt(settings.EncryptedMessageKeyLength, settings.FastEncryptedMessageKeyLength);

                if (!message.Read(ref rsaKeyBuffer))
                    return;

                byte[] encodedRsaKey = rsaKeyBuffer.GetBuffer();

                if (client.PinnedServerPublicKey != null)
                {
                    if (!client.ValidateServerPublicKey(encodedRsaKey))
                    {
                        client.Logger.Error(
                            "Certificate pinning validation failed - server public key does not match pinned key. Possible MITM attack!");
                        client.Channel?.CloseAsync();
                        return;
                    }

                    client.Logger.Debug("Certificate pinning validation successful");
                }

                var rsaSequence = (DerSequence)Asn1Object.FromByteArray(encodedRsaKey);

                var rsaParameters = new RSAParameters
                {
                    Exponent = ((DerInteger)rsaSequence[1]).Value.ToByteArrayUnsigned(),
                    Modulus = ((DerInteger)rsaSequence[0]).Value.ToByteArrayUnsigned()
                };

                client.RSA = new RSACryptoServiceProvider(2048);
                client.RSA.ImportParameters(rsaParameters);

                var notifyCSEncryptedSessionKey = new NetMessage();
                notifyCSEncryptedSessionKey.WriteEnum(MessageType.NotifyCSEncryptedSessionKey);
                notifyCSEncryptedSessionKey.Write(new ByteArray(client.RSA.Encrypt(client.Crypt.GetKey(), true), true));
                notifyCSEncryptedSessionKey.Write(new ByteArray(client.Crypt.EncryptKey(client.Crypt.GetFastKey()),
                    true));

                client.NexumToServer(notifyCSEncryptedSessionKey);
            }
        }

        private static void NotifyCSSessionKeySuccessHandler(NetClient client)
        {
            client.Logger.Debug("NotifyCSSessionKeySuccess");

            var notifyServerConnectionRequestData = new NetMessage();
            notifyServerConnectionRequestData.WriteEnum(MessageType.NotifyServerConnectionRequestData);
            notifyServerConnectionRequestData.Write(new ByteArray());

            switch (client.ServerType)
            {
                case ServerType.Auth:
                    notifyServerConnectionRequestData.Write(new Guid("{9be73c0b-3b10-403e-be7d-9f222702a38c}"));
                    break;

                case ServerType.Game:
                    notifyServerConnectionRequestData.Write(new Guid("{beb92241-8333-4117-ab92-9b4af78c688f}"));
                    break;

                case ServerType.Chat:
                    notifyServerConnectionRequestData.Write(new Guid("{97d36acf-8cc0-4dfb-bcc9-97cab255e2bc}"));
                    break;

                case ServerType.Relay:
                    notifyServerConnectionRequestData.Write(new Guid("{a43a97d1-9ec7-495e-ad5f-8fe45fde1151}"));
                    break;
            }

            notifyServerConnectionRequestData.Write(Constants.NetVersion);
            client.NexumToServer(notifyServerConnectionRequestData);
        }

        private static void NotifyServerConnectSuccessHandler(NetClient client, NetMessage message)
        {
            var connectionPayload = new ByteArray();
            var serverEndPoint = new IPEndPoint(IPAddress.Any, 0);

            if (!message.Read(out uint hostId)
                || !message.Read(out Guid serverGuid)
                || !message.Read(ref connectionPayload)
                || !message.ReadIPEndPoint(ref serverEndPoint))
                return;

            client.Logger.Debug(
                "NotifyServerConnectSuccess => hostId = {HostId}, serverGuid = {ServerGuid}, endpoint = {Endpoint}",
                hostId, serverGuid, serverEndPoint);

            client.HostId = hostId;
            client.UdpDefragBoard.LocalHostId = hostId;
            client.ServerGuid = serverGuid;
            client.SetConnectionState(ConnectionState.Connected);

            if (client.ReliablePingLoop == null)
            {
                double reliablePingInterval = (client.NetSettings?.IdleTimeout ?? NetConfig.NoPingTimeoutTime) * 0.3;
                client.ReliablePingLoop = new ThreadLoop(
                    TimeSpan.FromSeconds(reliablePingInterval),
                    client.SendReliablePing);
            }

            client.ReliablePingLoop.Start();

            client.OnConnectionComplete();
        }

        private static void UnreliableRelay2Handler(NetClient client, NetMessage message)
        {
            if (!message.Read(out uint hostId))
                return;

            var relayedPacket = new ByteArray();
            if (!message.Read(ref relayedPacket))
                return;

            var relayedMessage = new NetMessage(relayedPacket) { RelayFrom = hostId };
            ReadMessage(client, relayedMessage);
        }

        private static void ReliableRelay2Handler(NetClient client, NetMessage message)
        {
            if (!message.Read(out uint hostId))
                return;

            if (!message.Read(out uint _))
                return;

            var relayedPacket = new ByteArray();
            if (!message.Read(ref relayedPacket))
                return;

            if (!ReliableUdpHelper.UnwrapPayload(relayedPacket.GetBuffer(), out byte[] unwrappedData))
                return;

            var relayedMessage = new NetMessage(unwrappedData, true) { RelayFrom = hostId };
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
                    Core.NetUtil.CalculateJitter(client.ServerUdpJitter, lastPing, client.ServerUdpLastPing);

            client.ServerUdpLastPing = lastPing;
            client.ServerUdpLastReceivedTime = currentTime;
            client.ServerUdpRecentPing = client.ServerUdpRecentPing != 0.0
                ? Core.SysUtil.Lerp(client.ServerUdpRecentPing, lastPing, ReliableUdpConfig.LagLinearProgrammingFactor)
                : lastPing;

            double estimatedServerTime = serverTime + client.ServerUdpRecentPing;
            client.ServerTimeDiff = currentTime - estimatedServerTime;

            if (message.Read(out int paddingSize) && paddingSize > 0)
                client.ServerMtuDiscovery.OnPongReceived(paddingSize, currentTime);
        }

        private static void EncryptedHandler(NetClient client, NetMessage message)
        {
            NetCoreHandler.HandleEncrypted(
                message,
                client.Crypt,
                decryptedMsg => ReadMessage(client, decryptedMsg)
            );
        }

        private static void CompressedHandler(NetClient client, NetMessage message)
        {
            NetCoreHandler.HandleCompressed(
                message,
                client.Logger,
                decompressedMsg => ReadMessage(client, decompressedMsg)
            );
        }
    }
}
