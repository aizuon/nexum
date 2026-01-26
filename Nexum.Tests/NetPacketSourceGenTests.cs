using System;
using System.Net;
using Nexum.Core.Attributes;
using Nexum.Core.Serialization;
using Xunit;

namespace Nexum.Tests
{
    public enum TestEnum : byte
    {
        None,
        First,
        Second,
        Third
    }

    public enum TestEnumInt
    {
        Zero = 0,
        One = 1,
        Hundred = 100
    }

    [NetSerializable]
    public partial class AllPrimitivesPacket
    {
        [NetProperty(0)]
        public bool BoolValue { get; set; }

        [NetProperty(1)]
        public byte ByteValue { get; set; }

        [NetProperty(2)]
        public sbyte SByteValue { get; set; }

        [NetProperty(3)]
        public short ShortValue { get; set; }

        [NetProperty(4)]
        public ushort UShortValue { get; set; }

        [NetProperty(5)]
        public int IntValue { get; set; }

        [NetProperty(6)]
        public uint UIntValue { get; set; }

        [NetProperty(7)]
        public long LongValue { get; set; }

        [NetProperty(8)]
        public ulong ULongValue { get; set; }

        [NetProperty(9)]
        public float FloatValue { get; set; }

        [NetProperty(10)]
        public double DoubleValue { get; set; }
    }

    [NetSerializable]
    public partial class StringAndGuidPacket
    {
        [NetProperty(0)]
        public string StringValue { get; set; }

        [NetProperty(1)]
        public Guid GuidValue { get; set; }

        [NetProperty(2)]
        public Version VersionValue { get; set; }
    }

    [NetSerializable]
    public partial class EnumPacket
    {
        [NetProperty(0)]
        public TestEnum ByteEnum { get; set; }

        [NetProperty(1)]
        public TestEnumInt IntEnum { get; set; }
    }

    [NetSerializable]
    public partial class IPEndPointDefaultPacket
    {
        [NetProperty(0)]
        public IPEndPoint EndPoint { get; set; }
    }

    [NetSerializable]
    public partial class IPEndPointStringPacket
    {
        [NetProperty(0, typeof(StringEndPointSerializer))]
        public IPEndPoint EndPoint { get; set; }
    }

    [NetSerializable]
    public partial class ByteArrayPacket
    {
        [NetProperty(0)]
        public ByteArray ByteArrayValue { get; set; }
    }

    [NetSerializable]
    public partial class UnicodeStringPacket
    {
        [NetProperty(0, typeof(UnicodeStringSerializer))]
        public string UnicodeValue { get; set; }
    }

    [NetSerializable]
    public partial class MixedTypesPacket
    {
        [NetProperty(0)]
        public int Id { get; set; }

        [NetProperty(1)]
        public string Name { get; set; }

        [NetProperty(2)]
        public TestEnum Status { get; set; }

        [NetProperty(3)]
        public Guid Token { get; set; }

        [NetProperty(4)]
        public IPEndPoint Address { get; set; }

        [NetProperty(5, typeof(StringEndPointSerializer))]
        public IPEndPoint StringAddress { get; set; }

        [NetProperty(6)]
        public ByteArray Payload { get; set; }
    }

    [NetSerializable]
    public partial class PrimitiveArraysPacket
    {
        [NetProperty(0)]
        public uint[] UIntArray { get; set; }

        [NetProperty(1)]
        public int[] IntArray { get; set; }

        [NetProperty(2)]
        public byte[] ByteArrayProp { get; set; }

        [NetProperty(3)]
        public long[] LongArray { get; set; }

        [NetProperty(4)]
        public float[] FloatArray { get; set; }

        [NetProperty(5)]
        public double[] DoubleArray { get; set; }
    }

    [NetSerializable]
    public partial class StringArrayPacket
    {
        [NetProperty(0)]
        public string[] StringArray { get; set; }
    }

    [NetSerializable]
    public partial class EnumArrayPacket
    {
        [NetProperty(0)]
        public TestEnum[] EnumArray { get; set; }
    }

    [NetSerializable]
    public partial class NestedDataDto
    {
        [NetProperty(0)]
        public int IntValue { get; set; }

        [NetProperty(1)]
        public string StringValue { get; set; }

        [NetProperty(2)]
        public uint UIntValue { get; set; }
    }

    [NetSerializable]
    public partial class NestedObjectPacket
    {
        [NetProperty(0)]
        public int Id { get; set; }

        [NetProperty(1)]
        public NestedDataDto NestedData { get; set; }

