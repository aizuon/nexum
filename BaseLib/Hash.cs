using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BaseLib
{
    public static class Hash
    {
        public static byte[] GetBytes<T>(Stream inputStream) where T : HashAlgorithm, new()
        {
            using var val = new T();
            return val.ComputeHash(inputStream);
        }

        public static byte[] GetBytes<T>(byte[] data, int offset, int count) where T : HashAlgorithm, new()
        {
            using var val = new T();
            return val.ComputeHash(data, offset, count);
        }

        public static byte[] GetBytes<T>(byte[] data) where T : HashAlgorithm, new()
        {
            return GetBytes<T>(data, 0, data.Length);
        }

        public static byte[] GetBytes<T>(string data, Encoding encoding = null) where T : HashAlgorithm, new()
        {
            encoding ??= Encoding.ASCII;
            return GetBytes<T>(encoding.GetBytes(data));
        }

        public static string GetString<T>(Stream inputStream) where T : HashAlgorithm, new()
        {
            using var val = new T();
            byte[] value = val.ComputeHash(inputStream);
            return Convert.ToHexStringLower(value);
        }

        public static string GetString<T>(byte[] data, int offset, int count) where T : HashAlgorithm, new()
        {
            using var val = new T();
            byte[] value = val.ComputeHash(data, offset, count);
            return Convert.ToHexStringLower(value);
        }

        public static string GetString<T>(byte[] data) where T : HashAlgorithm, new()
        {
            return GetString<T>(data, 0, data.Length);
        }

        public static string GetString<T>(string data, Encoding encoding = null) where T : HashAlgorithm, new()
        {
            encoding ??= Encoding.ASCII;
            return GetString<T>(encoding.GetBytes(data));
        }

        public static ushort GetUInt16<T>(Stream inputStream) where T : HashAlgorithm, new()
        {
            using var val = new T();
            byte[] value = val.ComputeHash(inputStream);
            return BinaryPrimitives.ReadUInt16LittleEndian(value);
        }

        public static ushort GetUInt16<T>(byte[] data, int offset, int count) where T : HashAlgorithm, new()
        {
            using var val = new T();
            byte[] value = val.ComputeHash(data, offset, count);
            return BinaryPrimitives.ReadUInt16LittleEndian(value);
        }

        public static ushort GetUInt16<T>(byte[] data) where T : HashAlgorithm, new()
        {
            return GetUInt16<T>(data, 0, data.Length);
        }

        public static ushort GetUInt16<T>(string data, Encoding encoding = null) where T : HashAlgorithm, new()
        {
            encoding ??= Encoding.ASCII;
            return GetUInt16<T>(encoding.GetBytes(data));
        }

        public static uint GetUInt32<T>(Stream inputStream) where T : HashAlgorithm, new()
        {
            using var val = new T();
            byte[] value = val.ComputeHash(inputStream);
            return BinaryPrimitives.ReadUInt32LittleEndian(value);
        }

        public static uint GetUInt32<T>(byte[] data, int offset, int count) where T : HashAlgorithm, new()
        {
            using var val = new T();
            byte[] value = val.ComputeHash(data, offset, count);
            return BinaryPrimitives.ReadUInt32LittleEndian(value);
        }

        public static uint GetUInt32<T>(byte[] data) where T : HashAlgorithm, new()
        {
            return GetUInt32<T>(data, 0, data.Length);
        }

        public static uint GetUInt32<T>(string data, Encoding encoding = null) where T : HashAlgorithm, new()
        {
            encoding ??= Encoding.ASCII;
            return GetUInt32<T>(encoding.GetBytes(data));
        }

        public static ulong GetUInt64<T>(Stream inputStream) where T : HashAlgorithm, new()
        {
            using var val = new T();
            byte[] value = val.ComputeHash(inputStream);
            return BinaryPrimitives.ReadUInt64LittleEndian(value);
        }

        public static ulong GetUInt64<T>(byte[] data, int offset, int count) where T : HashAlgorithm, new()
        {
            using var val = new T();
            byte[] value = val.ComputeHash(data, offset, count);
            return BinaryPrimitives.ReadUInt64LittleEndian(value);
        }

        public static ulong GetUInt64<T>(byte[] data) where T : HashAlgorithm, new()
        {
            return GetUInt64<T>(data, 0, data.Length);
        }

        public static ulong GetUInt64<T>(string data, Encoding encoding = null) where T : HashAlgorithm, new()
        {
            encoding ??= Encoding.ASCII;
            return GetUInt64<T>(encoding.GetBytes(data));
        }
    }
}
