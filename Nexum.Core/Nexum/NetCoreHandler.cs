using System;
using Serilog;

namespace Nexum.Core
{
    internal static class NetCoreHandler
    {
        internal static bool HandleEncrypted(
            NetMessage message,
            NetCrypt crypt,
            Action<NetMessage> readMessageCallback)
        {
            var encryptedPayload = new ByteArray();

            if (!message.ReadEnum<EncryptMode>(out var encryptMode) || !message.Read(ref encryptedPayload))
                return false;

            byte[] decryptedBuffer = crypt.Decrypt(
                encryptedPayload.GetBufferSpan(),
                encryptMode
            );

            var decryptedMessage = new NetMessage(decryptedBuffer, true);
            readMessageCallback(decryptedMessage);
            return true;
        }

        internal static bool HandleCompressed(
            NetMessage message,
            ILogger logger,
            Action<NetMessage> readMessageCallback)
        {
            long compressedSize = 0;
            long uncompressedLength = 0;

            if (!message.ReadScalar(ref compressedSize) || !message.ReadScalar(ref uncompressedLength))
            {
                logger.Error("Corrupted compressed packet!");
                return false;
            }

            byte[] buffer = GC.AllocateUninitializedArray<byte>((int)compressedSize);
            if (!message.Read(ref buffer, (int)compressedSize))
            {
                logger.Error("Corrupted compressed packet!");
                return false;
            }

            var decompressedMessage = NetZip.DecompressPacket(new NetMessage(buffer, true));
            decompressedMessage.Compress = true;
            decompressedMessage.EncryptMode = message.EncryptMode;

            readMessageCallback(decompressedMessage);
            return true;
        }
    }
}
