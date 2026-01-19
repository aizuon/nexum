using System;
using System.Net;
using BaseLib.Extensions;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using Nexum.Core;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Client
{
    internal sealed class NetClientAdapter : ChannelHandlerAdapter
    {
        private readonly ILogger _logger;
        private readonly NetClient _owner;

        internal NetClientAdapter(NetClient owner)
        {
            _owner = owner;
            _logger = Log.ForContext(Constants.SourceContextPropertyName, owner.ServerType + "ClientAdapter");
        }

        public override void ChannelInactive(IChannelHandlerContext context)
        {
            base.ChannelInactive(context);
            var remoteAddress = context.Channel.RemoteAddress;
            _logger.Information("Client disconnected from {ServerType} at {RemoteAddress}",
                _owner.ServerType, ((IPEndPoint)remoteAddress).ToIPv4String());
        }

        public override void ChannelActive(IChannelHandlerContext context)
        {
            base.ChannelActive(context);
            var remoteAddress = context.Channel.RemoteAddress;
            _logger.Information("Client connected to {ServerType} at {RemoteAddress}",
                _owner.ServerType, ((IPEndPoint)remoteAddress).ToIPv4String());
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var buffer = message as IByteBuffer;
            int readableBytes = buffer.ReadableBytes;
            byte[] data = GC.AllocateUninitializedArray<byte>(readableBytes);
            buffer.GetBytes(buffer.ReaderIndex, data, 0, readableBytes);

            var netMessage = new NetMessage(data, readableBytes);

            NetClientHandler.ReadFrame(_owner, netMessage);

            buffer.Release();
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            _logger.Error(exception, "Unhandled exception in client channel pipeline");
        }
    }
}
