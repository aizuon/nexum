using Nexum.Core;
using Xunit;

namespace Nexum.Tests
{
    public class ByteArrayTests
    {
        [Fact]
        public void Constructor_Default_CreatesEmptyArray()
        {
            var byteArray = new ByteArray();

            Assert.Equal(0, byteArray.Length);
            Assert.Equal(0, byteArray.ReadOffset);
            Assert.Equal(0, byteArray.WriteOffset);
        }

        [Fact]
        public void Constructor_WithByteArray_CopiesData()
        {
            byte[] data = { 1, 2, 3, 4, 5 };

            var byteArray = new ByteArray(data);

            Assert.Equal(5, byteArray.Length);
            Assert.Equal(5, byteArray.WriteOffset);
            Assert.Equal(data, byteArray.GetBuffer());
        }

        [Fact]
        public void Constructor_WithByteArrayAndLength_CopiesSpecifiedLength()
        {
            byte[] data = { 1, 2, 3, 4, 5 };

            var byteArray = new ByteArray(data, 3);

            Assert.Equal(3, byteArray.Length);
        }

        [Fact]
        public void Write_ByteArray_AppendsData()
        {
            var byteArray = new ByteArray();
            byte[] data = { 1, 2, 3 };

            byteArray.Write(data);

            Assert.Equal(3, byteArray.Length);
            Assert.Equal(3, byteArray.WriteOffset);
        }

        [Fact]
        public void Write_Byte_AppendsData()
        {
            var byteArray = new ByteArray();

            byteArray.Write((byte)42);

            Assert.Equal(1, byteArray.Length);
            Assert.Equal(1, byteArray.WriteOffset);
        }

        [Fact]
        public void Write_Bool_AppendsData()
        {
            var byteArray = new ByteArray();

            byteArray.Write(true);
            byteArray.Write(false);

            Assert.Equal(2, byteArray.Length);
        }

        [Fact]
        public void Write_Short_AppendsData()
        {
            var byteArray = new ByteArray();

            byteArray.Write((short)1234);

            Assert.Equal(2, byteArray.Length);
        }

        [Fact]
        public void Write_Int_AppendsData()
        {
            var byteArray = new ByteArray();

            byteArray.Write(123456);

            Assert.Equal(4, byteArray.Length);
        }

        [Fact]
        public void Write_Long_AppendsData()
        {
            var byteArray = new ByteArray();

            byteArray.Write(123456789L);

            Assert.Equal(8, byteArray.Length);
        }

        [Fact]
        public void Write_Float_AppendsData()
        {
            var byteArray = new ByteArray();

            byteArray.Write(3.14f);

            Assert.Equal(4, byteArray.Length);
        }

        [Fact]
        public void Write_Double_AppendsData()
        {
            var byteArray = new ByteArray();

            byteArray.Write(3.14159);

            Assert.Equal(8, byteArray.Length);
        }

        [Fact]
        public void WriteScalar_SmallByte_WritesOneByte()
        {
            var byteArray = new ByteArray();

            byteArray.WriteScalar((byte)42);

            Assert.Equal(2, byteArray.Length);
        }

        [Fact]
        public void WriteScalar_Short_WritesTwoBytes()
        {
            var byteArray = new ByteArray();

            byteArray.WriteScalar((short)1234);

            Assert.Equal(3, byteArray.Length);
        }

        [Fact]
        public void WriteScalar_Int_WritesFourBytes()
        {
            var byteArray = new ByteArray();

            byteArray.WriteScalar(123456);

            Assert.Equal(5, byteArray.Length);
        }

        [Fact]
        public void WriteScalar_Long_WritesEightBytes()
        {
            var byteArray = new ByteArray();

            byteArray.WriteScalar(long.MaxValue);

            Assert.Equal(9, byteArray.Length);
        }

        [Fact]
        public void Read_Byte_ReadsCorrectValue()
        {
            var byteArray = new ByteArray();
            byteArray.Write((byte)42);

            byte value = 0;
            bool success = byteArray.Read(ref value);

            Assert.True(success, "Read should succeed for valid byte data");
            Assert.Equal(42, value);
            Assert.Equal(1, byteArray.ReadOffset);
        }

        [Fact]
        public void Read_Bool_ReadsCorrectValue()
        {
            var byteArray = new ByteArray();
            byteArray.Write(true);
            byteArray.Write(false);

            bool value1 = false;
            bool value2 = true;
            bool success1 = byteArray.Read(ref value1);
            bool success2 = byteArray.Read(ref value2);

            Assert.True(success1, "First bool read should succeed");
            Assert.True(success2, "Second bool read should succeed");
            Assert.True(value1, "First bool value should be true");
            Assert.False(value2, "Second bool value should be false");
        }

