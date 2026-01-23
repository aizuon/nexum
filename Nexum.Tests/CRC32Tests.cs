using System;
using System.Buffers.Binary;
using System.Text;
using BaseLib;
using Xunit;

namespace Nexum.Tests
{
    public class CRC32Tests
    {
        [Fact]
        public void Compute_Empty_IsZero()
        {
            uint crc = CRC32.Compute(ReadOnlySpan<byte>.Empty);
            Assert.Equal(0u, crc);
        }

        [Fact]
        public void Compute_StandardVector_123456789()
        {
            byte[] data = Encoding.ASCII.GetBytes("123456789");
            uint crc = CRC32.Compute(data);
            Assert.Equal(0xCBF43926u, crc);
        }

        [Fact]
        public void HashAlgorithm_OffsetCount_MatchesCompute()
        {
            byte[] data = Encoding.ASCII.GetBytes("abcde");
            byte[] slice = Encoding.ASCII.GetBytes("bcd");

            uint expected = CRC32.Compute(slice);

            using var crc32 = new CRC32();
            byte[] hash = crc32.ComputeHash(data, 1, 3);
            uint actual = BinaryPrimitives.ReadUInt32LittleEndian(hash);

            Assert.Equal(expected, actual);
        }
    }
}
