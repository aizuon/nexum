using System;
using System.Net;
using System.Text;
using Nexum.Core;
using Xunit;

namespace Nexum.Tests
{
    public class NetMessageTests
    {
        public NetMessageTests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [Fact]
        public void Constructor_Default_InitializesProperties()
        {
            var message = new NetMessage();
            Assert.False(message.Compress);
            Assert.Equal(EncryptMode.None, message.EncryptMode);
            Assert.True(message.Reliable);
            Assert.Equal(0u, message.RelayFrom);
        }

        [Fact]
        public void Constructor_FromByteArray_CopiesData()
        {
            var byteArray = new ByteArray(new byte[] { 1, 2, 3, 4, 5 });
            var message = new NetMessage(byteArray);
            Assert.Equal(5, message.Length);
        }

        [Fact]
        public void Constructor_FromByteArrayWithLength_CopiesSpecifiedLength()
        {
            byte[] data = { 1, 2, 3, 4, 5 };
            var message = new NetMessage(data, 3);
            Assert.Equal(3, message.Length);
        }

        [Fact]
        public void Constructor_FromNetMessage_CopiesDataAndProperties()
        {
            var original = new NetMessage();
            original.Write(new byte[] { 1, 2, 3 });
            original.EncryptMode = EncryptMode.Secure;
            var copy = new NetMessage(original);
            Assert.Equal(original.Length, copy.Length);
            Assert.Equal(original.EncryptMode, copy.EncryptMode);
        }

        [Fact]
        public void Encrypt_ReturnsTrue_WhenEncryptModeIsNotNone()
        {
            var message = new NetMessage
            {
                EncryptMode = EncryptMode.Secure
            };
            Assert.True(message.Encrypt);
        }

        [Fact]
        public void Encrypt_ReturnsFalse_WhenEncryptModeIsNone()
        {
            var message = new NetMessage
            {
                EncryptMode = EncryptMode.None
            };
            Assert.False(message.Encrypt);
        }

        [Fact]
        public void Buffer_ReturnsBufferCopy()
        {
            var message = new NetMessage();
            message.Write(new byte[] { 1, 2, 3 });
            byte[] buffer1 = message.Buffer;
            byte[] buffer2 = message.Buffer;
            Assert.NotSame(buffer1, buffer2);
            Assert.Equal(buffer1, buffer2);
        }

