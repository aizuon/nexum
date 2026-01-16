using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;

namespace Nexum.Core
{
    internal class UdpPacketDefragBoard
    {
        private readonly ConcurrentDictionary<IPEndPoint, Dictionary<uint, DefraggingPacket>>
            _defraggingPackets = new ConcurrentDictionary<IPEndPoint, Dictionary<uint, DefraggingPacket>>();

        private readonly List<uint> _packetIdsToRemoveCache = new List<uint>();

        private readonly List<IPEndPoint> _sendersToRemoveCache = new List<IPEndPoint>();

        public uint LocalHostId { get; set; } = (uint)HostId.None;

        public uint MaxMessageLength { get; set; } = NetConfig.MessageMaxLength;

        public int PendingPacketCount
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
        public AssembledPacketError PushFragment(
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

            int maxFragmentId = UdpPacketFragBoard.GetFragmentCount(packetLength) - 1;
            uint fragmentId = message.FragmentId;
            if (fragmentId > maxFragmentId)
            {
                error = $"Fragment ID {fragmentId} exceeds max {maxFragmentId}";
                return AssembledPacketError.Error;
            }

            int destOffset = FragmentConfig.MtuLength * (int)fragmentId;
            int expectedFragmentSize = Math.Min(FragmentConfig.MtuLength, packetLength - destOffset);

            if (expectedFragmentSize != fragmentLength)
            {
                error = $"Fragment size mismatch: expected {expectedFragmentSize}, got {fragmentLength}";
                return AssembledPacketError.Error;
            }

            var endpoint = message.EndPoint;
            if (!_defraggingPackets.TryGetValue(endpoint, out var packetsForSender))
            {
                packetsForSender = new Dictionary<uint, DefraggingPacket>();
                _defraggingPackets.TryAdd(endpoint, packetsForSender);
                _defraggingPackets.TryGetValue(endpoint, out packetsForSender);
            }

            DefraggingPacket defraggingPacket;
            lock (packetsForSender)
            {
                uint packetId = message.PacketId;
                if (!packetsForSender.TryGetValue(packetId, out defraggingPacket))
                {
                    int fragmentCount = UdpPacketFragBoard.GetFragmentCount(packetLength);
                    defraggingPacket = new DefraggingPacket
                    {
                        AssembledData = new byte[packetLength],
                        FragmentReceivedFlags = new bool[fragmentCount],
                        FragmentsReceivedCount = 0,
                        TotalFragmentCount = fragmentCount,
                        CreatedTime = currentTime
                    };
                    packetsForSender.Add(packetId, defraggingPacket);
                }
                else if (defraggingPacket.AssembledData.Length != packetLength)
                {
                    packetsForSender.Remove(packetId);
                    error = "Packet length mismatch between fragments";
                    return AssembledPacketError.Error;
                }

                if (fragmentId >= defraggingPacket.FragmentReceivedFlags.Length)
                {
                    error = "Fragment ID out of bounds";
                    return AssembledPacketError.Error;
                }

                if (destOffset + fragmentLength > defraggingPacket.AssembledData.Length)
                {
                    error = "Fragment payload would overflow buffer";
                    return AssembledPacketError.Error;
                }

                if (!defraggingPacket.FragmentReceivedFlags[fragmentId])
                {
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

        public void PruneStalePackets(double currentTime, double timeout = FragmentConfig.AssembleTimeout)
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

        public void Clear()
        {
            _defraggingPackets.Clear();
        }
    }
}
