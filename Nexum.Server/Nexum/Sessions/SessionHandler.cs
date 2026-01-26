using System;
using System.Net.Sockets;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;
using Nexum.Core.Configuration;
using Nexum.Core.Message.S2C;
using Nexum.Core.Routing;
using Nexum.Core.Serialization;
using Nexum.Server.Core;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Security;
using Serilog;
using SerilogConstants = Serilog.Core.Constants;

namespace Nexum.Server.Sessions
{
    internal sealed class SessionHandler : ChannelHandlerAdapter
    {
        internal readonly ILogger Logger;

        internal readonly NetServer Owner;

        internal SessionHandler(NetServer owner)
        {
            Owner = owner;
            Logger = Log.ForContext(SerilogConstants.SourceContextPropertyName,
                $"{owner.ServerName}{nameof(SessionHandler)}");
        }

        public override void ChannelActive(IChannelHandlerContext context)
        {
            uint hostId = Owner.HostIdFactory.New();
            var session = new NetSession(Owner, hostId, context.Channel);
            Owner.SessionsInternal.TryAdd(hostId, session);
            context.Channel.GetAttribute(ChannelAttributes.Session).Set(session);

            session.SetConnectionState(ConnectionState.Handshaking);

            session.Logger.Debug(
                "New incoming client({HostId}) => {EndPoint}, total sessions = {SessionCount}",
                hostId, session.RemoteEndPoint, Owner.Sessions.Count);

            var config = Owner.NetSettings;
            session.UdpDefragBoard.MaxMessageLength = config.MessageMaxLength;

            var pubKey = DotNetUtilities.GetRsaPublicKey(Owner.RSA.ExportParameters(false));
            var pubKeyStruct = new RsaPublicKeyStructure(pubKey.Modulus, pubKey.Exponent);
            byte[] encodedKey = pubKeyStruct.GetDerEncoded();

            var notifyServerConnectionHint = new NotifyServerConnectionHint
            {
                EnableServerLog = config.EnableServerLog,
                FallbackMethod = config.FallbackMethod,
                MessageMaxLength = config.MessageMaxLength,
                IdleTimeout = config.IdleTimeout,
                DirectP2PStartCondition = config.DirectP2PStartCondition,
                OverSendSuspectingThresholdInBytes = config.OverSendSuspectingThresholdInBytes,
                EnableNagleAlgorithm = config.EnableNagleAlgorithm,
                EncryptedMessageKeyLength = config.EncryptedMessageKeyLength,
                FastEncryptedMessageKeyLength = config.FastEncryptedMessageKeyLength,
                AllowServerAsP2PGroupMember = config.AllowServerAsP2PGroupMember,
                EnableP2PEncryptedMessaging = config.EnableP2PEncryptedMessaging,
                UpnpDetectNatDevice = config.UpnpDetectNatDevice,
                UpnpTcpAddrPortMapping = config.UpnpTcpAddrPortMapping,
                EnableLookaheadP2PSend = config.EnableLookaheadP2PSend,
                EnablePingTest = config.EnablePingTest,
                EmergencyLogLineCount = config.EmergencyLogLineCount,
                RsaPublicKey = new ByteArray(encodedKey, true)
            };

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

            session.Logger.Debug(
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
                session?.Logger.Debug("Session timed out due to inactivity ({IdleTimeout}s), closing connection",
                    Owner.NetSettings.IdleTimeout);
                context.CloseAsync();
                return;
            }

            base.UserEventTriggered(context, evt);
        }
    }
}
