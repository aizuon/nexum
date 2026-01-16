using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DotNetty.Buffers;

namespace Nexum.Core
{
    internal class UdpPacketFragBoard
    {
        private uint _currentPacketId;

        public UdpPacketFragBoard()
        {
            _currentPacketId = (uint)Random.Shared.Next();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerable<UdpMessage> FragmentPacket(byte[] payload, int payloadLength, uint srcHostId,
            uint destHostId)
        {
            ushort filterTag = FilterTag.Create(srcHostId, destHostId);
            return FragmentPacket(payload, payloadLength, filterTag);
        }

        public IEnumerable<UdpMessage> FragmentPacket(byte[] payload, int payloadLength, ushort filterTag)
        {
            if (payloadLength <= 0)
                yield break;

            if (payloadLength <= FragmentConfig.MtuLength)
            {
                yield return new UdpMessage
                {
                    SplitterFlag = Constants.UdpFullPacketSplitter,
                    FilterTag = filterTag,
                    PacketLength = payloadLength,
                    PacketId = 0,
                    FragmentId = 0,
                    Content = Unpooled.WrappedBuffer(payload, 0, payloadLength)
                };
                yield break;
            }

            uint packetId = Interlocked.Increment(ref _currentPacketId);
            int offset = 0;
            uint fragmentId = 0;

            while (offset < payloadLength)
            {
                int fragmentPayloadSize = Math.Min(FragmentConfig.MtuLength, payloadLength - offset);

                yield return new UdpMessage
                {
                    SplitterFlag = Constants.UdpFragmentSplitter,
                    FilterTag = filterTag,
                    PacketLength = payloadLength,
                    PacketId = packetId,
                    FragmentId = fragmentId,
                    Content = Unpooled.WrappedBuffer(payload, offset, fragmentPayloadSize)
                };

                offset += fragmentPayloadSize;
                fragmentId++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetFragmentCount(int payloadLength)
        {
            if (payloadLength <= 0)
                return 0;

            if (payloadLength <= FragmentConfig.MtuLength)
                return 1;

            return (payloadLength - 1) / FragmentConfig.MtuLength + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RequiresFragmentation(int payloadLength)
        {
            return payloadLength > FragmentConfig.MtuLength;
        }
    }
}
