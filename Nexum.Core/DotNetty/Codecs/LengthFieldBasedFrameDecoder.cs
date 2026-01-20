using System;
using System.Collections.Generic;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;

namespace Nexum.Core.DotNetty.Codecs
{
    internal class LengthFieldBasedFrameDecoder : ByteToMessageDecoder
    {
        internal readonly ByteOrder ByteOrder;
        internal readonly bool FailFast;
        internal readonly int InitialBytesToStrip;
        internal readonly int LengthAdjustment;
        internal readonly int LengthFieldEndOffset;
        internal readonly int LengthFieldLength;
        internal readonly int LengthFieldOffset;
        internal readonly int MaxFrameLength;
        internal long BytesToDiscard;
        internal bool DiscardingTooLongFrame;
        internal long TooLongFrameLength;

        internal LengthFieldBasedFrameDecoder(int maxFrameLength, int lengthFieldOffset, int lengthFieldLength)
            : this(maxFrameLength, lengthFieldOffset, lengthFieldLength, 0, 0)
        {
        }

        internal LengthFieldBasedFrameDecoder(int maxFrameLength, int lengthFieldOffset, int lengthFieldLength,
            int lengthAdjustment, int initialBytesToStrip)
            : this(maxFrameLength, lengthFieldOffset, lengthFieldLength, lengthAdjustment, initialBytesToStrip, true)
        {
        }

        internal LengthFieldBasedFrameDecoder(int maxFrameLength, int lengthFieldOffset, int lengthFieldLength,
            int lengthAdjustment, int initialBytesToStrip, bool failFast)
            : this(ByteOrder.BigEndian, maxFrameLength, lengthFieldOffset, lengthFieldLength, lengthAdjustment,
                initialBytesToStrip, failFast)
        {
        }

        internal LengthFieldBasedFrameDecoder(ByteOrder byteOrder, int maxFrameLength, int lengthFieldOffset,
            int lengthFieldLength, int lengthAdjustment, int initialBytesToStrip, bool failFast)
        {
            if (maxFrameLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxFrameLength),
                    "maxFrameLength must be a positive integer: " + maxFrameLength);
            if (lengthFieldOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(lengthFieldOffset),
                    "lengthFieldOffset must be a non-negative integer: " + lengthFieldOffset);
            if (initialBytesToStrip < 0)
                throw new ArgumentOutOfRangeException(nameof(initialBytesToStrip),
                    "initialBytesToStrip must be a non-negative integer: " + initialBytesToStrip);
            if (lengthFieldOffset > maxFrameLength - lengthFieldLength)
                throw new ArgumentOutOfRangeException(nameof(maxFrameLength),
                    "maxFrameLength (" + maxFrameLength + ") must be equal to or greater than lengthFieldOffset (" +
                    lengthFieldOffset + ") + lengthFieldLength (" + lengthFieldLength + ").");
            ByteOrder = byteOrder;
            MaxFrameLength = maxFrameLength;
            LengthFieldOffset = lengthFieldOffset;
            LengthFieldLength = lengthFieldLength;
            LengthAdjustment = lengthAdjustment;
            LengthFieldEndOffset = lengthFieldOffset + lengthFieldLength;
            InitialBytesToStrip = initialBytesToStrip;
            FailFast = failFast;
        }

        protected override void Decode(IChannelHandlerContext context, IByteBuffer input, List<object> output)
        {
            object obj = Decode(context, input);
            if (obj == null)
                return;
            output.Add(obj);
        }

        protected object Decode(IChannelHandlerContext context, IByteBuffer input)
        {
            if (DiscardingTooLongFrame)
            {
                long bytesToDiscard = BytesToDiscard;
                int length = (int)Math.Min(bytesToDiscard, input.ReadableBytes);
                input.SkipBytes(length);
                BytesToDiscard = bytesToDiscard - length;
                FailIfNecessary(false);
            }

