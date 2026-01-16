using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BaseLib;
using Nexum.Core;
using Org.BouncyCastle.Asn1;

namespace Nexum.Client
{
    internal class NetClientHandler
    {
        internal static void ReadFrame(NetClient client, NetMessage message, IPEndPoint udpEndPoint = null,
            bool bypass = false)
        {
            lock (client.RecvLock)
            {
                if (bypass)
                {
                    ReadMessage(client, message, udpEndPoint);
                    return;
                }

                var packet = new ByteArray();
                if (!message.Read(ref packet))
                    return;

                var innerMessage = new NetMessage(packet) { RelayFrom = message.RelayFrom };
                ReadMessage(client, innerMessage, udpEndPoint);
            }
        }

        internal static void ReadMessage(NetClient client, NetMessage message, IPEndPoint udpEndPoint = null)
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
                    RMIHandler(client, message, udpEndPoint);
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
                    P2PRequestIndirectServerTimeAndPingHandler(client, message, udpEndPoint);
                    break;

                case MessageType.P2PReplyIndirectServerTimeAndPong:
                    P2PReplyIndirectServerTimeAndPongHandler(client, message, udpEndPoint);
                    break;

                case MessageType.ReliableUdp_Frame:
                    ReliableUdpFrameHandler(client, message, udpEndPoint);
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

        private static void ReliableUdpFrameHandler(NetClient client, NetMessage message, IPEndPoint udpEndPoint)
        {
            if (!ReliableUdpHelper.ParseFrame(message, out var frame))
            {
                client.Logger.Warning("Failed to parse ReliableUdp_Frame");
                return;
            }

            if (client.ServerUdpSocket != null && client.ServerUdpSocket.Equals(udpEndPoint))
            {
                if (client.ToServerReliableUdp != null)
                {
                    client.ToServerReliableUdp.TakeReceivedFrame(frame);
                    ExtractMessagesFromServerReliableUdpStream(client, udpEndPoint);
                }
                else
                {
                    if (frame.Type == ReliableUdpFrameType.Data && frame.Data != null)
                        if (ReliableUdpHelper.UnwrapPayload(frame.Data, out byte[] payload))
                        {
                            var innerMessage = new NetMessage(new ByteArray(payload));
                            ReadMessage(client, innerMessage, udpEndPoint);
                        }
                }

                return;
            }

            P2PMember sourceMember = null;
            if (client.P2PGroup?.P2PMembers != null)
                foreach (var member in client.P2PGroup.P2PMembers.Values)
                    if (member.PeerLocalToRemoteSocket != null &&
                        member.PeerLocalToRemoteSocket.Equals(udpEndPoint))
                    {
                        sourceMember = member;
                        break;
                    }

            if (sourceMember == null)
            {
                client.Logger.Verbose(
                    "ReliableUdp_Frame from unknown endpoint {Endpoint}, ignoring",
                    udpEndPoint);
                return;
            }

            sourceMember.ProcessReceivedReliableUdpFrame(frame);

            ExtractMessagesFromP2PReliableUdpStream(client, sourceMember, udpEndPoint);
        }

        private static void ExtractMessagesFromServerReliableUdpStream(NetClient client, IPEndPoint udpEndPoint)
        {
            var stream = client.ToServerReliableUdp?.ReceivedStream;
            if (stream == null || stream.Length == 0)
                return;

            while (stream.Length > 0)
            {
                byte[] streamData = stream.PeekAll();
                var tempMsg = new NetMessage(new ByteArray(streamData));

                if (!tempMsg.Read(out ushort magic) || magic != Constants.TcpSplitter)
                    break;

                var payload = new ByteArray();
                if (!tempMsg.Read(ref payload))
                    break;

                int consumedBytes = tempMsg.ReadOffset;
                stream.PopFront(consumedBytes);

                var innerMessage = new NetMessage(payload);
                ReadMessage(client, innerMessage, udpEndPoint);
            }
        }