        [Fact]
        public void WriteRead_MessageType_RoundTrip()
        {
            var message = new NetMessage();
            var expected = MessageType.Compressed;
            message.WriteEnum(expected);
            var result = MessageType.None;
            bool success = message.Read(ref result);
            Assert.True(success);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void WriteRead_String_NonUnicode_RoundTrip()
        {
            var message = new NetMessage();
            string expected = "Hello World";
            message.Write(expected);
            string result = string.Empty;
            bool success = message.ReadString(ref result);
            Assert.True(success);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void WriteRead_String_Unicode_RoundTrip()
        {
            var message = new NetMessage();
            string expected = "Hello 世界 🌍";
            message.Write(expected, true);
            string result = string.Empty;
            bool success = message.Read(ref result, out bool isUnicode);
            Assert.True(success);
            Assert.True(isUnicode);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void WriteRead_EmptyString_RoundTrip()
        {
            var message = new NetMessage();
            string expected = string.Empty;
            message.Write(expected);
            string result = "not empty";
            bool success = message.ReadString(ref result);
            Assert.True(success);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void WriteRead_Guid_RoundTrip()
        {
            var message = new NetMessage();
            var expected = Guid.NewGuid();
            message.Write(expected);
            bool success = message.Read(out Guid result);
            Assert.True(success);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void WriteRead_Version_RoundTrip()
        {
            var message = new NetMessage();
            var expected = new Version(1, 2, 3, 4);
            message.Write(expected);
            var result = new Version();
            bool success = message.ReadVersion(ref result);
            Assert.True(success);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void WriteRead_IPEndPoint_RoundTrip()
        {
            var message = new NetMessage();
            var expected = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 8080);
            message.Write(expected);
            IPEndPoint result = null;
            bool success = message.ReadIPEndPoint(ref result);
            Assert.True(success);
            Assert.Equal(expected.Address, result.Address);
            Assert.Equal(expected.Port, result.Port);
        }

        [Fact]
        public void WriteRead_StringEndPoint_RoundTrip()
        {
            var message = new NetMessage();
            var expected = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 9090);
            message.WriteStringEndPoint(expected);
            IPEndPoint result = null;
            bool success = message.ReadStringEndPoint(ref result);
            Assert.True(success);
            Assert.Equal(expected.Address, result.Address);
            Assert.Equal(expected.Port, result.Port);
        }

        [Fact]
        public void Write_NetMessage_CopiesDataAndProperties()
        {
            var message1 = new NetMessage();
            var message2 = new NetMessage();
            message2.Write(new byte[] { 1, 2, 3 });
            message2.Compress = true;
            message2.EncryptMode = EncryptMode.Fast;
            message1.Write(message2);
            Assert.Equal(message2.Buffer, message1.Buffer);
            Assert.Equal(message2.Compress, message1.Compress);
            Assert.Equal(message2.EncryptMode, message1.EncryptMode);
        }

        [Fact]
        public void ReadString_ReturnsString()
        {
            var message = new NetMessage();
            string expected = "Test String";
            message.Write(expected);
            string result = message.ReadString();
            Assert.Equal(expected, result);
        }

        [Fact]
        public void EncryptMode_DefaultIsNone()
        {
            var message = new NetMessage();
            Assert.Equal(EncryptMode.None, message.EncryptMode);
        }

        [Fact]
        public void Reliable_DefaultIsTrue()
        {
            var message = new NetMessage();
            Assert.True(message.Reliable);
        }

        [Fact]
        public void Compress_DefaultIsFalse()
        {
            var message = new NetMessage();
            Assert.False(message.Compress);
        }

        [Fact]
        public void RelayFrom_DefaultIsZero()
        {
            var message = new NetMessage();
            Assert.Equal(0u, message.RelayFrom);
        }

        [Theory]
        [InlineData(EncryptMode.None)]
        [InlineData(EncryptMode.Secure)]
        [InlineData(EncryptMode.Fast)]
        public void EncryptMode_CanBeSet(EncryptMode mode)
        {
            var message = new NetMessage();
            message.EncryptMode = mode;
            Assert.Equal(mode, message.EncryptMode);
        }

        [Fact]
        public void Compress_CanBeSet()
        {
            var message = new NetMessage();
            message.Compress = true;
            Assert.True(message.Compress);
        }

        [Fact]
        public void Reliable_CanBeSet()
        {
            var message = new NetMessage();
            message.Reliable = false;
            Assert.False(message.Reliable);
        }

        [Fact]
        public void RelayFrom_CanBeSet()
        {
            var message = new NetMessage();
            message.RelayFrom = 12345u;
            Assert.Equal(12345u, message.RelayFrom);
        }

        [Fact]
        public void WriteRead_MultipleValues_RoundTrip()
        {
            var message = new NetMessage();
            int expectedInt = 42;
            string expectedString = "Test";
            bool expectedBool = true;
            message.Write(expectedInt);
            message.Write(expectedString);
            message.Write(expectedBool);

            bool successInt = message.Read(out int resultInt);
            bool successString = message.Read(out string resultString);
            bool successBool = message.Read(out bool resultBool);
            Assert.True(successInt);
            Assert.True(successString);
            Assert.True(successBool);
            Assert.Equal(expectedInt, resultInt);
            Assert.Equal(expectedString, resultString);
            Assert.Equal(expectedBool, resultBool);
        }

        [Fact]
        public void Read_BeyondBufferEnd_ReturnsFalse()
        {
            var message = new NetMessage();
            message.Write((byte)1);

            byte b = 0;
            message.Read(ref b);
            int value = 0;
            bool success = message.Read(ref value);

            Assert.False(success);
        }

        [Fact]
        public void ReadGuid_InsufficientData_ReturnsFalse()
        {
            var message = new NetMessage();
            message.Write(new byte[] { 1, 2, 3, 4 });

            bool success = message.Read(out Guid result);

            Assert.False(success);
        }

        [Fact]
        public void WriteRead_IPEndPoint_LoopbackAddress_RoundTrip()
        {
            var message = new NetMessage();
            var expected = new IPEndPoint(IPAddress.Loopback, 12345);

            message.Write(expected);
            IPEndPoint result = null;
            bool success = message.ReadIPEndPoint(ref result);

            Assert.True(success);
            Assert.Equal(expected.Address, result.Address);
            Assert.Equal(expected.Port, result.Port);
        }

        [Fact]
        public void WriteRead_LongString_RoundTrip()
        {
            var message = new NetMessage();
            string expected = new string('A', 10000);

            message.Write(expected);
            string result = string.Empty;
            bool success = message.ReadString(ref result);

            Assert.True(success);
            Assert.Equal(expected, result);
        }
    }
}
