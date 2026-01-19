using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DotNetty.Buffers;

namespace Nexum.Core
{
    internal sealed class UdpPacketFragBoard
    {
        private uint _currentPacketId;

        public UdpPacketFragBoard()
        {
            _currentPacketId = (uint)Random.Shared.Next();
        }

        public MtuDiscovery MtuDiscovery { get; set; }

        public UdpPacketDefragBoard DefragBoard { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetEffectiveMtu()
        {
            if (MtuDiscovery != null)
                return MtuDiscovery.ConfirmedMtu;

            if (DefragBoard != null)
                return DefragBoard.InferredMtu;

            return FragmentConfig.MtuLength;
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

            int mtuLength = GetEffectiveMtu();

            if (payloadLength <= mtuLength)
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
                int fragmentPayloadSize = Math.Min(mtuLength, payloadLength - offset);

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
            return GetFragmentCount(payloadLength, FragmentConfig.MtuLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetFragmentCount(int payloadLength, int mtuLength)
        {
            if (payloadLength <= 0)
                return 0;

            if (payloadLength <= mtuLength)
                return 1;

            return (payloadLength - 1) / mtuLength + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RequiresFragmentation(int payloadLength)
        {
            return payloadLength > FragmentConfig.MtuLength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RequiresFragmentation(int payloadLength, int mtuLength)
        {
            return payloadLength > mtuLength;
        }
    }
}
