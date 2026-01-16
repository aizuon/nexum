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

        public static NetMessage CompressPacket(NetMessage message)
        {
            var netMessage = new NetMessage
            {
                EncryptMode = message.EncryptMode
            };
            using (var mem = new MemoryStream())
            using (var zlib = new ZLibStream(mem, CompressionLevel.Optimal))
            {
                zlib.Write(message.GetBuffer(), 0, message.Length);
                zlib.Close();
                byte[] array = mem.ToArray();
                netMessage.WriteEnum(MessageType.Compressed);
                netMessage.WriteScalar(array.Length);
                netMessage.WriteScalar(message.Length);
                netMessage.Write(array);
            }

            return netMessage;
        }

        public static byte[] CompressData(byte[] data)
        {
            using (var mem = new MemoryStream())
            using (var zlib = new ZLibStream(mem, CompressionLevel.Optimal))
            {
                zlib.Write(data, 0, data.Length);
                zlib.Close();
                return mem.ToArray();
            }
        }

        public static NetMessage DecompressPacket(NetMessage message)
        {
            var netMessage = new NetMessage();
            try
            {
                byte[] inputBuffer = message.GetBuffer();
                using (var inputStream = new MemoryStream(inputBuffer, 0, message.Length))
                using (var zlib = new ZLibStream(inputStream, CompressionMode.Decompress))
                using (var outputStream = new MemoryStream())
                {
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(NetConfig.MessageMaxLength * 4);
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

                    netMessage.Write(outputStream.ToArray());
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to decompress packet of length {Length}", message.Length);
            }

            return netMessage;
        }
    }
}
