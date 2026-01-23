using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Nexum.Core
{
    internal sealed class UdpPacketDefragBoard
    {
        private readonly ConcurrentDictionary<IPEndPoint, Dictionary<uint, DefraggingPacket>>
            _defraggingPackets = new ConcurrentDictionary<IPEndPoint, Dictionary<uint, DefraggingPacket>>();

        private readonly List<uint> _packetIdsToRemoveCache = new List<uint>();

        private readonly List<IPEndPoint> _sendersToRemoveCache = new List<IPEndPoint>();

        private int _inferredMtu = FragmentConfig.MtuLength;

        internal uint LocalHostId { get; set; } = (uint)HostId.None;

        internal uint MaxMessageLength { get; set; } = NetConfig.MessageMaxLength;

        internal int InferredMtu => _inferredMtu;

        internal int PendingPacketCount
        {
            get
            {
                int count = 0;
                foreach (var kvp in _defraggingPackets)
                    lock (kvp.Value)
                    {
                        count += kvp.Value.Count;
                    }

                return count;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal AssembledPacketError PushFragment(
            UdpMessage message,
            uint srcHostId,
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
                    SrcHostId = srcHostId
                };
                return AssembledPacketError.Ok;
            }

            uint fragmentId = message.FragmentId;
            var endpoint = message.EndPoint;
            var packetsForSender = _defraggingPackets.GetOrAdd(endpoint, _ => new Dictionary<uint, DefraggingPacket>());

            DefraggingPacket defraggingPacket;
            lock (packetsForSender)
            {
                uint packetId = message.PacketId;
                if (!packetsForSender.TryGetValue(packetId, out defraggingPacket))
                {
                    if (fragmentId == 0)
                    {
                        int mtuLength = fragmentLength;

                        if (mtuLength < FragmentConfig.MinMtuLength || mtuLength > FragmentConfig.MaxMtuLength)
                        {
                            error = $"Invalid MTU inferred from fragment 0: {mtuLength}";
                            return AssembledPacketError.Error;
                        }

                        int fragmentCount = UdpPacketFragBoard.GetFragmentCount(packetLength, mtuLength);

                        defraggingPacket = new DefraggingPacket
                        {
                            AssembledData = GC.AllocateUninitializedArray<byte>(packetLength),
                            FragmentReceivedFlags = new bool[fragmentCount],
                            FragmentsReceivedCount = 0,
                            TotalFragmentCount = fragmentCount,
                            CreatedTime = currentTime,
                            InferredMtu = mtuLength,
                            MtuConfirmed = true
                        };
                        packetsForSender.Add(packetId, defraggingPacket);

                        UpdateInferredMtu(mtuLength);
                    }
                    else
                    {
                        defraggingPacket = new DefraggingPacket
                        {
                            AssembledData = GC.AllocateUninitializedArray<byte>(packetLength),
                            BufferedFragments = new Dictionary<uint, BufferedFragment>(),
                            CreatedTime = currentTime,
                            MtuConfirmed = false
                        };
                        packetsForSender.Add(packetId, defraggingPacket);
                    }
                }
                else if (defraggingPacket.AssembledData.Length != packetLength)
                {
                    packetsForSender.Remove(packetId);
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
                            packetsForSender.Remove(packetId);
                            error = $"Invalid MTU inferred from fragment 0: {mtuLength}";
                            return AssembledPacketError.Error;
                        }

                        int fragmentCount = UdpPacketFragBoard.GetFragmentCount(packetLength, mtuLength);

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
                    packetsForSender.Remove(packetId);

                    assembledPacket = new AssembledPacket
                    {
                        Packet = defraggingPacket,
                        SenderEndPoint = endpoint,
                        SrcHostId = srcHostId
                    };
                    return AssembledPacketError.Ok;
                }
            }

            return AssembledPacketError.Assembling;
        }

        internal void PruneStalePackets(double currentTime, double timeout = FragmentConfig.AssembleTimeout)
        {
            _sendersToRemoveCache.Clear();

            foreach (var kvp in _defraggingPackets)
            {
                var packetsForSender = kvp.Value;
                _packetIdsToRemoveCache.Clear();

                lock (packetsForSender)
                {
                    foreach (var packet in packetsForSender)
                        if (currentTime - packet.Value.CreatedTime > timeout)
                            _packetIdsToRemoveCache.Add(packet.Key);

                    foreach (uint packetId in _packetIdsToRemoveCache)
                        packetsForSender.Remove(packetId);

                    if (packetsForSender.Count == 0)
                        _sendersToRemoveCache.Add(kvp.Key);
                }
            }

            foreach (var sender in _sendersToRemoveCache)
                _defraggingPackets.TryRemove(sender, out _);
        }

        internal void Clear()
        {
            _defraggingPackets.Clear();
            _inferredMtu = FragmentConfig.MtuLength;
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
    }

    internal readonly struct BufferedFragment
    {
        internal readonly byte[] Data;

        internal BufferedFragment(byte[] data)
        {
            Data = data;
        }
    }
}
