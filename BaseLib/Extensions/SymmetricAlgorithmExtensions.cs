using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace BaseLib.Extensions
{
    public static class SymmetricAlgorithmExtensions
    {
        public static byte[] Encrypt(this SymmetricAlgorithm source, byte[] buffer)
        {
            using (var transform = source.CreateEncryptor())
            {
                using (var memoryStream = new MemoryStream())
                {
                    using (var cryptoStream = new CryptoStream(memoryStream, transform, CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(buffer, 0, buffer.Length);
                        cryptoStream.Close();
                        return memoryStream.ToArray();
                    }
                }
            }
        }

        public static byte[] Encrypt(this SymmetricAlgorithm source, Stream buffer)
        {
            using (var transform = source.CreateEncryptor())
            {
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, transform, CryptoStreamMode.Write))
                    {
                        buffer.CopyTo(cs);
                        cs.Flush();
                        return ms.ToArray();
                    }
                }
            }
        }

        public static async Task<byte[]> EncryptAsync(this SymmetricAlgorithm source, byte[] buffer)
        {
            using (var encryptor = source.CreateEncryptor())
            {
                using (var stream = new MemoryStream())
                {
                    await using (var cs = new CryptoStream(stream, encryptor, CryptoStreamMode.Write))
                    {
                        await cs.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        cs.Close();
                        return stream.ToArray();
                    }
                }
            }
        }

        public static async Task<byte[]> EncryptAsync(this SymmetricAlgorithm source, Stream buffer)
        {
            using (var transform = source.CreateEncryptor())
            {
                using (var ms = new MemoryStream())
                {
                    await using (var cs = new CryptoStream(ms, transform, CryptoStreamMode.Write))
                    {
                        await buffer.CopyToAsync(cs).ConfigureAwait(false);
                        await cs.FlushAsync().ConfigureAwait(false);
                        return ms.ToArray();
                    }
                }
            }
        }

        public static byte[] Decrypt(this SymmetricAlgorithm source, byte[] buffer)
        {
            using (var transform = source.CreateDecryptor())
            {
                using (var stream = new MemoryStream(buffer))
                {
                    using (var stream2 = new CryptoStream(stream, transform, CryptoStreamMode.Read))
                    {
                        return stream2.ReadToEnd();
                    }
                }
            }
        }

        public static byte[] Decrypt(this SymmetricAlgorithm source, Stream buffer)
        {
            using (var transform = source.CreateDecryptor())
            {
                using (var cs = new CryptoStream(buffer, transform, CryptoStreamMode.Read))
                {
                    return cs.ReadToEnd();
                }
            }
        }

        public static async Task<byte[]> DecryptAsync(this SymmetricAlgorithm source, byte[] buffer)
        {
            using (var decryptor = source.CreateDecryptor())
            {
                using (var stream = new MemoryStream(buffer))
                {
                    await using (var cs = new CryptoStream(stream, decryptor, CryptoStreamMode.Read))
                    {
                        return await cs.ReadToEndAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        public static async Task<byte[]> DecryptAsync(this SymmetricAlgorithm source, Stream buffer)
        {
            using (var transform = source.CreateDecryptor())
            {
                await using (var cs = new CryptoStream(buffer, transform, CryptoStreamMode.Read))
                {
                    return await cs.ReadToEndAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