        private static void ExtractMessagesFromP2PReliableUdpStream(NetClient client, P2PMember member,
            IPEndPoint udpEndPoint)
        {
            var stream = member.ToPeerReliableUdp?.ReceivedStream;
            if (stream == null || stream.Length == 0)
                return;

            while (stream.Length > 0)
            {
                byte[] streamData = stream.PeekAll();
                var tempMsg = new NetMessage(new ByteArray(streamData));

                if (!tempMsg.Read(out ushort magic) || magic != Constants.TcpSplitter)
                    break;

                var payload = new ByteArray();
                if (!tempMsg.Read(ref payload))
                    break;

                int consumedBytes = tempMsg.ReadOffset;
                stream.PopFront(consumedBytes);

                var innerMessage = new NetMessage(payload);
                ReadMessage(client, innerMessage, udpEndPoint);
            }
        }

        private static void RequestStartServerHolepunchHandler(NetClient client, NetMessage message)
        {
            if (!message.Read(out Guid magicNumber))
                return;

            client.UdpMagicNumber = magicNumber;
            client.Logger.Debug("RequestStartServerHolepunch => guid = {MagicNumber}", magicNumber);

            client.NexumToServerUdpIfAvailable(HolepunchHelper.CreateServerHolepunchMessage(magicNumber), true);
            HolepunchHelper.SendBurstMessages(
                () => HolepunchHelper.CreateServerHolepunchMessage(magicNumber),
                msg => client.NexumToServerUdpIfAvailable(msg, true),
                HolepunchConstants.UdpMatchedDelayMs
            );
        }

        private static void ServerHolepunchAckHandler(NetClient client, NetMessage message)
        {
            var selfUdpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);

            if (!message.Read(out Guid magicNumber) || !message.ReadIPEndPoint(ref selfUdpSocket))
                return;

            if (!magicNumber.Equals(client.UdpMagicNumber))
                return;
            var udpChannel = client.UdpChannel;
            if (udpChannel == null)
                return;

            lock (client.UdpHolepunchLock)
            {
                if (client.SelfUdpSocket != null)
                    return;

                client.Logger.Debug("ServerHolepunchAck => guid = {MagicNumber}, endpoint = {Endpoint}", magicNumber,
                    selfUdpSocket);
                client.SelfUdpSocket = selfUdpSocket;
            }

            var notifyHolepunchSuccess = new NetMessage();
            notifyHolepunchSuccess.WriteEnum(MessageType.NotifyHolepunchSuccess);
            notifyHolepunchSuccess.Write(client.UdpMagicNumber);
            notifyHolepunchSuccess.Write(new IPEndPoint(client.LocalIP,
                ((IPEndPoint)udpChannel.LocalAddress).Port));
            notifyHolepunchSuccess.Write(client.SelfUdpSocket);

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

                client.InitializeToServerReliableUdp(client.P2PFirstFrameNumber);

                client.StartReliableUdpLoop();
            }

