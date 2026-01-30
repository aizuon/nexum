using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using BaseLib.Extensions;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;
using Nexum.Core.Configuration;
using Nexum.Core.Fragmentation;
using Nexum.Core.Routing;
using Nexum.Core.Udp;
using Nexum.Core.Utilities;
using Serilog;
using SerilogConstants = Serilog.Core.Constants;

namespace Nexum.Core.DotNetty.Codecs
{
    internal sealed class UdpDefragmentationDecoder : MessageToMessageDecoder<UdpMessage>
    {
        private static readonly ILogger Logger =
            Log.ForContext(SerilogConstants.SourceContextPropertyName, nameof(UdpDefragmentationDecoder));

        private readonly ConcurrentDictionary<IPEndPoint, ConcurrentDictionary<uint, DefraggingPacket>>
            _defraggingPackets = new ConcurrentDictionary<IPEndPoint, ConcurrentDictionary<uint, DefraggingPacket>>();

        private readonly ITimeSource _owner;

        internal readonly uint MaxMessageLength;

        private int _inferredMtu = FragmentConfig.MtuLength;
        private EventLoopScheduler _pruneScheduler;

        internal UdpDefragmentationDecoder(ITimeSource owner, uint maxMessageLength)
        {
            _owner = owner;
            MaxMessageLength = maxMessageLength;
        }

        internal uint LocalHostId { get; set; } = (uint)HostId.None;

        internal int InferredMtu => _inferredMtu;

        internal int PendingPacketCount
        {
            get
            {
                int count = 0;
                foreach (var kvp in _defraggingPackets)
                    count += kvp.Value.Count;
                return count;
            }
        }

        public override void ChannelActive(IChannelHandlerContext context)
        {
            _pruneScheduler = EventLoopScheduler.StartIfNeeded(
                _pruneScheduler,
                TimeSpan.FromSeconds(FragmentConfig.AssembleTimeout / 2),
                () => PruneStalePackets(_owner.GetAbsoluteTime()),
                context.Channel.EventLoop);
            base.ChannelActive(context);
        }

        public override void ChannelInactive(IChannelHandlerContext context)
        {
            _pruneScheduler?.Stop();
            _pruneScheduler = null;
            base.ChannelInactive(context);
        }

