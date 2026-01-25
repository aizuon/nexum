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

        internal UdpPacketFragBoard()
        {
            _currentPacketId = (uint)Random.Shared.Next();
        }

        internal MtuDiscovery MtuDiscovery { get; set; }

        internal UdpPacketDefragBoard DefragBoard { get; set; }

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
        internal IEnumerable<UdpMessage> FragmentPacket(NetMessage message, uint srcHostId,
            uint destHostId)
        {
            ushort filterTag = FilterTag.Create(srcHostId, destHostId);
            return FragmentPacket(message, filterTag);
        }

        internal IEnumerable<UdpMessage> FragmentPacket(NetMessage message, ushort filterTag)
        {
            if (message.Length <= 0)
                yield break;

            int mtuLength = GetEffectiveMtu();

            if (message.Length <= mtuLength)
            {
                yield return new UdpMessage
                {
                    SplitterFlag = Constants.UdpFullPacketSplitter,
                    FilterTag = filterTag,
                    PacketLength = message.Length,
                    PacketId = 0,
                    FragmentId = 0,
                    Content = Unpooled.WrappedBuffer(message.GetBufferUnsafe(), 0, message.Length)
                };
                yield break;
            }

            uint packetId = Interlocked.Increment(ref _currentPacketId);
            int offset = 0;
            uint fragmentId = 0;

            while (offset < message.Length)
            {
                int fragmentPayloadSize = Math.Min(mtuLength, message.Length - offset);

                yield return new UdpMessage
                {
                    SplitterFlag = Constants.UdpFragmentSplitter,
                    FilterTag = filterTag,
                    PacketLength = message.Length,
                    PacketId = packetId,
                    FragmentId = fragmentId,
                    Content = Unpooled.WrappedBuffer(message.GetBufferUnsafe(), offset, fragmentPayloadSize)
                };

                offset += fragmentPayloadSize;
                fragmentId++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetFragmentCount(int payloadLength)
        {
            return GetFragmentCount(payloadLength, FragmentConfig.MtuLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetFragmentCount(int payloadLength, int mtuLength)
        {
            if (payloadLength <= 0)
                return 0;

            if (payloadLength <= mtuLength)
                return 1;

            return (payloadLength - 1) / mtuLength + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool RequiresFragmentation(int payloadLength)
        {
            return payloadLength > FragmentConfig.MtuLength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool RequiresFragmentation(int payloadLength, int mtuLength)
        {
            return payloadLength > mtuLength;
        }
    }
}
