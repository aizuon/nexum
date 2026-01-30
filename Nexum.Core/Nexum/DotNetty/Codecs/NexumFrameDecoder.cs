using System;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;

namespace Nexum.Core.DotNetty.Codecs
{
    internal sealed class NexumFrameDecoder : LengthFieldBasedFrameDecoder
    {
        internal NexumFrameDecoder(int maxFrameLength)
            : base(ByteOrder.LittleEndian, maxFrameLength, 2, 1, 0, 0, true)
        {
        }

        protected override long GetUnadjustedFrameLength(IByteBuffer buffer, int offset, int length, ByteOrder order)
        {
            byte scalarPrefix = buffer.GetByte(offset++);
            if (buffer.ReadableBytes - (offset - buffer.ReaderIndex) < scalarPrefix)
                return scalarPrefix;

            switch (scalarPrefix)
            {
                case 1:
                    return buffer.GetByte(offset) + scalarPrefix;

                case 2:
                    return buffer.GetShortLE(offset) + scalarPrefix;

                case 4:
                    return buffer.GetIntLE(offset) + scalarPrefix;

                default:
                    throw new Exception("Invalid scalar prefix " + scalarPrefix);
            }
        }

        protected override IByteBuffer ExtractFrame(IChannelHandlerContext context, IByteBuffer buffer, int index,
            int length)
        {
            int bytesToSkip = 2;
            var frame = buffer.Slice(index + bytesToSkip, length - bytesToSkip);
            frame.Retain();
            return frame;
        }
    }
}
