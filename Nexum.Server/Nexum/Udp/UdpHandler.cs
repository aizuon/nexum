using System;
using System.Net;
using BaseLib.Extensions;
using DotNetty.Transport.Channels;
using Nexum.Core.Configuration;
using Nexum.Core.DotNetty.Codecs;
using Nexum.Core.Fragmentation;
using Nexum.Core.Holepunching;
using Nexum.Core.Message.X2X;
using Nexum.Core.ReliableUdp;
using Nexum.Core.Routing;
using Nexum.Core.Serialization;
using Nexum.Core.Udp;
using Nexum.Server.Core;
using Nexum.Server.Sessions;
using Serilog;
using SerilogConstants = Serilog.Core.Constants;

namespace Nexum.Server.Udp
{
    internal sealed class UdpHandler : ChannelHandlerAdapter
    {
        internal readonly ILogger _logger;

        internal readonly NetServer Owner;

        internal UdpHandler(NetServer owner, int port)
        {
            Owner = owner;
            _logger = Log.ForContext(SerilogConstants.SourceContextPropertyName, $"{nameof(UdpHandler)}({port})");
        }

        private ILogger GetLoggerForUnknownSession(IPEndPoint endPoint)
        {
            return _logger.ForContext("EndPoint", endPoint.ToIPv4String());
        }

        public override void ChannelRead(IChannelHandlerContext context, object obj)
        {
            switch (obj)
            {
                case InboundReliableUdpFrame reliableFrame:
                    HandleReliableUdpFrame(context, reliableFrame);
                    return;

                case InboundUdpMessage inboundMsg:
                    HandleInboundUdpMessage(context, inboundMsg);
                    return;

                case AssembledPacket assembledPacket:
                    HandleAssembledPacket(context, assembledPacket);
                    return;

                default:
                    _logger.Warning("Received unknown message type: {Type}", obj?.GetType().Name);
                    return;
            }
        }

        private void HandleReliableUdpFrame(IChannelHandlerContext context, InboundReliableUdpFrame reliableFrame)
        {
            var session = FindSessionByEndpoint(reliableFrame.SenderEndPoint, reliableFrame.FilterTag);

            if (session == null)
            {
                GetLoggerForUnknownSession(reliableFrame.SenderEndPoint)
                    .Verbose("ReliableUdp_Frame from unknown session, ignoring");
                return;
            }

            session.LastUdpPing = DateTimeOffset.Now;

            if (session.ClientReliableUdp != null)
            {
                session.ClientReliableUdp.TakeReceivedFrame(reliableFrame.Frame);
                ExtractMessagesFromReliableUdpStream(session, reliableFrame.SenderEndPoint);
            }
            else
            {
                if (reliableFrame.Frame.Type == ReliableUdpFrameType.Data && reliableFrame.Frame.Data != null)
                    if (ReliableUdpHelper.UnwrapPayload(reliableFrame.Frame.Data, out var payload))
                    {
                        var innerMessage = new NetMessage(payload.ToArray(), true);
                        NetServerHandler.ReadMessage(Owner, session, innerMessage, reliableFrame.SenderEndPoint);
                    }
            }
        }

        private void HandleInboundUdpMessage(IChannelHandlerContext context, InboundUdpMessage inboundMsg)
        {
            var assembledPacket = inboundMsg.AssembledPacket;
            var session = FindSessionByEndpoint(assembledPacket.SenderEndPoint, assembledPacket.FilterTag);

            if (session == null)
            {
                HandleHolepunchMessage(inboundMsg.NetMessage, inboundMsg.MessageType, assembledPacket.SenderEndPoint);
                return;
            }

            session.LastUdpPing = DateTimeOffset.Now;

            inboundMsg.NetMessage.ReadOffset = 0;
            NetServerHandler.ReadFrame(Owner, session, inboundMsg.NetMessage, assembledPacket.SenderEndPoint, true);
        }

        private void HandleAssembledPacket(IChannelHandlerContext context, AssembledPacket assembledPacket)
        {
            var session = FindSessionByEndpoint(assembledPacket.SenderEndPoint, assembledPacket.FilterTag);

            if (session == null)
            {
                var netMessage = new NetMessage(assembledPacket.Packet.AssembledData, true);
                if (!netMessage.Read<MessageType>(out var messageType))
                    return;

                HandleHolepunchMessage(netMessage, messageType, assembledPacket.SenderEndPoint);
                return;
            }

            session.LastUdpPing = DateTimeOffset.Now;

            var assembledMessage = new NetMessage(assembledPacket.Packet.AssembledData, true);
            NetServerHandler.ReadFrame(Owner, session, assembledMessage, assembledPacket.SenderEndPoint, true);
        }

        private void HandleHolepunchMessage(NetMessage netMessage, MessageType messageType, IPEndPoint endPoint)
        {
            if (messageType != MessageType.ServerHolepunch)
            {
                GetLoggerForUnknownSession(endPoint).Warning(
                    "Expected ServerHolepunch as first UDP message but got {MessageType}",
                    messageType);
                return;
            }

            if (!ServerHolepunch.Deserialize(netMessage, out var holepunchPacketData))
                return;

            Owner.MagicNumberSessions.TryGetValue(holepunchPacketData.MagicNumber, out var session);

            if (session == null)
            {
                GetLoggerForUnknownSession(endPoint)
                    .Warning("Invalid holepunch magic number {MagicNumber}", holepunchPacketData.MagicNumber);
                return;
            }

            lock (session.UdpInitLock)
            {
                if (session.UdpSessionInitialized)
                    return;

                session.UdpSessionInitialized = true;
                session.UdpEndPointInternal = endPoint;
            }

            Owner.UdpSessions.TryAdd(FilterTag.Create(session.HostId, (uint)HostId.Server), session);
            session.Logger.Debug("UDP holepunch successful, endpoint = {UdpEndPoint}",
                session.UdpEndPoint);

            var serverHolepunchAckMsg = new ServerHolepunchAck
            {
                MagicNumber = session.HolepunchMagicNumber,
                EndPoint = session.UdpEndPoint
            }.Serialize();
            var capturedSession = session;
            session.NexumToClientUdpIfAvailable(serverHolepunchAckMsg, true);
            HolepunchHelper.SendBurstMessagesWithCheck(
                serverHolepunchAckMsg,
                msg => capturedSession.NexumToClientUdpIfAvailable(msg, true),
                () => !capturedSession.UdpEnabled
            );
        }

        private NetSession FindSessionByEndpoint(IPEndPoint endPoint, ushort filterTag)
        {
            if (filterTag != 0)
            {
                Owner.UdpSessions.TryGetValue(filterTag, out var session);
                if (session != null)
                    return session;
            }

            foreach (var kvp in Owner.UdpSessions)
                if (kvp.Value.UdpEndPoint?.Equals(endPoint) == true)
                    return kvp.Value;

            return null;
        }

        private void ExtractMessagesFromReliableUdpStream(NetSession session, IPEndPoint udpEndPoint)
        {
            var stream = session.ClientReliableUdp?.ReceivedStream;
            ReliableUdpHelper.ExtractMessagesFromStream(stream,
                msg => NetServerHandler.ReadMessage(Owner, session, msg, udpEndPoint));
        }
    }
}
