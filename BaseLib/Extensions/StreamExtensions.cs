using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace BaseLib.Extensions
{
    public static class StreamExtensions
    {
        private const int DefaultBufferSize = 81920;

        public static BinaryReader ToBinaryReader(this Stream stream, Encoding encoding, bool leaveOpen)
        {
            return new BinaryReader(stream, encoding, leaveOpen);
        }

        public static BinaryReader ToBinaryReader(this Stream stream, bool leaveOpen)
        {
            return new BinaryReader(stream, Encoding.UTF8, leaveOpen);
        }

        public static BinaryWriter ToBinaryWriter(this Stream stream, Encoding encoding, bool leaveOpen)
        {
            return new BinaryWriter(stream, encoding, leaveOpen);
        }

        public static BinaryWriter ToBinaryWriter(this Stream stream, bool leaveOpen)
        {
            return new BinaryWriter(stream, Encoding.UTF8, leaveOpen);
        }

        public static byte[] ReadToEnd(this Stream stream)
        {
            if (stream is MemoryStream sourceMs && sourceMs.TryGetBuffer(out var buffer))
            {
                int remaining = (int)(sourceMs.Length - sourceMs.Position);
                byte[] result = GC.AllocateUninitializedArray<byte>(remaining);
                Buffer.BlockCopy(buffer.Array!, buffer.Offset + (int)sourceMs.Position, result, 0, remaining);
                sourceMs.Position = sourceMs.Length;
                return result;
            }

            if (stream.CanSeek)
            {
                int remaining = (int)(stream.Length - stream.Position);
                byte[] result = GC.AllocateUninitializedArray<byte>(remaining);
                int totalRead = 0;
                while (totalRead < remaining)
                {
                    int read = stream.Read(result, totalRead, remaining - totalRead);
                    if (read == 0)
                        break;
                    totalRead += read;
                }

                return result.AsSpan(0, totalRead).ToArray();
            }

            using var memoryStream = new MemoryStream();
            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
            try
            {
                int bytesRead;
                while ((bytesRead = stream.Read(rentedBuffer, 0, rentedBuffer.Length)) > 0)
                    memoryStream.Write(rentedBuffer, 0, bytesRead);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }

            return memoryStream.ToArray();
        }

        public static async Task<byte[]> ReadToEndAsync(this Stream stream)
        {
            if (stream is MemoryStream sourceMs && sourceMs.TryGetBuffer(out var buffer))
            {
                int remaining = (int)(sourceMs.Length - sourceMs.Position);
                byte[] result = GC.AllocateUninitializedArray<byte>(remaining);
                Buffer.BlockCopy(buffer.Array!, buffer.Offset + (int)sourceMs.Position, result, 0, remaining);
                sourceMs.Position = sourceMs.Length;
                return result;
            }

            if (stream.CanSeek)
            {
                int remaining = (int)(stream.Length - stream.Position);
                byte[] result = GC.AllocateUninitializedArray<byte>(remaining);
                int totalRead = 0;
                while (totalRead < remaining)
                {
                    int read = await stream.ReadAsync(result.AsMemory(totalRead, remaining - totalRead));
                    if (read == 0)
                        break;
                    totalRead += read;
                }

                return result.AsSpan(0, totalRead).ToArray();
            }

            using var memoryStream = new MemoryStream();
            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
            try
            {
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(rentedBuffer.AsMemory())) > 0)
                    memoryStream.Write(rentedBuffer, 0, bytesRead);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }

            return memoryStream.ToArray();
        }

        public static bool IsEOF(this Stream @this)
        {
            return @this.Position >= @this.Length;
        }
    }
}
