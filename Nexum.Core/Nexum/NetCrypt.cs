using System;
using System.Buffers;
using System.Security.Cryptography;
using BaseLib;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace Nexum.Core
{
    internal class NetCrypt : IDisposable
    {
        internal const int DefaultKeyLength = 256;
        internal const int DefaultFastKeyLength = 512;
        private const int AesBlockSize = 16;

        private static readonly byte[] DefaultKey = new byte[16]
            { 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        private readonly object _aesLock = new object();
        private readonly object _rc4Lock = new object();

        private KeyParameter _aesKey;
        private KeyParameter _rc4Key;

        public NetCrypt(uint keySize, uint fastKeySize)
        {
            if (keySize == 0)
            {
                _aesKey = new KeyParameter(DefaultKey);
            }
            else
            {
                byte[] aesKey = new byte[keySize / 8];
                RandomNumberGenerator.Fill(aesKey);
                _aesKey = new KeyParameter(aesKey);
            }

            if (fastKeySize == 0)
            {
                _rc4Key = new KeyParameter(DefaultKey);
            }
            else
            {
                byte[] rc4Key = new byte[fastKeySize / 8];
                RandomNumberGenerator.Fill(rc4Key);
                _rc4Key = new KeyParameter(rc4Key);
            }
        }

        public NetCrypt(byte[] secureKey)
        {
            _aesKey = new KeyParameter(secureKey);
        }

        public void Dispose()
        {
            _aesKey = null;
            _rc4Key = null;
        }

        public void InitializeFastEncryption(byte[] key)
        {
            _rc4Key = new KeyParameter(key);
        }

        public byte[] GetKey()
        {
            return _aesKey?.GetKey();
        }

        public byte[] GetFastKey()
        {
            return _rc4Key?.GetKey();
        }

        public byte[] EncryptKey(byte[] key)
        {
            if (_aesKey == null)
                throw new ObjectDisposedException(GetType().FullName);

            lock (_aesLock)
            {
                var cipher = new PaddedBufferedBlockCipher(new AesEngine(), new Pkcs7Padding());
                cipher.Init(true, _aesKey);

                byte[] output = new byte[cipher.GetOutputSize(key.Length)];
                int len = cipher.ProcessBytes(key, 0, key.Length, output, 0);
                len += cipher.DoFinal(output, len);

                if (len == output.Length)
                    return output;

                byte[] result = GC.AllocateUninitializedArray<byte>(len);
                Buffer.BlockCopy(output, 0, result, 0, len);
                return result;
            }
        }

        public byte[] DecryptKey(byte[] encryptedKey)
        {
            if (_aesKey == null)
                throw new ObjectDisposedException(GetType().FullName);

            lock (_aesLock)
            {
                var cipher = new PaddedBufferedBlockCipher(new AesEngine(), new Pkcs7Padding());
                cipher.Init(false, _aesKey);

                byte[] output = new byte[cipher.GetOutputSize(encryptedKey.Length)];
                int len = cipher.ProcessBytes(encryptedKey, 0, encryptedKey.Length, output, 0);
                len += cipher.DoFinal(output, len);

                if (len == output.Length)
                    return output;

                byte[] result = GC.AllocateUninitializedArray<byte>(len);
                Buffer.BlockCopy(output, 0, result, 0, len);
                return result;
            }
        }

        public byte[] Encrypt(byte[] data, EncryptMode mode)
        {
            return Encrypt(data, data.Length, mode);
        }

        public byte[] Encrypt(byte[] data, int length, EncryptMode mode)
        {
            if (_aesKey == null)
                throw new ObjectDisposedException(GetType().FullName);

            switch (mode)
            {
                case EncryptMode.Secure:
                {
                    lock (_aesLock)
                    {
                        int contentSize = 1 + 4 + length;
                        int paddingNeeded = AesBlockSize - contentSize % AesBlockSize;
                        if (paddingNeeded == 0)
                            paddingNeeded = AesBlockSize;

                        int totalSize = contentSize + paddingNeeded;
                        uint crc32 = Hash.GetUInt32<CRC32>(data, 0, length);

                        byte[] plaintext = ArrayPool<byte>.Shared.Rent(totalSize);
                        try
                        {
                            plaintext[0] = (byte)paddingNeeded;
                            plaintext[1] = (byte)crc32;
                            plaintext[2] = (byte)(crc32 >> 8);
                            plaintext[3] = (byte)(crc32 >> 16);
                            plaintext[4] = (byte)(crc32 >> 24);
                            Buffer.BlockCopy(data, 0, plaintext, 5, length);
                            Array.Clear(plaintext, 5 + length, paddingNeeded);

                            var aes = new AesEngine();
                            aes.Init(true, _aesKey);

                            byte[] encrypted = GC.AllocateUninitializedArray<byte>(totalSize);
                            for (int i = 0; i < totalSize; i += AesBlockSize)
                                aes.ProcessBlock(plaintext, i, encrypted, i);

                            return encrypted;
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(plaintext);
                        }
                    }
                }

                case EncryptMode.Fast:
                {
                    lock (_rc4Lock)
                    {
                        byte[] encrypted = GC.AllocateUninitializedArray<byte>(length);

                        var rc4 = new RC4Engine();
                        rc4.Init(true, _rc4Key);
                        rc4.ProcessBytes(data, 0, length, encrypted, 0);

                        return encrypted;
                    }
                }

                default:
                    throw new ArgumentException("Invalid mode", nameof(mode));
            }
        }

        public byte[] Decrypt(byte[] data, EncryptMode mode)
        {
            return Decrypt(data, data.Length, mode);
        }

        public byte[] Decrypt(byte[] data, int length, EncryptMode mode)
        {
            if (_aesKey == null)
                throw new ObjectDisposedException(GetType().FullName);

            switch (mode)
            {
                case EncryptMode.Secure:
                {
                    lock (_aesLock)
                    {
                        byte[] decrypted = ArrayPool<byte>.Shared.Rent(length);
                        try
                        {
                            var aes = new AesEngine();
                            aes.Init(false, _aesKey);

                            for (int i = 0; i < length; i += AesBlockSize)
                                aes.ProcessBlock(data, i, decrypted, i);

                            byte paddingLen = decrypted[0];
                            int dataLen = length - 1 - 4 - paddingLen;
                            byte[] result = GC.AllocateUninitializedArray<byte>(dataLen);
                            Buffer.BlockCopy(decrypted, 5, result, 0, dataLen);
                            return result;
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(decrypted);
                        }
                    }
                }

                case EncryptMode.Fast:
                {
                    lock (_rc4Lock)
                    {
                        byte[] decrypted = GC.AllocateUninitializedArray<byte>(length);

                        var rc4 = new RC4Engine();
                        rc4.Init(false, _rc4Key);
                        rc4.ProcessBytes(data, 0, length, decrypted, 0);

                        return decrypted;
                    }
                }

                default:
                    throw new ArgumentException("Invalid mode", nameof(mode));
            }
        }
    }
}
