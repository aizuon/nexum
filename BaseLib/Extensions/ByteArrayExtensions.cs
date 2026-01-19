using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace BaseLib.Extensions
{
    public static class ByteArrayExtensions
    {
        public static string ToHexString(this byte[] buffer)
        {
            if (buffer.Length == 0)
                return string.Empty;

            return string.Create(buffer.Length * 3 - 1, buffer, static (span, bytes) =>
            {
                ReadOnlySpan<char> hexChars = "0123456789ABCDEF";
                int pos = 0;
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (i > 0)
                        span[pos++] = ' ';

                    byte b = bytes[i];
                    span[pos++] = hexChars[b >> 4];
                    span[pos++] = hexChars[b & 0xF];
                }
            });
        }

        public static BinaryReader ToBinaryReader(this byte[] @this)
        {
            return new BinaryReader(new MemoryStream(@this));
        }

        public static BinaryWriter ToBinaryWriter(this byte[] @this)
        {
            return new BinaryWriter(new MemoryStream(@this));
        }

        public static byte[] CompressGZip(this byte[] buffer)
        {
            using var memoryStream = new MemoryStream();
            using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Compress))
            {
                gZipStream.Write(buffer, 0, buffer.Length);
            }

            return memoryStream.ToArray();
        }

        public static byte[] CompressDeflate(this byte[] buffer)
        {
            using var memoryStream = new MemoryStream();
            using (var deflateStream = new DeflateStream(memoryStream, CompressionMode.Compress))
            {
                deflateStream.Write(buffer, 0, buffer.Length);
            }

            return memoryStream.ToArray();
        }

        public static byte[] DecompressGZip(this byte[] buffer)
        {
            using var stream = new MemoryStream(buffer);
            using var gzipStream = new GZipStream(stream, CompressionMode.Decompress);
            return gzipStream.ReadToEnd();
        }

        public static byte[] DecompressDeflate(this byte[] buffer)
        {
            using var stream = new MemoryStream(buffer);
            using var deflateStream = new DeflateStream(stream, CompressionMode.Decompress);
            return deflateStream.ReadToEnd();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] FastClone(this byte[] buffer)
        {
            byte[] array = GC.AllocateUninitializedArray<byte>(buffer.Length);
            Buffer.BlockCopy(buffer, 0, array, 0, buffer.Length);
            return array;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FastCopyTo(this byte[] source, byte[] destination, int destinationOffset)
        {
            Buffer.BlockCopy(source, 0, destination, destinationOffset, source.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FastCopyTo(this byte[] source, int sourceOffset, byte[] destination, int destinationOffset,
            int count)
        {
            Buffer.BlockCopy(source, sourceOffset, destination, destinationOffset, count);
        }
    }
}
