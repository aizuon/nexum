using System;
using Nexum.Core;
using Xunit;

namespace Nexum.Tests
{
    public class NetCryptTests
    {
        [Fact]
        public void Constructor_WithKeySizes_GeneratesRandomKeys()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);

            Assert.NotNull(crypt.GetKey());
            Assert.NotNull(crypt.GetFastKey());
            Assert.Equal(NetCrypt.DefaultKeyLength / 8, crypt.GetKey().Length);
            Assert.Equal(NetCrypt.DefaultFastKeyLength / 8, crypt.GetFastKey().Length);
        }

        [Fact]
        public void Constructor_WithZeroKeySizes_UsesDefaultKeys()
        {
            using var crypt = new NetCrypt(0, 0);

            Assert.NotNull(crypt.GetKey());
            Assert.NotNull(crypt.GetFastKey());
        }

        [Fact]
        public void Constructor_WithSecureKey_SetsKey()
        {
            byte[] key = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

            using var crypt = new NetCrypt(key);

            Assert.Equal(key, crypt.GetKey());
        }

        [Fact]
        public void InitializeFastEncryption_SetsKey()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, 0);
            byte[] fastKey = new byte[64];
            for (int i = 0; i < fastKey.Length; i++)
                fastKey[i] = (byte)i;

            crypt.InitializeFastEncryption(fastKey);

            Assert.Equal(fastKey, crypt.GetFastKey());
        }

        [Fact]
        public void EncryptDecrypt_SecureMode_RoundTrip()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

            byte[] encrypted = crypt.Encrypt(original, EncryptMode.Secure);
            byte[] decrypted = crypt.Decrypt(encrypted, EncryptMode.Secure);

            Assert.NotEqual(original, encrypted);
            Assert.Equal(original, decrypted);
        }

        [Fact]
        public void EncryptDecrypt_FastMode_RoundTrip()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

            byte[] encrypted = crypt.Encrypt(original, EncryptMode.Fast);
            byte[] decrypted = crypt.Decrypt(encrypted, EncryptMode.Fast);

            Assert.NotEqual(original, encrypted);
            Assert.Equal(original, decrypted);
        }

        [Fact]
        public void EncryptDecrypt_WithLength_RoundTrip()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            int length = 5;

            byte[] encrypted = crypt.Encrypt(original, length, EncryptMode.Secure);
            byte[] decrypted = crypt.Decrypt(encrypted, encrypted.Length, EncryptMode.Secure);

            Assert.Equal(length, decrypted.Length);
            for (int i = 0; i < length; i++)
                Assert.Equal(original[i], decrypted[i]);
        }

        [Fact]
        public void Encrypt_SecureMode_ProducesDifferentOutput()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };

            byte[] encrypted = crypt.Encrypt(data, EncryptMode.Secure);

            Assert.NotEqual(data, encrypted);
            Assert.NotEqual(data.Length, encrypted.Length);
        }

        [Fact]
        public void Encrypt_FastMode_SameLength()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };

            byte[] encrypted = crypt.Encrypt(data, EncryptMode.Fast);

            Assert.Equal(data.Length, encrypted.Length);
            Assert.NotEqual(data, encrypted);
        }

        [Fact]
        public void EncryptKey_ProducesEncryptedData()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] key = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

            byte[] encrypted = crypt.EncryptKey(key);

            Assert.NotNull(encrypted);
            Assert.NotEqual(key, encrypted);
        }

        [Fact]
        public void DecryptKey_ReturnsOriginalKey()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] originalKey = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

            byte[] encrypted = crypt.EncryptKey(originalKey);
            byte[] decrypted = crypt.DecryptKey(encrypted);

            Assert.Equal(originalKey, decrypted);
        }

        [Fact]
        public void Dispose_SetsKeysToNull()
        {
            var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);

            crypt.Dispose();

            Assert.Null(crypt.GetKey());
            Assert.Null(crypt.GetFastKey());
        }

        [Fact]
        public void Encrypt_AfterDispose_ThrowsObjectDisposedException()
        {
            var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            crypt.Dispose();
            byte[] data = new byte[] { 1, 2, 3 };

            Assert.Throws<ObjectDisposedException>(() => crypt.Encrypt(data, EncryptMode.Secure));
        }

        [Fact]
        public void Decrypt_AfterDispose_ThrowsObjectDisposedException()
        {
            var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] data = new byte[] { 1, 2, 3 };
            byte[] encrypted = crypt.Encrypt(data, EncryptMode.Secure);
            crypt.Dispose();

            Assert.Throws<ObjectDisposedException>(() => crypt.Decrypt(encrypted, EncryptMode.Secure));
        }

        [Fact]
        public void EncryptKey_AfterDispose_ThrowsObjectDisposedException()
        {
            var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            crypt.Dispose();
            byte[] key = new byte[16];

            Assert.Throws<ObjectDisposedException>(() => crypt.EncryptKey(key));
        }

        [Fact]
        public void DecryptKey_AfterDispose_ThrowsObjectDisposedException()
        {
            var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] key = new byte[16];
            byte[] encrypted = crypt.EncryptKey(key);
            crypt.Dispose();

            Assert.Throws<ObjectDisposedException>(() => crypt.DecryptKey(encrypted));
        }

        [Fact]
        public void Encrypt_InvalidMode_ThrowsArgumentException()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] data = new byte[] { 1, 2, 3 };

            Assert.Throws<ArgumentException>(() => crypt.Encrypt(data, EncryptMode.None));
        }

        [Fact]
        public void Decrypt_InvalidMode_ThrowsArgumentException()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] data = new byte[] { 1, 2, 3 };

            Assert.Throws<ArgumentException>(() => crypt.Decrypt(data, EncryptMode.None));
        }

        [Theory]
        [InlineData(128)]
        [InlineData(256)]
        [InlineData(512)]
        public void Constructor_DifferentKeySizes_CreatesCorrectKeyLength(uint keySize)
        {
            using var crypt = new NetCrypt(keySize, NetCrypt.DefaultFastKeyLength);

            Assert.Equal(keySize / 8, (uint)crypt.GetKey().Length);
        }

        [Fact]
        public void EncryptDecrypt_LargeData_RoundTrip()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] original = new byte[10000];
            for (int i = 0; i < original.Length; i++)
                original[i] = (byte)(i % 256);

            byte[] encrypted = crypt.Encrypt(original, EncryptMode.Secure);
            byte[] decrypted = crypt.Decrypt(encrypted, EncryptMode.Secure);

            Assert.Equal(original, decrypted);
        }

        [Fact]
        public void EncryptDecrypt_EmptyData_RoundTrip()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] original = Array.Empty<byte>();

            byte[] encrypted = crypt.Encrypt(original, EncryptMode.Fast);
            byte[] decrypted = crypt.Decrypt(encrypted, EncryptMode.Fast);

            Assert.Equal(original, decrypted);
        }

        [Fact]
        public void DifferentKeys_ProduceDifferentEncryption()
        {
            using var crypt1 = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            using var crypt2 = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };

            byte[] encrypted1 = crypt1.Encrypt(data, EncryptMode.Fast);
            byte[] encrypted2 = crypt2.Encrypt(data, EncryptMode.Fast);

            Assert.NotEqual(encrypted1, encrypted2);
        }

        [Fact]
        public void EncryptDecrypt_SingleByte_RoundTrip()
        {
            using var crypt = new NetCrypt(NetCrypt.DefaultKeyLength, NetCrypt.DefaultFastKeyLength);
            byte[] original = new byte[] { 42 };

            byte[] encrypted = crypt.Encrypt(original, EncryptMode.Fast);
            byte[] decrypted = crypt.Decrypt(encrypted, EncryptMode.Fast);

            Assert.Equal(original, decrypted);
        }

        [Fact]
        public void EncryptDecrypt_128BitKey_WorksCorrectly()
        {
            using var crypt = new NetCrypt(128, 128);
            byte[] original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

            byte[] encrypted = crypt.Encrypt(original, EncryptMode.Secure);
            byte[] decrypted = crypt.Decrypt(encrypted, EncryptMode.Secure);

            Assert.Equal(128 / 8, crypt.GetKey().Length);
            Assert.Equal(original, decrypted);
        }

        [Fact]
        public void EncryptKey_DecryptKey_DifferentInstancesSameKey_RoundTrip()
        {
            byte[] sharedKey = new byte[32];
            for (int i = 0; i < sharedKey.Length; i++)
                sharedKey[i] = (byte)i;

            using var crypt1 = new NetCrypt(sharedKey);
            using var crypt2 = new NetCrypt(sharedKey);

            byte[] originalKey = new byte[16] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160 };

            byte[] encrypted = crypt1.EncryptKey(originalKey);
            byte[] decrypted = crypt2.DecryptKey(encrypted);

            Assert.Equal(originalKey, decrypted);
        }
    }
}