        [Fact]
        public void Read_Short_ReadsCorrectValue()
        {
            var byteArray = new ByteArray();
            short expected = 1234;
            byteArray.Write(expected);

            short value = 0;
            bool success = byteArray.Read(ref value);

            Assert.True(success, "Read should succeed for valid short data");
            Assert.Equal(expected, value);
        }

        [Fact]
        public void Read_Int_ReadsCorrectValue()
        {
            var byteArray = new ByteArray();
            int expected = 123456;
            byteArray.Write(expected);

            int value = 0;
            bool success = byteArray.Read(ref value);

            Assert.True(success, "Read should succeed for valid int data");
            Assert.Equal(expected, value);
        }

        [Fact]
        public void Read_Long_ReadsCorrectValue()
        {
            var byteArray = new ByteArray();
            long expected = 123456789L;
            byteArray.Write(expected);

            long value = 0;
            bool success = byteArray.Read(ref value);

            Assert.True(success, "Read should succeed for valid long data");
            Assert.Equal(expected, value);
        }

        [Fact]
        public void Read_Float_ReadsCorrectValue()
        {
            var byteArray = new ByteArray();
            float expected = 3.14f;
            byteArray.Write(expected);

            float value = 0;
            bool success = byteArray.Read(ref value);

            Assert.True(success, "Read should succeed for valid float data");
            Assert.Equal(expected, value);
        }

        [Fact]
        public void Read_Double_ReadsCorrectValue()
        {
            var byteArray = new ByteArray();
            double expected = 3.14159;
            byteArray.Write(expected);

            double value = 0;
            bool success = byteArray.Read(ref value);

            Assert.True(success, "Read should succeed for valid double data");
            Assert.Equal(expected, value);
        }

        [Fact]
        public void ReadScalar_WrittenValue_ReadsCorrectly()
        {
            var byteArray = new ByteArray();
            long expected = 123456;
            byteArray.WriteScalar(expected);

            long value = 0;
            bool success = byteArray.ReadScalar(ref value);

            Assert.True(success, "ReadScalar should succeed for valid scalar data");
            Assert.Equal(expected, value);
        }

        [Fact]
        public void ReadScalar_SmallValue_ReadsCorrectly()
        {
            var byteArray = new ByteArray();
            long expected = 42;
            byteArray.WriteScalar(expected);

            long value = 0;
            bool success = byteArray.ReadScalar(ref value);

            Assert.True(success, "ReadScalar should succeed for small values");
            Assert.Equal(expected, value);
        }

        [Fact]
        public void Read_BeyondBuffer_ReturnsFalse()
        {
            var byteArray = new ByteArray();
            byteArray.Write((byte)42);

            byte value1 = 0;
            byte value2 = 0;
            bool success1 = byteArray.Read(ref value1);
            bool success2 = byteArray.Read(ref value2);

            Assert.True(success1, "First read should succeed");
            Assert.False(success2, "Second read should fail when buffer is exhausted");
        }

        [Fact]
        public void ReadBytes_ReadsSpecifiedLength()
        {
            var byteArray = new ByteArray();
            byte[] data = { 1, 2, 3, 4, 5 };
            byteArray.Write(data);

            bool success = byteArray.ReadBytes(out byte[] result, 3);

            Assert.True(success, "ReadBytes should succeed for valid length");
            Assert.Equal(3, result.Length);
            Assert.Equal(1, result[0]);
            Assert.Equal(2, result[1]);
            Assert.Equal(3, result[2]);
        }

        [Fact]
        public void ReadAll_ReadsRemainingData()
        {
            var byteArray = new ByteArray();
            byte[] data = { 1, 2, 3, 4, 5 };
            byteArray.Write(data);
            byte value = 0;
            byteArray.Read(ref value);

            bool success = byteArray.ReadAll(out byte[] result);

            Assert.True(success, "ReadAll should succeed when data is available");
            Assert.Equal(4, result.Length);
        }

        [Fact]
        public void Shrink_RemovesReadData()
        {
            var byteArray = new ByteArray();
            byte[] data = { 1, 2, 3, 4, 5 };
            byteArray.Write(data);
            byte value1 = 0;
            byte value2 = 0;
            byteArray.Read(ref value1);
            byteArray.Read(ref value2);

            byteArray.Shrink();

            Assert.Equal(3, byteArray.Length);
            Assert.Equal(0, byteArray.ReadOffset);
            Assert.Equal(3, byteArray.WriteOffset);
        }

        [Fact]
        public void WriteAt_UpdatesDataAtOffset()
        {
            var byteArray = new ByteArray();
            byte[] data = { 1, 2, 3, 4, 5 };
            byteArray.Write(data);

            byteArray.WriteAt(2, new byte[] { 99, 88 });

            byte[] buffer = byteArray.GetBuffer();
            Assert.Equal(99, buffer[2]);
            Assert.Equal(88, buffer[3]);
        }

