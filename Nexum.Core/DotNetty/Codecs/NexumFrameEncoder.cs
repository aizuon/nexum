using System.Collections.Generic;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;

namespace Nexum.Core.DotNetty.Codecs
{
    internal sealed class NexumFrameEncoder : MessageToMessageEncoder<IByteBuffer>
    {
        protected override void Encode(IChannelHandlerContext context, IByteBuffer message, List<object> output)
        {
            var buffer = context.Allocator
                .Buffer(2 + message.ReadableBytes)
                .WriteShortLE(Constants.TcpSplitter);

            output.Add(buffer);
            output.Add(message.Retain());
        }
    }
}
