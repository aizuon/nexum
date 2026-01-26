using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BaseLib.Extensions;
using DotNetty.Transport.Channels;
using Nexum.Core.Configuration;
using Nexum.Core.Fragmentation;
using Nexum.Core.Holepunching;
using Nexum.Core.Message.X2X;
using Nexum.Core.Routing;
using Nexum.Core.Serialization;
using Nexum.Core.Udp;
using Nexum.Server.Core;
using Serilog;
using SerilogConstants = Serilog.Core.Constants;

namespace Nexum.Server.Udp
{
    internal sealed class UdpHandler : ChannelHandlerAdapter
    {
        internal readonly ILogger
            _logger;

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        internal readonly NetServer Owner;

        internal UdpHandler(NetServer owner, int port)
        {
            Owner = owner;
            _logger = Log.ForContext(SerilogConstants.SourceContextPropertyName, $"{nameof(UdpHandler)}({port})");
        }

        private ILogger GetLoggerForUnknownSession(UdpMessage message)
        {
            return _logger.ForContext("EndPoint",
                message.EndPoint.ToIPv4String());
        }

        public override void ChannelRead(IChannelHandlerContext context, object obj)
        {
            var message = obj as UdpMessage;

            Owner.UdpSessions.TryGetValue(message.FilterTag, out var session);

            if (session == null)
            {
                var defragResult = Owner.UdpDefragBoard.PushFragment(
                    message,
                    (uint)HostId.None,
                    _stopwatch.Elapsed.TotalSeconds,
                    out var holepunchPacket,
                    out string defragError);

                if (defragResult == AssembledPacketError.Assembling)
                {
                    message.Content.Release();
                    return;
                }

                if (defragResult == AssembledPacketError.Error)
                {
                    GetLoggerForUnknownSession(message)
                        .Warning("UDP defragmentation error for unknown session: {Error}", defragError);
                    message.Content.Release();
                    return;
                }

                var holepunchMessage = new NetMessage(holepunchPacket.Packet.AssembledData, true);

                if (!holepunchMessage.Read<MessageType>(out var messageType))
                    return;

                if (messageType != MessageType.ServerHolepunch)
                {
                    GetLoggerForUnknownSession(message).Warning(
                        "Expected ServerHolepunch as first UDP message but got {MessageType}",
                        messageType);
                    message.Content.Release();
                    return;
                }

                if (!ServerHolepunch.Deserialize(holepunchMessage, out var holepunchPacketData))
                    return;

                Owner.MagicNumberSessions.TryGetValue(holepunchPacketData.MagicNumber, out session);

                if (session == null)
                {
                    GetLoggerForUnknownSession(message)
                        .Warning("Invalid holepunch magic number {MagicNumber}", holepunchPacketData.MagicNumber);
                    return;
                }

                lock (session.UdpInitLock)
                {
                    if (session.UdpSessionInitialized)
                        return;

                    session.UdpSessionInitialized = true;
                    session.UdpEndPointInternal = message.EndPoint;
                }

                Owner.UdpSessions.TryAdd(FilterTag.Create(session.HostId, (uint)HostId.Server), session);
                session.Logger.Debug("UDP holepunch successful, endpoint = {UdpEndPoint}",
                    session.UdpEndPoint);

                session.NexumToClientUdpIfAvailable(
                    HolepunchHelper.CreateServerHolepunchAckMessage(session.HolepunchMagicNumber, session.UdpEndPoint),
                    true);
                var capturedSession = session;
                HolepunchHelper.SendBurstMessagesWithCheck(
                    () => HolepunchHelper.CreateServerHolepunchAckMessage(
                        capturedSession.HolepunchMagicNumber, capturedSession.UdpEndPoint),
                    msg => capturedSession.NexumToClientUdpIfAvailable(msg, true),
                    () => !capturedSession.UdpEnabled
                );
                message.Content.Release();
                return;
            }

            session.LastUdpPing = DateTimeOffset.Now;

            double currentTime = session.GetAbsoluteTime();

            var result = session.UdpDefragBoard.PushFragment(
                message,
                session.HostId,
                currentTime,
                out var assembledPacket,
                out string error);

            if (result == AssembledPacketError.Assembling)
            {
                message.Content.Release();
                return;
            }

            if (result == AssembledPacketError.Error)
            {
                session.Logger.Warning("UDP defragmentation error: {Error}", error);
                message.Content.Release();
                return;
            }

            var assembledMessage = new NetMessage(assembledPacket.Packet.AssembledData, true);
            NetServerHandler.ReadFrame(Owner, session, assembledMessage, message.EndPoint, true);

            message.Content.Release();
        }

        public override Task WriteAsync(IChannelHandlerContext context, object message)
        {
            var udpMessage = message as UdpMessage;
            return base.WriteAsync(context, udpMessage);
        }
    }
}