            client.Logger.Information("UDP connection established with server, guid = {MagicNumber}", magicNumber);
            ProcessPendingP2PConnections(client);
        }

        private static void ProcessPendingP2PConnections(NetClient client)
        {
            foreach (uint hostId in client.PendingP2PConnections.Keys.ToArray())
            {
                if (!client.PendingP2PConnections.TryRemove(hostId, out _))
                    continue;
                if (!client.P2PGroup.P2PMembers.TryGetValue(hostId, out var p2pMember))
                    continue;

                lock (p2pMember.P2PMutex)
                {
                    if (p2pMember.P2PHolepunchInitiated || p2pMember.PeerUdpChannel != null)
                        continue;

                    p2pMember.P2PHolepunchInitiated = true;
                }

                client.Logger.Debug("Processing pending P2P connection => hostId = {HostId}", hostId);

                var magicNumber = client.UdpMagicNumber;
                client.NexumToServerUdpIfAvailable(
                    HolepunchHelper.CreatePeerUdpServerHolepunchMessage(magicNumber, hostId), true);
                HolepunchHelper.SendBurstMessages(
                    () => HolepunchHelper.CreatePeerUdpServerHolepunchMessage(magicNumber, hostId),
                    msg => client.NexumToServerUdpIfAvailable(msg, true)
                );
            }
        }

        private static void PeerUdpServerHolepunchAckHandler(NetClient client, NetMessage message)
        {
            var selfUdpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);

            if (!message.Read(out Guid magicNumber) || !message.ReadIPEndPoint(ref selfUdpSocket) ||
                !message.Read(out uint hostId))
                return;

            if (!magicNumber.Equals(client.UdpMagicNumber))
                return;

            if (client.P2PGroup.P2PMembers.TryGetValue(hostId, out var p2pMember))
                Task.Run(() =>
                {
                    lock (p2pMember.P2PMutex)
                    {
                        if (p2pMember.IsClosed)
                            return;
                        if (p2pMember.PeerUdpChannel != null)
                            return;

                        client.Logger.Debug(
                            "PeerUdpServerHolepunchAck => guid = {MagicNumber}, endpoint = {Endpoint}, hostId = {HostId}",
                            magicNumber,
                            selfUdpSocket,
                            hostId
                        );

                        var tuple = client.ConnectUdp();

                        p2pMember.PeerUdpChannel = tuple.Item1;
                        p2pMember.PeerUdpEventLoopGroup = tuple.Item2;

                        var peerUdpNotifyHolepunchSuccess = new NetMessage();
                        peerUdpNotifyHolepunchSuccess.WriteEnum(MessageType.PeerUdp_NotifyHolepunchSuccess);

                        p2pMember.SelfUdpLocalSocket = new IPEndPoint(client.LocalIP, tuple.Item3);
                        peerUdpNotifyHolepunchSuccess.Write(p2pMember.SelfUdpLocalSocket);

                        p2pMember.SelfUdpSocket = new IPEndPoint(client.SelfUdpSocket.Address, tuple.Item3);
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
                return;

            if (!client.P2PGroup.P2PMembers.TryGetValue(hostId, out var p2pMember))
                return;

            lock (p2pMember.P2PMutex)
            {
                if (!magicNumber.Equals(client.PeerUdpMagicNumber))
                    return;

                if (p2pMember.IsClosed || p2pMember.DirectP2P)
                    return;
            }

            client.Logger.Debug(
                "PeerUdpPeerHolepunch => hostId = {HostId}, guid = {MagicNumber}, serverGuid = {ServerGuid}, endpoint = {Endpoint}",
                hostId,
                magicNumber,
                serverGuid,
                selfUdpSocket
            );

            Task.Run(async () =>
            {
                bool socketsReady = await HolepunchHelper.WaitForConditionWithBackoffAsync(
                    () => p2pMember.PeerUdpSocket != null
                          && p2pMember.PeerUdpLocalSocket != null
                          && p2pMember.SelfUdpSocket != null
                          && p2pMember.SelfUdpLocalSocket != null,
                    () => p2pMember.IsClosed || p2pMember.DirectP2P
                );

                if (!socketsReady)
                    return;

                bool sameLan = NetUtil.IsUnicastEndpoint(p2pMember.PeerUdpLocalSocket)
                               && NetUtil.IsUnicastEndpoint(p2pMember.SelfUdpLocalSocket)
                               && NetUtil.IsSameLan(p2pMember.SelfUdpSocket, p2pMember.PeerUdpSocket)
                               && NetUtil.IsSameLan(p2pMember.PeerUdpLocalSocket, p2pMember.SelfUdpLocalSocket);

                for (int burst = 0; burst < HolepunchConstants.BurstCount; burst++)
                {
                    if (p2pMember.IsClosed || p2pMember.DirectP2P)
                        return;

                    if (sameLan)
                        p2pMember.NexumToPeer(
                            HolepunchHelper.CreatePeerUdpPeerHolepunchAckMessage(
                                magicNumber, client.HostId, selfUdpSocket, udpEndPoint, p2pMember.PeerUdpLocalSocket),
                            p2pMember.PeerUdpLocalSocket);

                    if (NetUtil.IsUnicastEndpoint(p2pMember.PeerUdpSocket))
                        p2pMember.NexumToPeer(
                            HolepunchHelper.CreatePeerUdpPeerHolepunchAckMessage(
                                magicNumber, client.HostId, selfUdpSocket, udpEndPoint, p2pMember.PeerUdpSocket),
                            p2pMember.PeerUdpSocket);

                    p2pMember.NexumToPeer(
                        HolepunchHelper.CreatePeerUdpPeerHolepunchAckMessage(
                            magicNumber, client.HostId, selfUdpSocket, udpEndPoint, udpEndPoint),
                        udpEndPoint);

                    if (burst < HolepunchConstants.BurstCount - 1)
                        await Task.Delay(HolepunchConstants.BurstDelayMs);
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

            if (client.P2PGroup.P2PMembers.TryGetValue(hostId, out var p2pMember))
            {
                if (!magicNumber.Equals(p2pMember.PeerUdpMagicNumber))
                    return;

                lock (p2pMember.P2PMutex)
                {
                    if (p2pMember.P2PHolepunchNotified)
                        return;

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
        }

        private static void P2PRequestIndirectServerTimeAndPingHandler(NetClient client, NetMessage message,
            IPEndPoint udpEndPoint)
        {
            if (!message.Read(out double time))
                return;

            P2PMember p2pMember = null;
            if (client.P2PGroup?.P2PMembers != null)
                foreach (var member in client.P2PGroup.P2PMembers.Values)
                    if (member.PeerRemoteToLocalSocket?.Equals(udpEndPoint) ?? false)
                    {
                        p2pMember = member;
                        break;
                    }

            if (p2pMember != null)
            {
                var p2pReplyIndirectServerTimeAndPong = new NetMessage();
                p2pReplyIndirectServerTimeAndPong.WriteEnum(MessageType.P2PReplyIndirectServerTimeAndPong);
                p2pReplyIndirectServerTimeAndPong.Write(time);
                p2pMember.NexumToPeer(p2pReplyIndirectServerTimeAndPong);
            }
            else
            {
                client.Logger.Warning(
                    "Recieved P2PRequestIndirectServerTimeAndPing from unknown IP => {UdpEndpoint}",
                    udpEndPoint
                );
            }
        }

        private static void P2PReplyIndirectServerTimeAndPongHandler(NetClient client, NetMessage message,
            IPEndPoint udpEndPoint)
        {
            if (!message.Read(out double sentTime))
                return;

            P2PMember p2pMember = null;
            if (client.P2PGroup?.P2PMembers != null)
                foreach (var member in client.P2PGroup.P2PMembers.Values)
                    if (member.PeerRemoteToLocalSocket?.Equals(udpEndPoint) ?? false)
                    {
                        p2pMember = member;
                        break;
                    }

            if (p2pMember != null)
            {
                double currentTime = client.GetAbsoluteTime();
                double lastPing = (currentTime - sentTime) / 2.0;

                if (lastPing < 0)
                    lastPing = 0;

                p2pMember.LastPing = lastPing;
                p2pMember.RecentPing = p2pMember.RecentPing != 0.0
                    ? Core.SysUtil.Lerp(p2pMember.RecentPing, lastPing, ReliableUdpConfig.LagLinearProgrammingFactor)
                    : lastPing;
            }
            else
            {
                client.Logger.Warning(
                    "Recieved P2PReplyIndirectServerTimeAndPong from unknown IP => {UdpEndpoint}",
                    udpEndPoint
                );
            }
        }

        private static void HandleP2PGroupMemberJoin(NetClient client, NetMessage message, NexumOpCode opCode)
        {
            var byteArray1 = new ByteArray();

            if (!message.Read(out uint groupHostId) || !message.Read(out uint memberId) ||
                !message.Read(ref byteArray1))
                return;

            if (opCode == NexumOpCode.P2PGroup_MemberJoin)
            {
                var byteArray2 = new ByteArray();
                var byteArray3 = new ByteArray();
                if (!message.Read(out uint eventId)
                    || !message.Read(ref byteArray2)
                    || !message.Read(ref byteArray3)
                    || !message.Read(out uint p2pFirstFrameNumber)
                    || !message.Read(out Guid peerUdpMagicNumber)
                    || !message.Read(out bool _)
                    || !message.Read(out ushort _))
                    return;

                ProcessP2PGroupMemberJoin(client, groupHostId, memberId, eventId, peerUdpMagicNumber,
                    p2pFirstFrameNumber, "P2PGroup_MemberJoin");
            }
            else
            {
                if (!message.Read(out uint eventId)
                    || !message.Read(out uint p2pFirstFrameNumber)
                    || !message.Read(out Guid peerUdpMagicNumber)
                    || !message.Read(out bool _)
                    || !message.Read(out ushort _))
                    return;

                ProcessP2PGroupMemberJoin(client, groupHostId, memberId, eventId, peerUdpMagicNumber,
                    p2pFirstFrameNumber, "P2PGroup_MemberJoin_Unencrypted");
            }
        }

        private static void ProcessP2PGroupMemberJoin(NetClient client, uint groupHostId, uint memberId, uint eventId,
            Guid peerUdpMagicNumber, uint p2pFirstFrameNumber, string logName)
        {
            client.Logger.Information(
                "{LogName} => memberId = {MemberId}, groupHostId = {GroupHostId}, eventId = {EventId}",
                logName, memberId, groupHostId, eventId);

            if (memberId == client.HostId)
            {
                Task.Run(() =>
                {
                    if (client.UdpChannel != null)
                        client.CloseUdp();

                    var tuple = client.ConnectUdp();
                    client.UdpChannel = tuple.Item1;
                    client.UdpEventLoopGroup = tuple.Item2;
                });

                client.P2PGroup.HostId = groupHostId;
                client.PeerUdpMagicNumber = peerUdpMagicNumber;
                client.P2PFirstFrameNumber = p2pFirstFrameNumber;
                client.RmiToServer((ushort)NexumOpCode.C2S_RequestCreateUdpSocket, new NetMessage());

                client.StartReliableUdpLoop();
            }
            else
            {
                var newMember = new P2PMember(client, client.P2PGroup.HostId, memberId)
                {
                    PeerUdpMagicNumber = peerUdpMagicNumber
                };

                newMember.InitializeReliableUdp(p2pFirstFrameNumber);
                newMember.UdpDefragBoard.MaxMessageLength = client.NetSettings?.MessageMaxLength ?? 2097152;

                client.P2PGroup.P2PMembers.TryAdd(memberId, newMember);
            }

            var joinAck = new NetMessage();
            joinAck.Write(groupHostId);
            joinAck.Write(memberId);
            joinAck.Write(eventId);
            joinAck.Write(false);
            client.RmiToServer((ushort)NexumOpCode.P2PGroup_MemberJoin_Ack, joinAck);
        }

        private static void RMIHandler(NetClient client, NetMessage message, IPEndPoint udpEndPoint)
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
                    break;
                }

                case NexumOpCode.S2C_RequestCreateUdpSocket:
                {
                    var udpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
                    if (!message.ReadStringEndPoint(ref udpSocket))
                        return;

                    client.Logger.Debug("S2C_RequestCreateUdpSocket => {UdpSocket}", udpSocket);

                    Task.Run(() =>
                    {
                        if (client.UdpChannel != null)
                            client.CloseUdp();

                        var tuple = client.ConnectUdp();
                        client.UdpChannel = tuple.Item1;
                        client.UdpEventLoopGroup = tuple.Item2;

                        var ack = new NetMessage();
                        ack.Write(true);

                        client.RmiToServer((ushort)NexumOpCode.C2S_CreateUdpSocketAck, ack);
                    });

                    break;
                }

                case NexumOpCode.S2C_CreateUdpSocketAck:
                {
                    if (!message.Read(out bool result) || !result)
                        return;

                    var udpSocket = new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort);
                    if (!message.ReadStringEndPoint(ref udpSocket))
                        return;

                    client.Logger.Information("S2C_CreateUdpSocketAck => {UdpSocket}", udpSocket);
                    client.ServerUdpSocket = udpSocket;
                    break;
                }

                case NexumOpCode.P2PRecycleComplete:
                {
                    if (!message.Read(out uint hostId))
                        return;

                    client.Logger.Debug("P2PRecycleComplete => hostId = {HostId}", hostId);

                    var notify = new NetMessage();
                    notify.Write(hostId);

                    client.RmiToServer((ushort)NexumOpCode.NotifyJitDirectP2PTriggered, notify);
                    break;
                }

                case NexumOpCode.NewDirectP2PConnection:
                {
                    if (!message.Read(out uint hostId))
                        return;
                    if (!client.UdpEnabled)
                    {
                        if (client.PendingP2PConnections.TryAdd(hostId, 0))
                            client.Logger.Debug(
                                "NewDirectP2PConnection => hostId = {HostId}, queueing - UDP not enabled yet", hostId);

                        return;
                    }

                    if (!client.P2PGroup.P2PMembers.TryGetValue(hostId, out var targetMember))
                        return;

                    lock (targetMember.P2PMutex)
                    {
                        if (targetMember.P2PHolepunchInitiated || targetMember.PeerUdpChannel != null)
                        {
                            client.Logger.Debug(
                                "NewDirectP2PConnection => hostId = {HostId}, skipping - holepunch already in progress",
                                hostId);
                            return;
                        }

                        targetMember.P2PHolepunchInitiated = true;
                    }

                    client.Logger.Debug("NewDirectP2PConnection => hostId = {HostId}", hostId);
                    var msg = new NetMessage();
                    msg.WriteEnum(MessageType.PeerUdp_ServerHolepunch);
                    msg.Write(client.UdpMagicNumber);
                    msg.Write(hostId);

                    client.NexumToServerUdpIfAvailable(msg, true);
                    Task.Run(async () =>
                    {
                        for (int i = 0; i < HolepunchConstants.BurstCount; i++)
                        {
                            await Task.Delay(HolepunchConstants.BurstDelayMs);

                            var burstMsg = new NetMessage();
                            burstMsg.WriteEnum(MessageType.PeerUdp_ServerHolepunch);
                            burstMsg.Write(client.UdpMagicNumber);
                            burstMsg.Write(hostId);

                            client.NexumToServerUdpIfAvailable(burstMsg, true);
                        }
                    });
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

                    if (!client.P2PGroup.P2PMembers.TryGetValue(hostId, out var p2pMember))
                        break;

                    lock (p2pMember.P2PMutex)
                    {
                        if (p2pMember.P2PHolepunchStarted || p2pMember.DirectP2P)
                            break;

                        p2pMember.P2PHolepunchStarted = true;
                        p2pMember.PeerUdpSocket = remoteEndpoint;
                        p2pMember.PeerUdpLocalSocket = localEndpoint;
                    }

                    client.Logger.Debug(
                        "RequestP2PHolepunch => hostId = {HostId}, localEndpoint = {LocalEndpoint}, remoteEndpoint = {RemoteEndpoint}",
                        hostId, localEndpoint, remoteEndpoint);

                    Task.Run(async () =>
                    {
                        bool channelReady = await HolepunchHelper.WaitForConditionWithBackoffAsync(
                            () => p2pMember.PeerUdpChannel != null,
                            () => p2pMember.IsClosed || p2pMember.DirectP2P
                        );

                        if (!channelReady)
                            return;

                        bool tryLocalFirst = NetUtil.IsUnicastEndpoint(p2pMember.PeerUdpLocalSocket)
                                             && NetUtil.IsUnicastEndpoint(p2pMember.PeerUdpSocket)
                                             && NetUtil.IsSameLan(p2pMember.PeerUdpLocalSocket,
                                                 p2pMember.PeerUdpSocket);

                        if (tryLocalFirst && NetUtil.IsUnicastEndpoint(p2pMember.PeerUdpLocalSocket))
                            p2pMember.NexumToPeer(
                                HolepunchHelper.CreatePeerUdpPeerHolepunchMessage(
                                    client.HostId, p2pMember.PeerUdpMagicNumber, client.ServerGuid,
                                    p2pMember.PeerUdpLocalSocket),
                                p2pMember.PeerUdpLocalSocket);

                        if (NetUtil.IsUnicastEndpoint(p2pMember.PeerUdpSocket))
                            p2pMember.NexumToPeer(
                                HolepunchHelper.CreatePeerUdpPeerHolepunchMessage(
                                    client.HostId, p2pMember.PeerUdpMagicNumber, client.ServerGuid,
                                    p2pMember.PeerUdpSocket),
                                p2pMember.PeerUdpSocket);

                        for (int burst = 0; burst < HolepunchConstants.BurstCount; burst++)
                        {
                            await Task.Delay(HolepunchConstants.BurstDelayMs);
                            if (p2pMember.IsClosed || p2pMember.DirectP2P)
                                return;

                            if (NetUtil.IsUnicastEndpoint(p2pMember.PeerUdpSocket))
                                p2pMember.NexumToPeer(
                                    HolepunchHelper.CreatePeerUdpPeerHolepunchMessage(
                                        client.HostId, p2pMember.PeerUdpMagicNumber, client.ServerGuid,
                                        p2pMember.PeerUdpSocket),
                                    p2pMember.PeerUdpSocket);

                            if (NetUtil.IsUnicastEndpoint(p2pMember.PeerUdpLocalSocket))
                                p2pMember.NexumToPeer(
                                    HolepunchHelper.CreatePeerUdpPeerHolepunchMessage(
                                        client.HostId, p2pMember.PeerUdpMagicNumber, client.ServerGuid,
                                        p2pMember.PeerUdpLocalSocket),
                                    p2pMember.PeerUdpLocalSocket);
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
                                return;

                            p2pMember.DirectP2P = true;
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

                    client.Logger.Debug("RenewP2PConnectionState => hostId = {HostId}", hostId);

                    if (client.P2PGroup.P2PMembers.TryGetValue(hostId, out var p2pMember))
                    {
                        lock (p2pMember.P2PMutex)
                        {
                            p2pMember.DirectP2P = false;
                            p2pMember.Close();
                        }

                        var notify = new NetMessage();
                        notify.Write(hostId);

                        client.RmiToServer((ushort)NexumOpCode.NotifyJitDirectP2PTriggered, notify);
                    }

                    break;
                }

                case NexumOpCode.ShutdownTcpAck:
                {
                    client.Logger.Debug("ShutdownTcpAck");
                    client.RmiToServer((ushort)NexumOpCode.ShutdownTcpHandshake, new NetMessage());
                    break;
                }

                case NexumOpCode.ReportServerTimeAndFrameRateAndPing:
                {
                    message.Read(out double time);
                    message.Read(out double frameRate);

                    P2PMember p2pMember = null;

                    if (udpEndPoint != null)
                    {
                        if (client.P2PGroup?.P2PMembers != null)
                            foreach (var member in client.P2PGroup.P2PMembers.Values)
                                if (member.PeerRemoteToLocalSocket?.Equals(udpEndPoint) ?? false)
                                {
                                    p2pMember = member;
                                    break;
                                }
                    }
                    else
                    {
                        client.P2PGroup.P2PMembers.TryGetValue(message.RelayFrom, out p2pMember);
                    }

                    if (p2pMember != null)
                    {
                        var selfPing = new NetMessage();
                        selfPing.Write(time);
                        selfPing.Write(frameRate);
                        selfPing.Reliable = true;

                        // p2pMember.RmiToPeer((ushort)NexumOpCode.ReportServerTimeAndFrameRateAndPing, selfPing, true);
                    }

                    break;
                }

                case NexumOpCode.ReportServerTimeAndFrameRateAndPong:
                {
                    message.Read(out double clientTime);
                    message.Read(out double serverTime);
                    message.Read(out double serverPing);
                    message.Read(out double frameRate);

                    P2PMember p2pMember = null;

                    if (udpEndPoint != null)
                    {
                        if (client.P2PGroup?.P2PMembers != null)
                            foreach (var member in client.P2PGroup.P2PMembers.Values)
                                if (member.PeerRemoteToLocalSocket?.Equals(udpEndPoint) ?? false)
                                {
                                    p2pMember = member;
                                    break;
                                }
                    }
                    else
                    {
                        client.P2PGroup.P2PMembers.TryGetValue(message.RelayFrom, out p2pMember);
                    }

                    if (p2pMember != null)
                    {
                        var selfPong = new NetMessage();
                        selfPong.Write(clientTime);
                        selfPong.Write(serverTime);
                        selfPong.Write(serverPing);
                        selfPong.Write(frameRate);
                        selfPong.Reliable = true;

                        // p2pMember.RmiToPeer((ushort)NexumOpCode.ReportServerTimeAndFrameRateAndPong, selfPong, true);
                    }

                    break;
                }

                default:
                    client.OnRMIRecieve(message, rmiId);
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
                var rsaSequence = (DerSequence)Asn1Object.FromByteArray(encodedRsaKey);

                var rsaParameters = new RSAParameters
                {
                    Exponent = ((DerInteger)rsaSequence[1]).Value.ToByteArrayUnsigned(),
                    Modulus = ((DerInteger)rsaSequence[0]).Value.ToByteArrayUnsigned()
                };

                client.RSA = new RSACryptoServiceProvider(2048);
                client.RSA.ImportParameters(rsaParameters);

                var response = new NetMessage();
                response.WriteEnum(MessageType.NotifyCSEncryptedSessionKey);
                response.Write(new ByteArray(client.RSA.Encrypt(client.Crypt.GetKey(), true)));
                response.Write(new ByteArray(client.Crypt.EncryptKey(client.Crypt.GetFastKey())));

                client.NexumToServer(response);
            }
        }

        private static void NotifyCSSessionKeySuccessHandler(NetClient client)
        {
            client.Logger.Debug("NotifyCSSessionKeySuccess");

            var message = new NetMessage();
            message.WriteEnum(MessageType.NotifyServerConnectionRequestData);
            message.Write(new ByteArray());

            switch (client.ServerType)
            {
                case ServerType.Auth:
                    message.Write(new Guid("{9be73c0b-3b10-403e-be7d-9f222702a38c}"));
                    break;

                case ServerType.Game:
                    message.Write(new Guid("{beb92241-8333-4117-ab92-9b4af78c688f}"));
                    break;

                case ServerType.Chat:
                    message.Write(new Guid("{97d36acf-8cc0-4dfb-bcc9-97cab255e2bc}"));
                    break;

                case ServerType.Relay:
                    message.Write(new Guid("{a43a97d1-9ec7-495e-ad5f-8fe45fde1151}"));
                    break;
            }

            message.Write(Constants.NetVersion);
            client.NexumToServer(message);
        }

        private static void NotifyServerConnectSuccessHandler(NetClient client, NetMessage message)
        {
            var payload = new ByteArray();
            var serverEndPoint = new IPEndPoint(IPAddress.Any, 0);

            if (!message.Read(out uint hostId)
                || !message.Read(out Guid serverGuid)
                || !message.Read(ref payload)
                || !message.ReadIPEndPoint(ref serverEndPoint))
                return;

            client.Logger.Debug(
                "NotifyServerConnectSuccess => hostId = {HostId}, serverGuid = {ServerGuid}, endpoint = {Endpoint}",
                hostId, serverGuid, serverEndPoint);

            client.HostId = hostId;
            client.UdpDefragBoard.LocalHostId = hostId;
            client.ServerGuid = serverGuid;
            client.OnConnectionComplete();
        }

        private static void UnreliableRelay2Handler(NetClient client, NetMessage message)
        {
            if (!message.Read(out uint hostId))
                return;

            var packet = new ByteArray();
            if (!message.Read(ref packet))
                return;

            var netMessage = new NetMessage(packet) { RelayFrom = hostId };
            ReadMessage(client, netMessage);
        }

        private static void ReliableRelay2Handler(NetClient client, NetMessage message)
        {
            if (!message.Read(out uint hostId))
                return;

            if (!message.Read(out uint _))
                return;

            var packet = new ByteArray();
            if (!message.Read(ref packet))
                return;

            if (!ReliableUdpHelper.UnwrapPayload(packet.GetBuffer(), out byte[] unwrappedData))
                return;

            var netMessage = new NetMessage(new ByteArray(unwrappedData)) { RelayFrom = hostId };
            ReadMessage(client, netMessage);
        }

        private static void UnreliablePongHandler(NetClient client, NetMessage message)
        {
            if (!message.Read(out double sentTime) || !message.Read(out double _))
                return;

            double currentTime = client.GetAbsoluteTime();
            double lastPing = (currentTime - sentTime) / 2.0;

            if (lastPing < 0)
                lastPing = 0;

            client.ServerUdpLastPing = lastPing;
            client.ServerUdpRecentPing = client.ServerUdpRecentPing != 0.0
                ? Core.SysUtil.Lerp(client.ServerUdpRecentPing, lastPing, ReliableUdpConfig.LagLinearProgrammingFactor)
                : lastPing;
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