        [Fact]
        public void WriteEnum_WritesEnumValue()
        {
            var byteArray = new ByteArray();

            byteArray.WriteEnum(TestEnum.Value2);

            Assert.Equal(4, byteArray.Length);
        }

        [Fact]
        public void GetBuffer_ReturnsCopy()
        {
            var byteArray = new ByteArray();
            byte[] data = { 1, 2, 3 };
            byteArray.Write(data);

            byte[] buffer1 = byteArray.GetBuffer();
            byte[] buffer2 = byteArray.GetBuffer();

            Assert.NotSame(buffer1, buffer2);
            Assert.Equal(buffer1, buffer2);
        }

        [Theory]
        [InlineData((sbyte)-10)]
        [InlineData((sbyte)0)]
        [InlineData((sbyte)127)]
        public void WriteRead_SByte_RoundTrip(sbyte value)
        {
            var byteArray = new ByteArray();

            byteArray.Write(value);
            sbyte result = 0;
            bool success = byteArray.Read(ref result);

            Assert.True(success, "Read should succeed for sbyte data");
            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData((ushort)0)]
        [InlineData((ushort)1234)]
        [InlineData(ushort.MaxValue)]
        public void WriteRead_UShort_RoundTrip(ushort value)
        {
            var byteArray = new ByteArray();

            byteArray.Write(value);
            ushort result = 0;
            bool success = byteArray.Read(ref result);

            Assert.True(success, "Read should succeed for ushort data");
            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(123456u)]
        [InlineData(uint.MaxValue)]
        public void WriteRead_UInt_RoundTrip(uint value)
        {
            var byteArray = new ByteArray();

            byteArray.Write(value);
            uint result = 0;
            bool success = byteArray.Read(ref result);

            Assert.True(success, "Read should succeed for uint data");
            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(0ul)]
        [InlineData(123456789ul)]
        [InlineData(ulong.MaxValue)]
        public void WriteRead_ULong_RoundTrip(ulong value)
        {
            var byteArray = new ByteArray();

            byteArray.Write(value);
            ulong result = 0;
            bool success = byteArray.Read(ref result);

            Assert.True(success, "Read should succeed for ulong data");
            Assert.Equal(value, result);
        }

        [Fact]
        public void WriteRead_ByteArray_RoundTrip()
        {
            var byteArray1 = new ByteArray();
            var byteArray2 = new ByteArray(new byte[] { 1, 2, 3, 4, 5 });

            byteArray1.Write(byteArray2);
            var result = new ByteArray();
            bool success = byteArray1.Read(ref result);

            Assert.True(success, "Read should succeed for ByteArray data");
            Assert.Equal(byteArray2.GetBuffer(), result.GetBuffer());
        }

        [Theory]
        [InlineData(short.MinValue)]
        [InlineData(short.MaxValue)]
        [InlineData((short)0)]
        public void WriteRead_Short_BoundaryValues(short value)
        {
            var byteArray = new ByteArray();

            byteArray.Write(value);
            short result = 0;
            bool success = byteArray.Read(ref result);

            Assert.True(success, "Read should succeed for short boundary values");
            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        [InlineData(0)]
        public void WriteRead_Int_BoundaryValues(int value)
        {
            var byteArray = new ByteArray();

            byteArray.Write(value);
            int result = 0;
            bool success = byteArray.Read(ref result);

            Assert.True(success, "Read should succeed for int boundary values");
            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(long.MinValue)]
        [InlineData(long.MaxValue)]
        [InlineData(0L)]
        public void WriteRead_Long_BoundaryValues(long value)
        {
            var byteArray = new ByteArray();

            byteArray.Write(value);
            long result = 0;
            bool success = byteArray.Read(ref result);

            Assert.True(success, "Read should succeed for long boundary values");
            Assert.Equal(value, result);
        }

        [Fact]
        public void WriteScalar_NegativeValue_WritesCorrectly()
        {
            var byteArray = new ByteArray();

            byteArray.WriteScalar(-100L);
            long result = 0;
            bool success = byteArray.ReadScalar(ref result);

            Assert.True(success, "ReadScalar should succeed for negative values");
            Assert.Equal(-100L, result);
        }

        [Fact]
        public void Read_EmptyBuffer_ReturnsFalse()
        {
            var byteArray = new ByteArray();

            int value = 0;
            bool success = byteArray.Read(ref value);

            Assert.False(success, "Read should fail on empty buffer");
        }

        [Fact]
        public void ReadBytes_ExceedsAvailable_ReturnsFalse()
        {
            var byteArray = new ByteArray();
            byteArray.Write(new byte[] { 1, 2, 3 });

            bool success = byteArray.ReadBytes(out byte[] _, 10);

            Assert.False(success, "ReadBytes should fail when requested length exceeds available data");
        }

        private enum TestEnum
        {
            Value1 = 1,
            Value2 = 2,
            Value3 = 3
        }
    }
}