            if (input.ReadableBytes < LengthFieldEndOffset)
                return null;
            int offset = input.ReaderIndex + LengthFieldOffset;
            long unadjustedFrameLength = GetUnadjustedFrameLength(input, offset, LengthFieldLength, ByteOrder);
            if (unadjustedFrameLength < 0L)
            {
                input.SkipBytes(LengthFieldEndOffset);
                throw new CorruptedFrameException("negative pre-adjustment length field: " + unadjustedFrameLength);
            }

            long num1 = unadjustedFrameLength + (LengthAdjustment + LengthFieldEndOffset);
            if (num1 < LengthFieldEndOffset)
            {
                input.SkipBytes(LengthFieldEndOffset);
                throw new CorruptedFrameException("Adjusted frame length (" + num1 +
                                                  ") is less than lengthFieldEndOffset: " + LengthFieldEndOffset);
            }

            if (num1 > MaxFrameLength)
            {
                long num2 = num1 - input.ReadableBytes;
                TooLongFrameLength = num1;
                if (num2 < 0L)
                {
                    input.SkipBytes((int)num1);
                }
                else
                {
                    DiscardingTooLongFrame = true;
                    BytesToDiscard = num2;
                    input.SkipBytes(input.ReadableBytes);
                }

                FailIfNecessary(true);
                return null;
            }

            int length1 = (int)num1;
            if (input.ReadableBytes < length1)
                return null;
            if (InitialBytesToStrip > length1)
            {
                input.SkipBytes(length1);
                throw new CorruptedFrameException("Adjusted frame length (" + num1 +
                                                  ") is less than initialBytesToStrip: " + InitialBytesToStrip);
            }

            input.SkipBytes(InitialBytesToStrip);
            int readerIndex = input.ReaderIndex;
            int length2 = length1 - InitialBytesToStrip;
            var frame = ExtractFrame(context, input, readerIndex, length2);
            input.SetReaderIndex(readerIndex + length2);
            return frame;
        }

        protected virtual long GetUnadjustedFrameLength(IByteBuffer buffer, int offset, int length, ByteOrder order)
        {
            long frameLength;
            switch (length)
            {
                case 1:
                    frameLength = buffer.GetByte(offset);
                    break;
                case 2:
                    frameLength = order == ByteOrder.BigEndian
                        ? buffer.GetUnsignedShort(offset)
                        : buffer.GetUnsignedShortLE(offset);
                    break;
                case 3:
                    frameLength = order == ByteOrder.BigEndian
                        ? buffer.GetUnsignedMedium(offset)
                        : buffer.GetUnsignedMediumLE(offset);
                    break;
                case 4:
                    frameLength = order == ByteOrder.BigEndian ? buffer.GetInt(offset) : buffer.GetIntLE(offset);
                    break;
                case 8:
                    frameLength = order == ByteOrder.BigEndian ? buffer.GetLong(offset) : buffer.GetLongLE(offset);
                    break;
                default:
                    throw new DecoderException("unsupported lengthFieldLength: " + LengthFieldLength +
                                               " (expected: 1, 2, 3, 4, or 8)");
            }

            return frameLength;
        }

        protected virtual IByteBuffer ExtractFrame(IChannelHandlerContext context, IByteBuffer buffer, int index,
            int length)
        {
            var byteBuffer = buffer.Slice(index, length);
            byteBuffer.Retain();
            return byteBuffer;
        }

        private void FailIfNecessary(bool firstDetectionOfTooLongFrame)
        {
            if (BytesToDiscard == 0)
            {
                long tooLongFrameLength = TooLongFrameLength;
                TooLongFrameLength = 0;
                DiscardingTooLongFrame = false;
                if (!FailFast || (FailFast && firstDetectionOfTooLongFrame))
                    Fail(tooLongFrameLength);
            }
            else
            {
                if (FailFast && firstDetectionOfTooLongFrame)
                    Fail(TooLongFrameLength);
            }
        }

        private void Fail(long frameLength)
        {
            if (frameLength > 0L)
                throw new TooLongFrameException("Adjusted frame length exceeds " + MaxFrameLength + ": " + frameLength +
                                                " - discarded");
            throw new TooLongFrameException("Adjusted frame length exceeds " + MaxFrameLength + " - discarding");
        }
    }
}
