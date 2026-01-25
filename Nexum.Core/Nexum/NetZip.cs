using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using Serilog;

namespace Nexum.Core
{
    internal static class NetZip
    {
        private static readonly ILogger Logger =
            Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, nameof(NetZip));

        internal static NetMessage CompressPacket(NetMessage message)
        {
            var compressedMessage = new NetMessage
            {
                EncryptMode = message.EncryptMode
            };
            using (var mem = new MemoryStream())
            using (var zlib = new ZLibStream(mem, CompressionLevel.Optimal))
            {
                zlib.Write(message.GetBufferSpan());
                zlib.Close();

                compressedMessage.Write(MessageType.Compressed);

                ArraySegment<byte> buffer;
                if (!mem.TryGetBuffer(out buffer))
                {
                    byte[] array = mem.ToArray();
                    buffer = new ArraySegment<byte>(array);
                }

                compressedMessage.WriteScalar(buffer.Count);
                compressedMessage.WriteScalar(message.Length);
                compressedMessage.Write(buffer.AsSpan());
            }

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