        [NetProperty(2)]
        public string Comment { get; set; }
    }

    [NetSerializable]
    public partial class ObjectArrayPacket
    {
        [NetProperty(0)]
        public int Id { get; set; }

        [NetProperty(1)]
        public NestedDataDto[] DataArray { get; set; }
    }

    [NetSerializable]
    public partial class ComplexMixedPacket
    {
        [NetProperty(0)]
        public int Id { get; set; }

        [NetProperty(1)]
        public uint[] UIntArray { get; set; }

        [NetProperty(2)]
        public NestedDataDto NestedData { get; set; }

        [NetProperty(3)]
        public NestedDataDto[] DataArray { get; set; }

        [NetProperty(4)]
        public string[] StringArray { get; set; }
    }

    public class NetPacketSourceGenTests
    {
        [Fact]
        public void AllPrimitives_RoundTrip()
        {
            var original = new AllPrimitivesPacket
            {
                BoolValue = true,
                ByteValue = 255,
                SByteValue = -128,
                ShortValue = -32768,
                UShortValue = 65535,
                IntValue = int.MinValue,
                UIntValue = uint.MaxValue,
                LongValue = long.MinValue,
                ULongValue = ulong.MaxValue,
                FloatValue = 3.14159f,
                DoubleValue = 2.718281828459045
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = AllPrimitivesPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.BoolValue, result.BoolValue);
            Assert.Equal(original.ByteValue, result.ByteValue);
            Assert.Equal(original.SByteValue, result.SByteValue);
            Assert.Equal(original.ShortValue, result.ShortValue);
            Assert.Equal(original.UShortValue, result.UShortValue);
            Assert.Equal(original.IntValue, result.IntValue);
            Assert.Equal(original.UIntValue, result.UIntValue);
            Assert.Equal(original.LongValue, result.LongValue);
            Assert.Equal(original.ULongValue, result.ULongValue);
            Assert.Equal(original.FloatValue, result.FloatValue);
            Assert.Equal(original.DoubleValue, result.DoubleValue);
        }

        [Fact]
        public void StringAndGuid_RoundTrip()
        {
            var original = new StringAndGuidPacket
            {
                StringValue = "Hello, World! Test123",
                GuidValue = Guid.NewGuid(),
                VersionValue = new Version(1, 2, 3, 4)
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = StringAndGuidPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.StringValue, result.StringValue);
            Assert.Equal(original.GuidValue, result.GuidValue);
            Assert.Equal(original.VersionValue, result.VersionValue);
        }

        [Fact]
        public void Enum_RoundTrip()
        {
            var original = new EnumPacket
            {
                ByteEnum = TestEnum.Third,
                IntEnum = TestEnumInt.Hundred
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = EnumPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.ByteEnum, result.ByteEnum);
            Assert.Equal(original.IntEnum, result.IntEnum);
        }

        [Fact]
        public void IPEndPointDefault_UsesByteArrayFormat()
        {
            var original = new IPEndPointDefaultPacket
            {
                EndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 8080)
            };

            var msg = original.Serialize();

            Assert.Equal(6, msg.Length);

