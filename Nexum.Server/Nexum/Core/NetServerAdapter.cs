using System;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using Nexum.Core.Serialization;
using Serilog;
using SerilogConstants = Serilog.Core.Constants;

namespace Nexum.Server.Core
{
    internal sealed class NetServerAdapter : ChannelHandlerAdapter
    {
        internal readonly ILogger Logger;
        internal readonly NetServer Owner;

        internal NetServerAdapter(NetServer owner)
        {
            Owner = owner;
            Logger = Log.ForContext(SerilogConstants.SourceContextPropertyName,
                $"{owner.ServerName}ServerAdapter");
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var buffer = message as IByteBuffer;
            int readableBytes = buffer.ReadableBytes;
            byte[] data = GC.AllocateUninitializedArray<byte>(readableBytes);
            buffer.GetBytes(buffer.ReaderIndex, data, 0, readableBytes);

            var session = context.Channel.GetAttribute(ChannelAttributes.Session).Get();

            var netMessage = new NetMessage(data, readableBytes, true);

            NetServerHandler.ReadFrame(Owner, session, netMessage);

            buffer.Release();
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            var session = context.Channel.GetAttribute(ChannelAttributes.Session).Get();
            (session?.Logger ?? Owner.Logger ?? Logger).Error(exception,
                "Unhandled exception in server channel pipeline");
        }
    }
}
