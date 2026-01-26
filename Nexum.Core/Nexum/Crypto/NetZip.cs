using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using Nexum.Core.Message.X2X;
using Nexum.Core.Serialization;
using Serilog;
using Serilog.Core;

namespace Nexum.Core.Crypto
{
    internal static class NetZip
    {
        private static readonly ILogger Logger =
            Log.ForContext(Constants.SourceContextPropertyName, nameof(NetZip));

        internal static NetMessage CompressPacket(NetMessage message)
        {
            using var mem = new MemoryStream();
            using (var zlib = new ZLibStream(mem, CompressionLevel.Optimal, true))
            {
                zlib.Write(message.GetBufferSpan());
            }

            ArraySegment<byte> buffer;
            if (!mem.TryGetBuffer(out buffer))
            {
                byte[] array = mem.ToArray();
                buffer = new ArraySegment<byte>(array);
            }

            var compressedMessage = new CompressedMessage
            {
                CompressedSize = buffer.Count,
                OriginalSize = message.Length,
                CompressedData = new ByteArray(buffer.AsSpan().ToArray(), true)
            }.Serialize();
            compressedMessage.EncryptMode = message.EncryptMode;

            return compressedMessage;
        }

        internal static byte[] CompressData(byte[] data)
        {
            using (var mem = new MemoryStream())
            using (var zlib = new ZLibStream(mem, CompressionLevel.Optimal))
            {
                zlib.Write(data, 0, data.Length);
                zlib.Close();
                return mem.ToArray();
            }
        }

        internal static NetMessage DecompressPacket(NetMessage message)
        {
            var decompressedMessage = new NetMessage();
            try
            {
                using (var inputStream = new MemoryStream(message.GetBufferUnsafe(), 0, message.Length))
                using (var zlib = new ZLibStream(inputStream, CompressionMode.Decompress))
                using (var outputStream = new MemoryStream())
                {
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
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

                    if (outputStream.TryGetBuffer(out var outBuffer))
                        decompressedMessage.Write(outBuffer.AsSpan());
                    else
                        decompressedMessage.Write(outputStream.ToArray());
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to decompress packet of length {Length}", message.Length);
            }

            return decompressedMessage;
        }
    }
}