            msg.ReadOffset = 0;
            bool success = IPEndPointDefaultPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.EndPoint.Address, result.EndPoint.Address);
            Assert.Equal(original.EndPoint.Port, result.EndPoint.Port);
        }

        [Fact]
        public void IPEndPointString_UsesStringFormat()
        {
            var original = new IPEndPointStringPacket
            {
                EndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 8080)
            };

            var msg = original.Serialize();

            Assert.True(msg.Length > 6, "String format should use more than 6 bytes");

            msg.ReadOffset = 0;
            bool success = IPEndPointStringPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.EndPoint.Address, result.EndPoint.Address);
            Assert.Equal(original.EndPoint.Port, result.EndPoint.Port);
        }

        [Fact]
        public void ByteArray_RoundTrip()
        {
            var original = new ByteArrayPacket
            {
                ByteArrayValue = new ByteArray(new byte[] { 1, 2, 3, 4, 5 }, true)
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = ByteArrayPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.NotNull(result);
            Assert.Equal(original.ByteArrayValue.GetBuffer(), result.ByteArrayValue.GetBuffer());
        }

        [Fact]
        public void UnicodeString_RoundTrip()
        {
            var original = new UnicodeStringPacket
            {
                UnicodeValue = "Hello, World! 你好世界 🎉"
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = UnicodeStringPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.NotNull(result);
            Assert.Equal(original.UnicodeValue, result.UnicodeValue);
        }

        [Fact]
        public void MixedTypes_RoundTrip()
        {
            var original = new MixedTypesPacket
            {
                Id = 42,
                Name = "TestPacket",
                Status = TestEnum.Second,
                Token = Guid.NewGuid(),
                Address = new IPEndPoint(IPAddress.Loopback, 1234),
                StringAddress = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 5678),
                Payload = new ByteArray(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, true)
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = MixedTypesPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.Id, result.Id);
            Assert.Equal(original.Name, result.Name);
            Assert.Equal(original.Status, result.Status);
            Assert.Equal(original.Token, result.Token);
            Assert.Equal(original.Address.Address, result.Address.Address);
            Assert.Equal(original.Address.Port, result.Address.Port);
            Assert.Equal(original.StringAddress.Address, result.StringAddress.Address);
            Assert.Equal(original.StringAddress.Port, result.StringAddress.Port);
            Assert.Equal(original.Payload.GetBuffer(), result.Payload.GetBuffer());
        }

        [Fact]
        public void Deserialize_FailsOnEmptyMessage()
        {
            var msg = new NetMessage();

            Assert.False(AllPrimitivesPacket.Deserialize(msg, out _));
            Assert.False(StringAndGuidPacket.Deserialize(msg, out _));
            Assert.False(EnumPacket.Deserialize(msg, out _));
            Assert.False(IPEndPointDefaultPacket.Deserialize(msg, out _));
            Assert.False(IPEndPointStringPacket.Deserialize(msg, out _));
            Assert.False(ByteArrayPacket.Deserialize(msg, out _));
            Assert.False(UnicodeStringPacket.Deserialize(msg, out _));
            Assert.False(MixedTypesPacket.Deserialize(msg, out _));
        }

        [Fact]
        public void Deserialize_FailsOnPartialData()
        {
            var msg = new NetMessage();
            msg.Write(true);
            msg.ReadOffset = 0;

            Assert.False(AllPrimitivesPacket.Deserialize(msg, out _));
        }

        [Fact]
        public void DefaultValues_Serialize()
        {
            var packet = new AllPrimitivesPacket();

            var msg = packet.Serialize();

            Assert.True(msg.Length > 0);
        }

        [Fact]
        public void PrimitiveArrays_RoundTrip()
        {
            var original = new PrimitiveArraysPacket
            {
                UIntArray = new uint[] { 1, 2, 3, uint.MaxValue },
                IntArray = new[] { -100, 0, 100, int.MinValue },
                ByteArrayProp = new byte[] { 0, 127, 255 },
                LongArray = new[] { long.MinValue, 0, long.MaxValue },
                FloatArray = new[] { 1.5f, -2.5f, float.MaxValue },
                DoubleArray = new[] { 3.14159, -2.71828, double.MaxValue }
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = PrimitiveArraysPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.UIntArray, result.UIntArray);
            Assert.Equal(original.IntArray, result.IntArray);
            Assert.Equal(original.ByteArrayProp, result.ByteArrayProp);
            Assert.Equal(original.LongArray, result.LongArray);
            Assert.Equal(original.FloatArray, result.FloatArray);
            Assert.Equal(original.DoubleArray, result.DoubleArray);
        }

        [Fact]
        public void StringArray_RoundTrip()
        {
            var original = new StringArrayPacket
            {
                StringArray = new[] { "Hello", "World", "Test123", "" }
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = StringArrayPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.StringArray, result.StringArray);
        }

        [Fact]
        public void EnumArray_RoundTrip()
        {
            var original = new EnumArrayPacket
            {
                EnumArray = new[] { TestEnum.None, TestEnum.First, TestEnum.Third }
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = EnumArrayPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.EnumArray, result.EnumArray);
        }

        [Fact]
        public void NestedObject_RoundTrip()
        {
            var original = new NestedObjectPacket
            {
                Id = 12345,
                NestedData = new NestedDataDto
                {
                    IntValue = 100,
                    StringValue = "TestString",
                    UIntValue = 1
                },
                Comment = "Test nested object"
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = NestedObjectPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.Id, result.Id);
            Assert.NotNull(result.NestedData);
            Assert.Equal(original.NestedData.IntValue, result.NestedData.IntValue);
            Assert.Equal(original.NestedData.StringValue, result.NestedData.StringValue);
            Assert.Equal(original.NestedData.UIntValue, result.NestedData.UIntValue);
            Assert.Equal(original.Comment, result.Comment);
        }

        [Fact]
        public void ObjectArray_RoundTrip()
        {
            var original = new ObjectArrayPacket
            {
                Id = 99,
                DataArray = new[]
                {
                    new NestedDataDto { IntValue = 1, StringValue = "First", UIntValue = 10 },
                    new NestedDataDto { IntValue = 2, StringValue = "Second", UIntValue = 5 },
                    new NestedDataDto { IntValue = 3, StringValue = "Third", UIntValue = 3 }
                }
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = ObjectArrayPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.Id, result.Id);
            Assert.NotNull(result.DataArray);
            Assert.Equal(original.DataArray.Length, result.DataArray.Length);
            for (int i = 0; i < original.DataArray.Length; i++)
            {
                Assert.Equal(original.DataArray[i].IntValue, result.DataArray[i].IntValue);
                Assert.Equal(original.DataArray[i].StringValue, result.DataArray[i].StringValue);
                Assert.Equal(original.DataArray[i].UIntValue, result.DataArray[i].UIntValue);
            }
        }

        [Fact]
        public void ComplexMixed_RoundTrip()
        {
            var original = new ComplexMixedPacket
            {
                Id = 42,
                UIntArray = new uint[] { 100, 200, 300 },
                NestedData = new NestedDataDto
                {
                    IntValue = 50,
                    StringValue = "NestedString",
                    UIntValue = 1
                },
                DataArray = new[]
                {
                    new NestedDataDto { IntValue = 1, StringValue = "ArrayItem1", UIntValue = 99 },
                    new NestedDataDto { IntValue = 2, StringValue = "ArrayItem2", UIntValue = 50 }
                },
                StringArray = new[] { "tag1", "tag2", "tag3" }
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = ComplexMixedPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.Id, result.Id);
            Assert.Equal(original.UIntArray, result.UIntArray);
            Assert.NotNull(result.NestedData);
            Assert.Equal(original.NestedData.IntValue, result.NestedData.IntValue);
            Assert.Equal(original.NestedData.StringValue, result.NestedData.StringValue);
            Assert.Equal(original.NestedData.UIntValue, result.NestedData.UIntValue);
            Assert.NotNull(result.DataArray);
            Assert.Equal(original.DataArray.Length, result.DataArray.Length);
            for (int i = 0; i < original.DataArray.Length; i++)
            {
                Assert.Equal(original.DataArray[i].IntValue, result.DataArray[i].IntValue);
                Assert.Equal(original.DataArray[i].StringValue, result.DataArray[i].StringValue);
                Assert.Equal(original.DataArray[i].UIntValue, result.DataArray[i].UIntValue);
            }

            Assert.Equal(original.StringArray, result.StringArray);
        }

        [Fact]
        public void EmptyArrays_RoundTrip()
        {
            var original = new PrimitiveArraysPacket
            {
                UIntArray = Array.Empty<uint>(),
                IntArray = Array.Empty<int>(),
                ByteArrayProp = Array.Empty<byte>(),
                LongArray = Array.Empty<long>(),
                FloatArray = Array.Empty<float>(),
                DoubleArray = Array.Empty<double>()
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = PrimitiveArraysPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Empty(result.UIntArray);
            Assert.Empty(result.IntArray);
            Assert.Empty(result.ByteArrayProp);
            Assert.Empty(result.LongArray);
            Assert.Empty(result.FloatArray);
            Assert.Empty(result.DoubleArray);
        }

        [Fact]
        public void EmptyObjectArray_RoundTrip()
        {
            var original = new ObjectArrayPacket
            {
                Id = 1,
                DataArray = Array.Empty<NestedDataDto>()
            };

            var msg = original.Serialize();
            msg.ReadOffset = 0;

            bool success = ObjectArrayPacket.Deserialize(msg, out var result);

            Assert.True(success);
            Assert.Equal(original.Id, result.Id);
            Assert.NotNull(result.DataArray);
            Assert.Empty(result.DataArray);
        }

        [Fact]
        public void ArrayDeserialize_FailsOnEmptyMessage()
        {
            var msg = new NetMessage();

            Assert.False(PrimitiveArraysPacket.Deserialize(msg, out _));
            Assert.False(StringArrayPacket.Deserialize(msg, out _));
            Assert.False(ObjectArrayPacket.Deserialize(msg, out _));
            Assert.False(NestedObjectPacket.Deserialize(msg, out _));
            Assert.False(ComplexMixedPacket.Deserialize(msg, out _));
        }
    }
}
