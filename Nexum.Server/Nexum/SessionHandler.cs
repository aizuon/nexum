using System;
using System.Net.Sockets;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;
using Nexum.Core;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Security;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Server
{
    internal sealed class SessionHandler : ChannelHandlerAdapter
    {
        public readonly ILogger Logger;

        internal readonly NetServer Owner;

        public SessionHandler(NetServer owner)
        {
            Owner = owner;
            Logger = Log.ForContext(Constants.SourceContextPropertyName, owner.ServerType + "SessionHandler");
        }

        public override void ChannelActive(IChannelHandlerContext context)
        {
            uint hostId = Owner.HostIdFactory.New();
            var session = new NetSession(Owner, hostId, context.Channel);
            Owner.SessionsInternal.TryAdd(hostId, session);
            context.Channel.GetAttribute(ChannelAttributes.Session).Set(session);

            session.SetConnectionState(ConnectionState.Handshaking);

            session.Logger.Information(
                "New incoming client({HostId}) => {EndPoint}, total sessions = {SessionCount}",
                hostId, session.RemoteEndPoint, Owner.Sessions.Count);

            var config = Owner.NetSettings;
            session.UdpDefragBoard.MaxMessageLength = config.MessageMaxLength;

            var notifyServerConnectionHint = new NetMessage();
            notifyServerConnectionHint.WriteEnum(MessageType.NotifyServerConnectionHint);
            notifyServerConnectionHint.Write(config.EnableServerLog);
            notifyServerConnectionHint.WriteEnum(config.FallbackMethod);
            notifyServerConnectionHint.Write(config.MessageMaxLength);
            notifyServerConnectionHint.Write(config.IdleTimeout);
            notifyServerConnectionHint.WriteEnum(config.DirectP2PStartCondition);
            notifyServerConnectionHint.Write(config.OverSendSuspectingThresholdInBytes);
            notifyServerConnectionHint.Write(config.EnableNagleAlgorithm);
            notifyServerConnectionHint.Write(config.EncryptedMessageKeyLength);
            notifyServerConnectionHint.Write(config.FastEncryptedMessageKeyLength);
            notifyServerConnectionHint.Write(config.AllowServerAsP2PGroupMember);
            notifyServerConnectionHint.Write(config.EnableP2PEncryptedMessaging);
            notifyServerConnectionHint.Write(config.UpnpDetectNatDevice);
            notifyServerConnectionHint.Write(config.UpnpTcpAddrPortMapping);
            notifyServerConnectionHint.Write(config.EnableLookaheadP2PSend);
            notifyServerConnectionHint.Write(config.EnablePingTest);
            notifyServerConnectionHint.Write(config.EmergencyLogLineCount);

            var pubKey = DotNetUtilities.GetRsaPublicKey(Owner.RSA.ExportParameters(false));
            var pubKeyStruct = new RsaPublicKeyStructure(pubKey.Modulus, pubKey.Exponent);
            byte[] encodedKey = pubKeyStruct.GetDerEncoded();

            notifyServerConnectionHint.Write(new ByteArray(encodedKey));

            session.NexumToClient(notifyServerConnectionHint);
        }

        public override void ChannelInactive(IChannelHandlerContext context)
        {
            var session = context.Channel.GetAttribute(ChannelAttributes.Session).Get();

            if (session == null)
            {
                base.ChannelInactive(context);
                return;
            }

            Owner.MagicNumberSessions.TryRemove(session.HolepunchMagicNumber, out _);
            if (session.UdpSessionInitialized)
                Owner.UdpSessions.TryRemove(FilterTag.Create(session.HostId, (uint)HostId.Server), out _);

            session.Dispose();
            Owner.SessionsInternal.TryRemove(session.HostId, out _);
            Owner.HostIdFactory.Free(session.HostId);

            session.Logger.Information(
                "Client({HostId}) disconnected, remaining sessions = {SessionCount}",
                session.HostId, Owner.Sessions.Count);

            base.ChannelInactive(context);
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            var socketEx = exception as SocketException;
            if (socketEx?.ErrorCode == 10053)
                return;
            Logger.Error(exception, "Unhandled exception");
        }

        public override void UserEventTriggered(IChannelHandlerContext context, object evt)
        {
            if (evt is IdleStateEvent idleEvent && idleEvent.State == IdleState.AllIdle)
            {
                var session = context.Channel.GetAttribute(ChannelAttributes.Session).Get();
                session?.Logger.Information("Session timed out due to inactivity ({IdleTimeout}s), closing connection",
                    Owner.NetSettings.IdleTimeout);
                context.CloseAsync();
                return;
            }

            base.UserEventTriggered(context, evt);
        }
    }
}
