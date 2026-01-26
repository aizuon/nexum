using System;
using System.Net;
using BaseLib.Extensions;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Client.Core
{
    internal sealed class NetClientAdapter : ChannelHandlerAdapter
    {
        private readonly NetClient _owner;
        private ILogger _logger;

        internal NetClientAdapter(NetClient owner)
        {
            _owner = owner;
            _logger = Log.ForContext(Constants.SourceContextPropertyName,
                $"{owner.ServerName}ClientAdapter");
        }

        internal void UpdateLoggerContext(string context)
        {
            _logger = Log.ForContext(Constants.SourceContextPropertyName, context);
        }

        public override void ChannelInactive(IChannelHandlerContext context)
        {
            base.ChannelInactive(context);
            var remoteAddress = context.Channel.RemoteAddress;
            _logger.Debug("Client disconnected from {ServerName} at {RemoteAddress}",
                _owner.ServerName, ((IPEndPoint)remoteAddress).ToIPv4String());

            _owner.SetConnectionState(ConnectionState.Disconnected);
        }

        public override void ChannelActive(IChannelHandlerContext context)
        {
            base.ChannelActive(context);

            _owner.Channel = context.Channel;

            var remoteAddress = context.Channel.RemoteAddress;
            _logger.Debug("Client connected to {ServerName} at {RemoteAddress}",
                _owner.ServerName, ((IPEndPoint)remoteAddress).ToIPv4String());

            _owner.SetConnectionState(ConnectionState.Handshaking);
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var buffer = message as IByteBuffer;
            int readableBytes = buffer.ReadableBytes;
            byte[] data = GC.AllocateUninitializedArray<byte>(readableBytes);
            buffer.GetBytes(buffer.ReaderIndex, data, 0, readableBytes);

            var receivedMessage = new NetMessage(data, readableBytes, true);

            NetClientHandler.ReadFrame(_owner, receivedMessage);

            buffer.Release();
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            _logger.Error(exception, "Unhandled exception in client channel pipeline");
        }
    }
}
