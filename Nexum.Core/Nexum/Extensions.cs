using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using DotNetty.Buffers;

namespace Nexum.Core
{
    public static class NexumBinaryReaderExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadScalar(this BinaryReader @this)
        {
            byte prefix = @this.ReadByte();
            switch (prefix)
            {
                case 1:
                    return @this.ReadByte();

                case 2:
                    return @this.ReadInt16();

                case 4:
                    return @this.ReadInt32();

                default:
                    throw new Exception($"Invalid prefix {prefix}");
            }
        }

        public static byte[] ReadStruct(this BinaryReader @this)
        {
            int size = @this.ReadScalar();
            return @this.ReadBytes(size);
        }

        public static string ReadNexumString(this BinaryReader @this)
        {
            byte stringType = @this.ReadByte();
            int size = @this.ReadScalar();
            if (size <= 0)
                return string.Empty;

            switch (stringType)
            {
                case 1:
                    return Constants.Encoding.GetString(@this.ReadBytes(size));

                case 2:
                    return Encoding.UTF8.GetString(@this.ReadBytes(size * 2));

                default:
                    throw new Exception("Unknown StringType: " + stringType);
            }
        }
    }

    internal static class NexumBinaryWriterExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteScalar(this BinaryWriter @this, int value)
        {
            byte prefix = 4;
            if (value < 128)
                prefix = 1;
            else if (value < 32768)
                prefix = 2;

            switch (prefix)
            {
                case 1:
                    @this.Write(prefix);
                    @this.Write((byte)value);
                    break;

                case 2:
                    @this.Write(prefix);
                    @this.Write((short)value);
                    break;

                case 4:
                    @this.Write(prefix);
                    @this.Write(value);
                    break;

                default:
                    throw new Exception("Invalid prefix");
            }
        }

        public static void WriteStruct(this BinaryWriter @this, byte[] data)
        {
            @this.WriteScalar(data.Length);
            @this.Write(data);
        }

        public static void WriteNexumString(this BinaryWriter @this, string value, bool unicode = false)
        {
            @this.Write((byte)(unicode ? 2 : 1));

            int size = value.Length;
            @this.WriteScalar(size);
            if (size <= 0)
                return;
            var encoding = unicode ? Encoding.UTF8 : Constants.Encoding;
            byte[] bytes = encoding.GetBytes(value);
            @this.Write(bytes);
        }
    }

    public static class NexumByteArrayExtensions
    {
        public static byte[] CompressZLib(this byte[] @this)
        {
            using (var ms = new MemoryStream())
            using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal))
            {
                zlib.Write(@this, 0, @this.Length);
                zlib.Close();
                return ms.ToArray();
            }
        }

        public static byte[] DecompressZLib(this byte[] @this)
        {
            using (var inputStream = new MemoryStream(@this))
            using (var zlib = new ZLibStream(inputStream, CompressionMode.Decompress))
            using (var outputStream = new MemoryStream())
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
                try
                {
                    int bytesRead;
                    while ((bytesRead = zlib.Read(buffer, 0, buffer.Length)) > 0)
                        outputStream.Write(buffer, 0, bytesRead);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                return outputStream.ToArray();
            }
        }
    }

    internal static class NexumIByteBufferExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetPossibleScalarlength(this IByteBuffer @this)
        {
            byte prefix = 0;
            int length = @this.ReadableBytes;

            if (length < sbyte.MaxValue)
                prefix = 1;
            else if (length < short.MaxValue)
                prefix = 2;
            else if (length < int.MaxValue)
                prefix = 4;

            return prefix;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadScalar(this IByteBuffer @this)
        {
            byte prefix = @this.ReadByte();
            switch (prefix)
            {
                case 1:
                    return @this.ReadByte();

                case 2:
                    return @this.ReadShortLE();

                case 4:
                    return @this.ReadIntLE();

                case 8:
                    return (int)@this.ReadLongLE();

                default:
                    throw new Exception($"Invalid prefix {prefix}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IByteBuffer ReadStruct(this IByteBuffer @this)
        {
            int length = @this.ReadScalar();
            return @this.ReadSlice(length);
        }

        public static string ReadNexumString(this IByteBuffer @this)
        {
            byte stringType = @this.ReadByte();
            int size = @this.ReadScalar();
            if (size <= 0)
                return string.Empty;

            string str;
            switch (stringType)
            {
                case 1:
                    str = @this.ToString(@this.ReaderIndex, size, Constants.Encoding);
                    @this.SkipBytes(size);
                    break;

                case 2:
                    str = @this.ToString(@this.ReaderIndex, size * 2, Encoding.UTF8);
                    @this.SkipBytes(size * 2);
                    break;

                default:
                    throw new Exception("Unknown StringType: " + stringType);
            }

            return str;
        }

        public static IByteBuffer WriteScalar(this IByteBuffer @this, long value)
        {
            byte prefix = 0;

            if (value < sbyte.MaxValue)
                prefix = 1;
            else if (value < short.MaxValue)
                prefix = 2;
            else if (value < int.MaxValue)
                prefix = 4;
            else if (value < long.MaxValue)
                prefix = 8;

            switch (prefix)
            {
                case 1:
                    @this.WriteByte(prefix);
                    @this.WriteByte((byte)value);
                    break;

                case 2:
                    @this.WriteByte(prefix);
                    @this.WriteShortLE((short)value);
                    break;

                case 4:
                    @this.WriteByte(prefix);
                    @this.WriteIntLE((int)value);
                    break;

                case 8:
                    @this.WriteByte(prefix);
                    @this.WriteLongLE(value);
                    break;

                default:
                    throw new Exception("Invalid prefix");
            }

            return @this;
        }

        public static IByteBuffer WriteStruct(this IByteBuffer @this, IByteBuffer data)
        {
            @this.WriteScalar(data.ReadableBytes)
                .WriteBytes(data);
            return @this;
        }

        public static IByteBuffer WriteStruct(this IByteBuffer @this, IByteBuffer data, int length)
        {
            @this.WriteScalar(length)
                .WriteBytes(data, length);
            return @this;
        }

        public static IByteBuffer WriteStruct(this IByteBuffer @this, IByteBuffer data, int offset, int length)
        {
            @this.WriteScalar(length)
                .WriteBytes(data, offset, length);
            return @this;
        }

        public static IByteBuffer WriteStruct(this IByteBuffer @this, byte[] data)
        {
            @this.WriteScalar(data.Length)
                .WriteBytes(data);
            return @this;
        }

        public static IByteBuffer WriteStruct(this IByteBuffer @this, byte[] data, int length)
        {
            @this.WriteScalar(length)
                .WriteBytes(data, 0, length);
            return @this;
        }

        public static IByteBuffer WriteStruct(this IByteBuffer @this, byte[] data, int offset, int length)
        {
            @this.WriteScalar(length)
                .WriteBytes(data, offset, length);
            return @this;
        }

        public static IByteBuffer WriteNexumString(this IByteBuffer @this, string value, bool unicode = false)
        {
            @this.WriteByte((byte)(unicode ? 2 : 1));

            int size = value.Length;
            @this.WriteScalar(size);
            if (size <= 0)
                return @this;

            var encoding = unicode ? Encoding.UTF8 : Constants.Encoding;
            byte[] bytes = encoding.GetBytes(value);
            @this.WriteBytes(bytes);
            return @this;
        }
    }
}
