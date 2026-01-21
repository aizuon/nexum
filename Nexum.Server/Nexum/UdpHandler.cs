using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BaseLib.Extensions;
using DotNetty.Transport.Channels;
using Nexum.Core;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Server
{
    internal sealed class UdpHandler : ChannelHandlerAdapter
    {
        internal static readonly ILogger
            Logger = Log.ForContext(Constants.SourceContextPropertyName, nameof(UdpHandler));

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        internal readonly NetServer Owner;

        internal UdpHandler(NetServer owner)
        {
            Owner = owner;
        }

        public override void ChannelRead(IChannelHandlerContext context, object obj)
        {
            var message = obj as UdpMessage;

            var log = Logger.ForContext("EndPoint",
                message.EndPoint.ToIPv4String());

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
                    log.Warning("UDP defragmentation error for unknown session: {Error}", defragError);
                    message.Content.Release();
                    return;
                }

                var holepunchMessage = new NetMessage(holepunchPacket.Packet.AssembledData, true);

                holepunchMessage.Read(out byte coreid);
                var messageType = (MessageType)coreid;

                if (messageType != MessageType.ServerHolepunch)
                {
                    log.Warning("Expected ServerHolepunch as first UDP message but got {MessageType}",
                        messageType);
                    message.Content.Release();
                    return;
                }

                holepunchMessage.Read(out Guid magicNumber);

                Owner.MagicNumberSessions.TryGetValue(magicNumber, out var session2);

                if (session2 == null)
                {
                    log.Warning("Invalid holepunch magic number {MagicNumber}", magicNumber);
                    return;
                }

                lock (session2.UdpInitLock)
                {
                    if (session2.UdpSessionInitialized)
                        return;

                    session2.UdpSessionInitialized = true;
                    session2.UdpEndPointInternal = message.EndPoint;
                    Owner.UdpSessions.TryAdd(FilterTag.Create(session2.HostId, (uint)HostId.Server), session2);
                }

                session2.Logger.Debug("UDP holepunch successful, endpoint = {UdpEndPoint}",
                    session2.UdpEndPoint);

                var serverHolepunchAck = new NetMessage();
                serverHolepunchAck.WriteEnum(MessageType.ServerHolepunchAck);
                serverHolepunchAck.Write(session2.HolepunchMagicNumber);
                serverHolepunchAck.Write(session2.UdpEndPoint);

                session2.NexumToClientUdpIfAvailable(serverHolepunchAck, true);
                var capturedSession = session2;
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
