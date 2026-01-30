using System;
using System.Buffers;
using System.Security.Cryptography;
using BaseLib.Hashing;
using Nexum.Core.Configuration;
using Nexum.Core.Message.X2X;
using Nexum.Core.Serialization;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace Nexum.Core.Crypto
{
    internal sealed class NetCrypt : IDisposable
    {
        internal const int DefaultKeyLength = 256;
        internal const int DefaultFastKeyLength = 512;
        private const int AesBlockSize = 16;

        private static readonly byte[] DefaultKey = new byte[16]
            { 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

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
            return new EncryptedMessage
            {
                EncryptMode = data.EncryptMode,
                EncryptedData = new ByteArray(encryptedBuffer, true)
            }.Serialize();
        }

        internal byte[] GetKey()
        {
            return _aesKey?.GetKey();
        }

        internal byte[] GetFastKey()
        {
            return _rc4Key?.GetKey();
        }

        internal byte[] EncryptKey(ReadOnlySpan<byte> key)
        {
            var aesKey = _aesKey;
            if (aesKey == null)
                throw new ObjectDisposedException(GetType().FullName);

            var cipher = new PaddedBufferedBlockCipher(new AesEngine(), new Pkcs7Padding());
            cipher.Init(true, aesKey);

            int outputSize = cipher.GetOutputSize(key.Length);
            byte[] output = GC.AllocateUninitializedArray<byte>(outputSize);
            int len = cipher.ProcessBytes(key, output.AsSpan());
            len += cipher.DoFinal(output, len);

            if (len == output.Length)
                return output;

            byte[] result = GC.AllocateUninitializedArray<byte>(len);
            Buffer.BlockCopy(output, 0, result, 0, len);
            return result;
        }

        internal byte[] DecryptKey(ReadOnlySpan<byte> encryptedKey)
        {
            var aesKey = _aesKey;
            if (aesKey == null)
                throw new ObjectDisposedException(GetType().FullName);

            var cipher = new PaddedBufferedBlockCipher(new AesEngine(), new Pkcs7Padding());
            cipher.Init(false, aesKey);

            int outputSize = cipher.GetOutputSize(encryptedKey.Length);
            byte[] output = GC.AllocateUninitializedArray<byte>(outputSize);
            int len = cipher.ProcessBytes(encryptedKey, output.AsSpan());
            len += cipher.DoFinal(output, len);

            if (len == output.Length)
                return output;

            byte[] result = GC.AllocateUninitializedArray<byte>(len);
            Buffer.BlockCopy(output, 0, result, 0, len);
            return result;
        }

        internal byte[] Encrypt(ReadOnlySpan<byte> data, EncryptMode mode)
        {
            switch (mode)
            {
                case EncryptMode.Secure:
                {
                    var aesKey = _aesKey;
                    if (aesKey == null)
                        throw new ObjectDisposedException(GetType().FullName);

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

                        var aesEngine = new AesEngine();
                        aesEngine.Init(true, aesKey);

                        byte[] encrypted = GC.AllocateUninitializedArray<byte>(totalSize);
                        for (int i = 0; i < totalSize; i += AesBlockSize)
                            aesEngine.ProcessBlock(plaintext, i, encrypted, i);

                        return encrypted;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(plaintext);
                    }
                }

                case EncryptMode.Fast:
                {
                    var rc4Key = _rc4Key;
                    if (rc4Key == null)
                        throw new ObjectDisposedException(GetType().FullName);

                    byte[] encrypted = GC.AllocateUninitializedArray<byte>(data.Length);

                    var rc4Engine = new RC4Engine();
                    rc4Engine.Init(true, rc4Key);
                    rc4Engine.ProcessBytes(data, encrypted.AsSpan());

                    return encrypted;
                }

                default:
                    throw new ArgumentException("Invalid mode", nameof(mode));
            }
        }

        internal byte[] Decrypt(ReadOnlySpan<byte> data, EncryptMode mode)
        {
            switch (mode)
            {
                case EncryptMode.Secure:
                {
                    var aesKey = _aesKey;
                    if (aesKey == null)
                        throw new ObjectDisposedException(GetType().FullName);

                    byte[] decrypted = ArrayPool<byte>.Shared.Rent(data.Length);
                    try
                    {
                        var aesEngine = new AesEngine();
                        aesEngine.Init(false, aesKey);

                        for (int i = 0; i < data.Length; i += AesBlockSize)
                            aesEngine.ProcessBlock(data.Slice(i, AesBlockSize), decrypted.AsSpan(i, AesBlockSize));

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

                case EncryptMode.Fast:
                {
                    var rc4Key = _rc4Key;
                    if (rc4Key == null)
                        throw new ObjectDisposedException(GetType().FullName);

                    byte[] decrypted = GC.AllocateUninitializedArray<byte>(data.Length);

                    var rc4Engine = new RC4Engine();
                    rc4Engine.Init(false, rc4Key);
                    rc4Engine.ProcessBytes(data, decrypted.AsSpan());

                    return decrypted;
                }

                default:
                    throw new ArgumentException("Invalid mode", nameof(mode));
            }
        }
    }
}
