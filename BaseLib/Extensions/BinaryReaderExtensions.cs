using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace BaseLib.Extensions
{
    public static class BinaryReaderExtensions
    {
        public static byte[] ReadToEnd(this BinaryReader r)
        {
            return r.BaseStream.ReadToEnd();
        }

        public static Task<byte[]> ReadToEndAsync(this BinaryReader r)
        {
            return r.BaseStream.ReadToEndAsync();
        }

        public static T Read<T>(this BinaryReader r) where T : struct, IComparable, IConvertible
        {
            var typeFromHandle = typeof(T);
            switch (Type.GetTypeCode(typeFromHandle))
            {
                case TypeCode.Boolean:
                    return (T)(object)r.ReadBoolean();
                case TypeCode.Char:
                    return (T)(object)r.ReadChar();
                case TypeCode.Byte:
                    return (T)(object)r.ReadByte();
                case TypeCode.SByte:
                    return (T)(object)r.ReadSByte();
                case TypeCode.Int16:
                    return (T)(object)r.ReadInt16();
                case TypeCode.Int32:
                    return (T)(object)r.ReadInt32();
                case TypeCode.Int64:
                    return (T)(object)r.ReadInt64();
                case TypeCode.UInt16:
                    return (T)(object)r.ReadUInt16();
                case TypeCode.UInt32:
                    return (T)(object)r.ReadUInt32();
                case TypeCode.UInt64:
                    return (T)(object)r.ReadUInt64();
                case TypeCode.Single:
                    return (T)(object)r.ReadSingle();
                case TypeCode.Double:
                    return (T)(object)r.ReadDouble();
                case TypeCode.Decimal:
                    return (T)(object)r.ReadDecimal();
                case TypeCode.String:
                    return (T)(object)r.ReadString();
                default:
                    throw new NotSupportedException("Type is not supported");
            }
        }

        public static T[] ReadArray<T>(this BinaryReader r, int count) where T : struct, IComparable, IConvertible
        {
            return r.ReadArray<T>((uint)count);
        }

        public static T[] ReadArray<T>(this BinaryReader r, uint count) where T : struct, IComparable, IConvertible
        {
            var array = new T[count];
            for (int i = 0; i < count; i++)
                array[i] = r.Read<T>();
            return array;
        }

        public static T ReadEnum<T>(this BinaryReader r) where T : struct, IComparable, IConvertible
        {
            var typeFromHandle = typeof(T);
            if (!typeFromHandle.IsEnum)
                throw new ArgumentException("T is not an enum");
            var underlyingType = Enum.GetUnderlyingType(typeFromHandle);
            switch (Type.GetTypeCode(underlyingType))
            {
                case TypeCode.Boolean:
                    return (T)(object)r.ReadBoolean();
                case TypeCode.Char:
                    return (T)(object)r.ReadChar();
                case TypeCode.Byte:
                    return (T)(object)r.ReadByte();
                case TypeCode.SByte:
                    return (T)(object)r.ReadSByte();
                case TypeCode.Int16:
                    return (T)(object)r.ReadInt16();
                case TypeCode.Int32:
                    return (T)(object)r.ReadInt32();
                case TypeCode.Int64:
                    return (T)(object)r.ReadInt64();
                case TypeCode.UInt16:
                    return (T)(object)r.ReadUInt16();
                case TypeCode.UInt32:
                    return (T)(object)r.ReadUInt32();
                case TypeCode.UInt64:
                    return (T)(object)r.ReadUInt64();
                case TypeCode.Single:
                    return (T)(object)r.ReadSingle();
                case TypeCode.Double:
                    return (T)(object)r.ReadDouble();
                case TypeCode.Decimal:
                    return (T)(object)r.ReadDecimal();
                default:
                    throw new NotSupportedException("Type is not supported");
            }
        }

        public static IPEndPoint ReadIPEndPoint(this BinaryReader r)
        {
            var address = new IPAddress(r.ReadBytes(4));
            return new IPEndPoint(address, r.ReadUInt16());
        }
    }
}