        protected override void Decode(IChannelHandlerContext context, UdpMessage message, List<object> output)
        {
            double currentTime = _owner.GetAbsoluteTime();

            var result = PushFragment(
                message,
                currentTime,
                out var assembledPacket,
                out string error);

            switch (result)
            {
                case AssembledPacketError.Ok:
                    output.Add(assembledPacket);
                    break;

                case AssembledPacketError.Assembling:
                    break;

                case AssembledPacketError.Error:
                    Logger.Warning("UDP defragmentation error from {Endpoint}: {Error}",
                        message.EndPoint.ToIPv4String(), error);
                    break;
            }

            message.Content.Release();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AssembledPacketError PushFragment(
            UdpMessage message,
            double currentTime,
            out AssembledPacket assembledPacket,
            out string error)
        {
            assembledPacket = null;
            error = null;

            ushort splitterFlag = message.SplitterFlag;
            if (splitterFlag != Constants.UdpFragmentSplitter &&
                splitterFlag != Constants.UdpFullPacketSplitter)
            {
                error = $"Invalid splitter flag: 0x{splitterFlag:X4}";
                return AssembledPacketError.Error;
            }

            uint srcHostId = (uint)HostId.None;

            if (FilterTag.ShouldFilter(message.FilterTag, srcHostId, LocalHostId))
                return AssembledPacketError.Assembling;

            int packetLength = message.PacketLength;
            if (packetLength == 0 || packetLength > MaxMessageLength)
            {
                error = $"Invalid packet length: {packetLength}";
                return AssembledPacketError.Error;
            }

            var content = message.Content;
            int fragmentLength = content.ReadableBytes;

            if (splitterFlag == Constants.UdpFullPacketSplitter)
            {
                if (fragmentLength != packetLength)
                {
                    error = $"Full packet size mismatch: header says {packetLength}, got {fragmentLength}";
                    return AssembledPacketError.Error;
                }

                byte[] assembledData = GC.AllocateUninitializedArray<byte>(fragmentLength);
                content.GetBytes(content.ReaderIndex, assembledData, 0, fragmentLength);

                var packet = new DefraggingPacket
                {
                    AssembledData = assembledData,
                    FragmentsReceivedCount = 1,
                    TotalFragmentCount = 1,
                    CreatedTime = currentTime
                };

                assembledPacket = new AssembledPacket
                {
                    Packet = packet,
                    SenderEndPoint = message.EndPoint,
                    SrcHostId = srcHostId,
                    FilterTag = message.FilterTag
                };
                return AssembledPacketError.Ok;
            }

            uint fragmentId = message.FragmentId;
            uint packetId = message.PacketId;
            var endpoint = message.EndPoint;
            var packetsForSender =
                _defraggingPackets.GetOrAdd(endpoint, _ => new ConcurrentDictionary<uint, DefraggingPacket>());

            var defraggingPacket = packetsForSender.GetOrAdd(packetId, _ => new DefraggingPacket
            {
                AssembledData = GC.AllocateUninitializedArray<byte>(packetLength),
                CreatedTime = currentTime,
                MtuConfirmed = false
            });

            bool lockTaken = false;
            try
            {
                defraggingPacket.Lock.Enter(ref lockTaken);

                if (defraggingPacket.AssembledData.Length != packetLength)
                {
                    packetsForSender.TryRemove(packetId, out _);
                    error = "Packet length mismatch between fragments";
                    return AssembledPacketError.Error;
                }

                if (!defraggingPacket.MtuConfirmed)
                {
                    if (fragmentId == 0)
                    {
                        int mtuLength = fragmentLength;

                        if (mtuLength < FragmentConfig.MinMtuLength || mtuLength > FragmentConfig.MaxMtuLength)
                        {
                            packetsForSender.TryRemove(packetId, out _);
                            error = $"Invalid MTU inferred from fragment 0: {mtuLength}";
                            return AssembledPacketError.Error;
                        }

                        int fragmentCount = GetFragmentCount(packetLength, mtuLength);

                        defraggingPacket.InferredMtu = mtuLength;
                        defraggingPacket.TotalFragmentCount = fragmentCount;
                        defraggingPacket.FragmentReceivedFlags = new bool[fragmentCount];
                        defraggingPacket.FragmentsReceivedCount = 0;
                        defraggingPacket.MtuConfirmed = true;

                        UpdateInferredMtu(mtuLength);

                        defraggingPacket.FragmentReceivedFlags[0] = true;
                        defraggingPacket.FragmentsReceivedCount++;
                        content.GetBytes(content.ReaderIndex, defraggingPacket.AssembledData, 0, fragmentLength);

                        if (defraggingPacket.BufferedFragments != null)
                        {
                            foreach (var buffered in defraggingPacket.BufferedFragments)
                            {
                                uint bufferedFragId = buffered.Key;
                                var bufferedFrag = buffered.Value;

                                int maxFragmentId = fragmentCount - 1;
                                if (bufferedFragId > maxFragmentId)
                                    continue;

                                int destOffset = mtuLength * (int)bufferedFragId;
                                int expectedSize = Math.Min(mtuLength, packetLength - destOffset);

                                if (expectedSize != bufferedFrag.Data.Length)
                                    continue;

                                if (!defraggingPacket.FragmentReceivedFlags[bufferedFragId])
                                {
                                    defraggingPacket.FragmentReceivedFlags[bufferedFragId] = true;
                                    defraggingPacket.FragmentsReceivedCount++;
                                    Buffer.BlockCopy(bufferedFrag.Data, 0, defraggingPacket.AssembledData, destOffset,
                                        bufferedFrag.Data.Length);
                                }
                            }

                            defraggingPacket.BufferedFragments = null;
                        }
                    }
                    else
                    {
                        defraggingPacket.BufferedFragments ??= new Dictionary<uint, BufferedFragment>();
                        if (!defraggingPacket.BufferedFragments.TryGetValue(fragmentId, out _))
                        {
                            byte[] fragData = GC.AllocateUninitializedArray<byte>(fragmentLength);
                            content.GetBytes(content.ReaderIndex, fragData, 0, fragmentLength);
                            defraggingPacket.BufferedFragments[fragmentId] = new BufferedFragment(fragData);
                        }

                        return AssembledPacketError.Assembling;
                    }
                }
                else
                {
                    int mtuLength = defraggingPacket.InferredMtu;
                    int maxFragmentId = defraggingPacket.TotalFragmentCount - 1;

                    if (fragmentId > maxFragmentId)
                    {
                        error = $"Fragment ID {fragmentId} exceeds max {maxFragmentId} (MTU={mtuLength})";
                        return AssembledPacketError.Error;
                    }

                    int destOffset = mtuLength * (int)fragmentId;
                    int expectedFragmentSize = Math.Min(mtuLength, packetLength - destOffset);

                    if (expectedFragmentSize != fragmentLength)
                    {
                        error =
                            $"Fragment size mismatch: expected {expectedFragmentSize}, got {fragmentLength} (MTU={mtuLength})";
                        return AssembledPacketError.Error;
                    }

                    if (defraggingPacket.FragmentReceivedFlags[fragmentId])
                        return AssembledPacketError.Assembling;

                    defraggingPacket.FragmentReceivedFlags[fragmentId] = true;
                    defraggingPacket.FragmentsReceivedCount++;
                    content.GetBytes(content.ReaderIndex, defraggingPacket.AssembledData, destOffset, fragmentLength);
                }

                if (defraggingPacket.IsComplete)
                {
                    packetsForSender.TryRemove(packetId, out _);

                    assembledPacket = new AssembledPacket
                    {
                        Packet = defraggingPacket,
                        SenderEndPoint = endpoint,
                        SrcHostId = srcHostId,
                        FilterTag = message.FilterTag
                    };
                    return AssembledPacketError.Ok;
                }
            }
            finally
            {
                if (lockTaken)
                    defraggingPacket.Lock.Exit(false);
            }

            return AssembledPacketError.Assembling;
        }

        internal void PruneStalePackets(double currentTime, double timeout = FragmentConfig.AssembleTimeout)
        {
            foreach (var kvp in _defraggingPackets)
            {
                var packetsForSender = kvp.Value;
                foreach (var packet in packetsForSender)
                {
                    bool shouldRemove = false;
                    bool lockTaken = false;
                    try
                    {
                        packet.Value.Lock.Enter(ref lockTaken);
                        shouldRemove = currentTime - packet.Value.CreatedTime > timeout;
                    }
                    finally
                    {
                        if (lockTaken)
                            packet.Value.Lock.Exit(false);
                    }

                    if (shouldRemove)
                        packetsForSender.TryRemove(packet.Key, out _);
                }

                if (packetsForSender.IsEmpty)
                    _defraggingPackets.TryRemove(kvp.Key, out _);
            }
        }

        internal void Clear()
        {
            _defraggingPackets.Clear();
            Interlocked.Exchange(ref _inferredMtu, FragmentConfig.MtuLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateInferredMtu(int mtu)
        {
            int current;
            do
            {
                current = _inferredMtu;
                if (mtu <= current)
                    return;
            } while (Interlocked.CompareExchange(ref _inferredMtu, mtu, current) != current);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetFragmentCount(int payloadLength, int mtuLength)
        {
            if (payloadLength <= 0)
                return 0;

            if (payloadLength <= mtuLength)
                return 1;

            return (payloadLength - 1) / mtuLength + 1;
        }
    }
}
