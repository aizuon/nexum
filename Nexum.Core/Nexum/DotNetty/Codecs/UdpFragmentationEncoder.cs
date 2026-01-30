using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;
using Nexum.Core.Configuration;
using Nexum.Core.Mtu;
using Nexum.Core.Udp;

namespace Nexum.Core.DotNetty.Codecs
{
    internal sealed class UdpFragmentationEncoder : MessageToMessageEncoder<OutboundUdpPacket>
    {
        private uint _currentPacketId;

        internal UdpFragmentationEncoder()
        {
            _currentPacketId = (uint)Random.Shared.Next();
        }

        internal MtuDiscovery MtuDiscovery { get; set; }

        internal UdpDefragmentationDecoder DefragDecoder { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetEffectiveMtu()
        {
            if (MtuDiscovery != null)
                return MtuDiscovery.ConfirmedMtu;

            if (DefragDecoder != null)
                return DefragDecoder.InferredMtu;

            return FragmentConfig.MtuLength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetEffectiveMtu(OutboundUdpPacket packet)
        {
            if (packet.Mtu > 0)
                return packet.Mtu;

            return GetEffectiveMtu();
        }

        protected override void Encode(IChannelHandlerContext context, OutboundUdpPacket message, List<object> output)
        {
            var content = message.Content;
            int packetLength = content.ReadableBytes;

            if (packetLength <= 0)
            {
                content.Release();
                return;
            }

            int mtuLength = GetEffectiveMtu(message);
            ushort filterTag = message.FilterTag;
            var endPoint = message.EndPoint;

            if (packetLength <= mtuLength)
            {
                output.Add(new UdpMessage
                {
                    SplitterFlag = Constants.UdpFullPacketSplitter,
                    FilterTag = filterTag,
                    PacketLength = packetLength,
                    PacketId = 0,
                    FragmentId = 0,
                    Content = content.RetainedSlice(content.ReaderIndex, packetLength),
                    EndPoint = endPoint
                });
                content.Release();
                return;
            }

            int fragmentCount = GetFragmentCount(packetLength, mtuLength);
            if (output.Capacity < fragmentCount)
                output.Capacity = fragmentCount;

            uint packetId = Interlocked.Increment(ref _currentPacketId);
            int offset = 0;
            uint fragmentId = 0;

            while (offset < packetLength)
            {
                int fragmentPayloadSize = Math.Min(mtuLength, packetLength - offset);

                output.Add(new UdpMessage
                {
                    SplitterFlag = Constants.UdpFragmentSplitter,
                    FilterTag = filterTag,
                    PacketLength = packetLength,
                    PacketId = packetId,
                    FragmentId = fragmentId,
                    Content = content.RetainedSlice(content.ReaderIndex + offset, fragmentPayloadSize),
                    EndPoint = endPoint
                });

                offset += fragmentPayloadSize;
                fragmentId++;
            }

            content.Release();
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
