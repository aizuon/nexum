using System;
using System.Buffers;
using System.Security.Cryptography;
using BaseLib;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace Nexum.Core
{
    internal sealed class NetCrypt : IDisposable
    {
        internal const int DefaultKeyLength = 256;
        internal const int DefaultFastKeyLength = 512;
        private const int AesBlockSize = 16;

        private static readonly byte[] DefaultKey = new byte[16]
            { 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        // Reuse engines to avoid per-packet allocations (protected by locks above).
        private readonly AesEngine _aesEngine = new AesEngine();

        private readonly object _aesLock = new object();
        private readonly RC4Engine _rc4Engine = new RC4Engine();
        private readonly object _rc4Lock = new object();

        private KeyParameter _aesKey;
        private KeyParameter _rc4Key;

        internal NetCrypt(uint keySize, uint fastKeySize)
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

        internal NetCrypt(byte[] secureKey)
        {
            _aesKey = new KeyParameter(secureKey);
        }

        public void Dispose()
        {
            _aesKey = null;
            _rc4Key = null;
        }

        internal void InitializeFastEncryption(byte[] key)
        {
            _rc4Key = new KeyParameter(key);
        }

        internal NetMessage CreateEncryptedMessage(NetMessage data)
        {
            byte[] encryptedBuffer = Encrypt(data.GetBufferSpan(), data.EncryptMode);
            var encryptedMessage = new NetMessage();
            encryptedMessage.Write(MessageType.Encrypted);
            encryptedMessage.Write(data.EncryptMode);
            encryptedMessage.Write(new ByteArray(encryptedBuffer, true));
            return encryptedMessage;
        }

        internal byte[] GetKey()
        {
            return _aesKey?.GetKey();
        }

        internal byte[] GetFastKey()
        {
            return _rc4Key?.GetKey();
        }

        internal byte[] EncryptKey(byte[] key)
        {
            if (_aesKey == null)
                throw new ObjectDisposedException(GetType().FullName);

            lock (_aesLock)
            {
                var cipher = new PaddedBufferedBlockCipher(new AesEngine(), new Pkcs7Padding());
                cipher.Init(true, _aesKey);

                int outputSize = cipher.GetOutputSize(key.Length);
                byte[] output = GC.AllocateUninitializedArray<byte>(outputSize);
                int len = cipher.ProcessBytes(key, 0, key.Length, output, 0);
                len += cipher.DoFinal(output, len);

                if (len == output.Length)
                    return output;

                byte[] result = GC.AllocateUninitializedArray<byte>(len);
                Buffer.BlockCopy(output, 0, result, 0, len);
                return result;
            }
        }

        internal byte[] DecryptKey(byte[] encryptedKey)
        {
            if (_aesKey == null)
                throw new ObjectDisposedException(GetType().FullName);

            lock (_aesLock)
            {
                var cipher = new PaddedBufferedBlockCipher(new AesEngine(), new Pkcs7Padding());
                cipher.Init(false, _aesKey);

                int outputSize = cipher.GetOutputSize(encryptedKey.Length);
                byte[] output = GC.AllocateUninitializedArray<byte>(outputSize);
                int len = cipher.ProcessBytes(encryptedKey, 0, encryptedKey.Length, output, 0);
                len += cipher.DoFinal(output, len);

                if (len == output.Length)
                    return output;

                byte[] result = GC.AllocateUninitializedArray<byte>(len);
                Buffer.BlockCopy(output, 0, result, 0, len);
                return result;
            }
        }

        internal byte[] Encrypt(byte[] data, EncryptMode mode)
        {
            return Encrypt(data.AsSpan(), mode);
        }

        internal byte[] Encrypt(ReadOnlySpan<byte> data, EncryptMode mode)
        {
            if (_aesKey == null)
                throw new ObjectDisposedException(GetType().FullName);

            switch (mode)
            {
                case EncryptMode.Secure:
                {
                    lock (_aesLock)
                    {
                        int contentSize = 1 + 4 + data.Length;
                        int paddingNeeded = AesBlockSize - contentSize % AesBlockSize;
                        if (paddingNeeded == 0)
                            paddingNeeded = AesBlockSize;

                        int totalSize = contentSize + paddingNeeded;
                        uint crc32 = Hash.GetUInt32<CRC32>(data);

                        byte[] plaintext = ArrayPool<byte>.Shared.Rent(totalSize);
                        try
                        {
                            plaintext[0] = (byte)paddingNeeded;
                            plaintext[1] = (byte)crc32;
                            plaintext[2] = (byte)(crc32 >> 8);
                            plaintext[3] = (byte)(crc32 >> 16);
                            plaintext[4] = (byte)(crc32 >> 24);
                            data.CopyTo(plaintext.AsSpan(5));
                            Array.Clear(plaintext, 5 + data.Length, paddingNeeded);

                            _aesEngine.Init(true, _aesKey);

                            byte[] encrypted = GC.AllocateUninitializedArray<byte>(totalSize);
                            for (int i = 0; i < totalSize; i += AesBlockSize)
                                _aesEngine.ProcessBlock(plaintext, i, encrypted, i);

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
                        byte[] encrypted = GC.AllocateUninitializedArray<byte>(data.Length);

                        _rc4Engine.Init(true, _rc4Key);
                        _rc4Engine.ProcessBytes(data, encrypted.AsSpan());

                        return encrypted;
                    }
                }

                default:
                    throw new ArgumentException("Invalid mode", nameof(mode));
            }
        }

        internal byte[] Decrypt(byte[] data, EncryptMode mode)
        {
            return Decrypt(data.AsSpan(), mode);
        }

        internal byte[] Decrypt(ReadOnlySpan<byte> data, EncryptMode mode)
        {
            if (_aesKey == null)
                throw new ObjectDisposedException(GetType().FullName);

            switch (mode)
            {
                case EncryptMode.Secure:
                {
                    lock (_aesLock)
                    {
                        byte[] decrypted = ArrayPool<byte>.Shared.Rent(data.Length);
                        try
                        {
                            _aesEngine.Init(false, _aesKey);

                            for (int i = 0; i < data.Length; i += AesBlockSize)
                                _aesEngine.ProcessBlock(data.Slice(i, AesBlockSize), decrypted.AsSpan(i, AesBlockSize));

                            byte paddingLen = decrypted[0];
                            int dataLen = data.Length - 1 - 4 - paddingLen;
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
                        byte[] decrypted = GC.AllocateUninitializedArray<byte>(data.Length);

                        _rc4Engine.Init(false, _rc4Key);
                        _rc4Engine.ProcessBytes(data, decrypted.AsSpan());

                        return decrypted;
                    }
                }

                default:
                    throw new ArgumentException("Invalid mode", nameof(mode));
            }
        }
    }
}
