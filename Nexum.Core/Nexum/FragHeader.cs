using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Nexum.Core
{
    internal struct FragHeader
    {
        internal ushort SplitterFlag;

        internal ushort FilterTag;

        internal uint PacketLength;

        internal uint PacketId;

        internal uint FragmentId;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly void WriteTo(ByteArray byteArray)
        {
            byteArray.Write(SplitterFlag);
            byteArray.Write(FilterTag);
            byteArray.Write(PacketLength);
            byteArray.Write(PacketId);
            byteArray.Write(FragmentId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryReadFrom(ByteArray byteArray, out FragHeader header)
        {
            header = default(FragHeader);

            if (byteArray.Length - byteArray.ReadOffset < FragmentConfig.HeaderSize)
                return false;

            if (!byteArray.Read(ref header.SplitterFlag))
                return false;
            if (!byteArray.Read(ref header.FilterTag))
                return false;
            if (!byteArray.Read(ref header.PacketLength))
                return false;
            if (!byteArray.Read(ref header.PacketId))
                return false;
            if (!byteArray.Read(ref header.FragmentId))
                return false;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly void WriteTo(byte[] buffer, int offset)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), SplitterFlag);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset + 2), FilterTag);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 4), PacketLength);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 8), PacketId);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 12), FragmentId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryReadFrom(byte[] buffer, int offset, int length, out FragHeader header)
        {
            header = default(FragHeader);

            if (length - offset < FragmentConfig.HeaderSize)
                return false;

            var span = buffer.AsSpan(offset);
            header.SplitterFlag = BinaryPrimitives.ReadUInt16LittleEndian(span);
            header.FilterTag = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2));
            header.PacketLength = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4));
            header.PacketId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8));
            header.FragmentId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12));
            return true;
        }
    }
}
