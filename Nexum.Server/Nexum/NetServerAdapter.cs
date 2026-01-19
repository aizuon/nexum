using System;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using Nexum.Core;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Server
{
    internal sealed class NetServerAdapter : ChannelHandlerAdapter
    {
        internal readonly ILogger Logger;
        internal readonly NetServer Owner;

        internal NetServerAdapter(NetServer owner)
        {
            Owner = owner;
            Logger = Log.ForContext(Constants.SourceContextPropertyName, owner.ServerType + "ServerAdapter");
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var buffer = message as IByteBuffer;
            int offset = buffer.ArrayOffset + buffer.ReaderIndex;
            int length = buffer.ReadableBytes;
            byte[] data = GC.AllocateUninitializedArray<byte>(length);
            Buffer.BlockCopy(buffer.Array, offset, data, 0, length);

            var session = context.Channel.GetAttribute(ChannelAttributes.Session).Get();

            var netMessage = new NetMessage(new ByteArray(data, length));

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
