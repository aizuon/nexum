using System;
using System.Text;
using Nexum.Core;
using Xunit;

namespace Nexum.Tests
{
    public class NetZipTests
    {
        public NetZipTests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [Fact]
        public void CompressPacket_CompressesData()
        {
            var message = new NetMessage();
            byte[] data = new byte[1000];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 256);
            message.Write(data);
            var compressed = NetZip.CompressPacket(message);
            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);
            Assert.True(compressed.Length < message.Length);
        }

        [Fact]
        public void CompressPacket_PreservesEncryptMode()
        {
            var message = new NetMessage
            {
                EncryptMode = EncryptMode.Secure
            };
            message.Write(new byte[] { 1, 2, 3, 4, 5 });
            var compressed = NetZip.CompressPacket(message);
            Assert.Equal(message.EncryptMode, compressed.EncryptMode);
        }

        [Fact]
        public void CompressPacket_ContainsCompressedMessageType()
        {
            var message = new NetMessage();
            message.Write(new byte[] { 1, 2, 3, 4, 5 });
            var compressed = NetZip.CompressPacket(message);
            var messageType = MessageType.None;
            bool success = compressed.Read(ref messageType);
            Assert.True(success);
            Assert.Equal(MessageType.Compressed, messageType);
        }

        [Fact]
        public void CompressPacket_StoresCompressedAndOriginalSize()
        {
            var message = new NetMessage();
            byte[] data = new byte[100];
            message.Write(data);
            var compressed = NetZip.CompressPacket(message);
            var messageType = MessageType.None;
            compressed.Read(ref messageType);
            long compressedSize = 0;
            long originalSize = 0;
            bool success1 = compressed.ReadScalar(ref compressedSize);
            bool success2 = compressed.ReadScalar(ref originalSize);

            Assert.True(success1);
            Assert.True(success2);
            Assert.Equal(message.Length, originalSize);
            Assert.True(compressedSize > 0);
        }

        [Fact]
        public void DecompressPacket_RestoresOriginalData()
        {
            var original = new NetMessage();
            byte[] data = new byte[1000];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 256);
            original.Write(data);

            var compressed = NetZip.CompressPacket(original);
            var messageType = MessageType.None;
            compressed.Read(ref messageType);
            long compressedSize = 0;
            long originalSize = 0;
            compressed.ReadScalar(ref compressedSize);
            compressed.ReadScalar(ref originalSize);

            byte[] compressedData;
            compressed.ReadBytes(out compressedData, (int)compressedSize);
            var compressedMessage = new NetMessage(compressedData, (int)compressedSize);
            var decompressed = NetZip.DecompressPacket(compressedMessage);
            Assert.Equal(originalSize, decompressed.Length);
            Assert.Equal(original.Buffer, decompressed.Buffer);
        }

        [Fact]
        public void CompressData_CompressesRawData()
        {
            byte[] data = new byte[1000];
            for (int i = 0; i < data.Length; i++)
                data[i] = 0;
            byte[] compressed = NetZip.CompressData(data);
            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);
            Assert.True(compressed.Length < data.Length);
        }

        [Fact]
        public void CompressData_EmptyArray_ProducesOutput()
        {
            byte[] data = Array.Empty<byte>();
            byte[] compressed = NetZip.CompressData(data);
            Assert.NotNull(compressed);
        }

        [Fact]
        public void CompressData_SmallData_StillCompresses()
        {
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            byte[] compressed = NetZip.CompressData(data);
            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);
        }

        [Fact]
        public void CompressPacket_EmptyMessage_Compresses()
        {
            var message = new NetMessage();
            var compressed = NetZip.CompressPacket(message);
            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);
        }

        [Fact]
        public void CompressDecompress_LargeMessage_RoundTrip()
        {
            var original = new NetMessage();
            byte[] data = new byte[10000];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 10);

            original.Write(data);
            var compressed = NetZip.CompressPacket(original);
            var messageType = MessageType.None;
            compressed.Read(ref messageType);
            long compressedSize = 0;
            long originalSize = 0;
            compressed.ReadScalar(ref compressedSize);
            compressed.ReadScalar(ref originalSize);

            byte[] compressedData;
            compressed.ReadBytes(out compressedData, (int)compressedSize);
            var compressedMessage = new NetMessage(compressedData, (int)compressedSize);

            var decompressed = NetZip.DecompressPacket(compressedMessage);
            Assert.Equal(original.Length, decompressed.Length);
            Assert.Equal(original.Buffer, decompressed.Buffer);
        }

        [Fact]
        public void CompressPacket_WithVaryingData_CompressesEffectively()
        {
            var message = new NetMessage();
            for (int i = 0; i < 100; i++)
            {
                message.Write((byte)0);
                message.Write((byte)1);
                message.Write((byte)2);
            }

            var compressed = NetZip.CompressPacket(message);
            Assert.True(compressed.Length < message.Length);
        }

        [Fact]
        public void CompressPacket_WithRandomData_MayNotCompress()
        {
            var message = new NetMessage();
            var random = new Random(42);
            byte[] data = new byte[100];
            random.NextBytes(data);
            message.Write(data);
            var compressed = NetZip.CompressPacket(message);
            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);
        }

        [Fact]
        public void DecompressPacket_InvalidData_ReturnsEmptyMessage()
        {
            var message = new NetMessage();
            message.Write(new byte[] { 255, 255, 255, 255 });
            var decompressed = NetZip.DecompressPacket(message);
            Assert.NotNull(decompressed);
        }

        [Fact]
        public void CompressPacket_MultipleMessages_EachCompressesIndependently()
        {
            var message1 = new NetMessage();
            var message2 = new NetMessage();
            message1.Write(new byte[] { 1, 1, 1, 1, 1 });
            message2.Write(new byte[] { 2, 2, 2, 2, 2 });
            var compressed1 = NetZip.CompressPacket(message1);
            var compressed2 = NetZip.CompressPacket(message2);
            Assert.NotEqual(compressed1.Buffer, compressed2.Buffer);
        }

        [Fact]
        public void CompressDecompress_AllZeros_HighCompression()
        {
            var original = new NetMessage();
            byte[] data = new byte[1000];
            original.Write(data);

            var compressed = NetZip.CompressPacket(original);

            Assert.True(compressed.Length < original.Length / 2,
                "All-zeros data should compress significantly");
        }

        [Fact]
        public void CompressData_WithOffset_CompressesCorrectPortion()
        {
            byte[] data = new byte[100];
            for (int i = 0; i < data.Length; i++)
                data[i] = 0;

            byte[] compressed = NetZip.CompressData(data);

            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);
        }
    }
}
