using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace BaseLib.Extensions
{
    public static class BinaryWriterExtensions
    {
        public static void Write<T>(this BinaryWriter w, IConvertible value) where T : struct, IComparable, IConvertible
        {
            var typeFromHandle = typeof(T);
            object obj = value;
            if (typeFromHandle != value.GetType())
                obj = Convert.ChangeType(obj, typeFromHandle);
            switch (Type.GetTypeCode(typeFromHandle))
            {
                case TypeCode.Boolean:
                    w.Write((bool)obj);
                    break;
                case TypeCode.Char:
                    w.Write((char)obj);
                    break;
                case TypeCode.Byte:
                    w.Write((byte)obj);
                    break;
                case TypeCode.SByte:
                    w.Write((sbyte)obj);
                    break;
                case TypeCode.Int16:
                    w.Write((short)obj);
                    break;
                case TypeCode.Int32:
                    w.Write((int)obj);
                    break;
                case TypeCode.Int64:
                    w.Write((long)obj);
                    break;
                case TypeCode.UInt16:
                    w.Write((ushort)obj);
                    break;
                case TypeCode.UInt32:
                    w.Write((uint)obj);
                    break;
                case TypeCode.UInt64:
                    w.Write((ulong)obj);
                    break;
                case TypeCode.Single:
                    w.Write((float)obj);
                    break;
                case TypeCode.Double:
                    w.Write((double)obj);
                    break;
                case TypeCode.Decimal:
                    w.Write((decimal)obj);
                    break;
                case TypeCode.String:
                    w.Write((string)obj);
                    break;
                default:
                    throw new NotSupportedException("Type is not supported");
            }
        }

        public static void Write<T>(this BinaryWriter w, IEnumerable<T> values)
            where T : struct, IComparable, IConvertible
        {
            foreach (var value in values)
                w.Write<T>(value);
        }

        public static void WriteEnum<T>(this BinaryWriter w, T value) where T : struct, IComparable, IConvertible
        {
            var typeFromHandle = typeof(T);
            if (!typeFromHandle.IsEnum)
                throw new ArgumentException("T is not an enum");
            var underlyingType = Enum.GetUnderlyingType(typeFromHandle);
            switch (Type.GetTypeCode(underlyingType))
            {
                case TypeCode.Boolean:
                    w.Write<bool>(value);
                    break;
                case TypeCode.Char:
                    w.Write<char>(value);
                    break;
                case TypeCode.Byte:
                    w.Write<byte>(value);
                    break;
                case TypeCode.SByte:
                    w.Write<sbyte>(value);
                    break;
                case TypeCode.Int16:
                    w.Write<short>(value);
                    break;
                case TypeCode.Int32:
                    w.Write<int>(value);
                    break;
                case TypeCode.Int64:
                    w.Write<long>(value);
                    break;
                case TypeCode.UInt16:
                    w.Write<ushort>(value);
                    break;
                case TypeCode.UInt32:
                    w.Write<uint>(value);
                    break;
                case TypeCode.UInt64:
                    w.Write<ulong>(value);
                    break;
                case TypeCode.Single:
                    w.Write<float>(value);
                    break;
                case TypeCode.Double:
                    w.Write<double>(value);
                    break;
                case TypeCode.Decimal:
                    w.Write<decimal>(value);
                    break;
                default:
                    throw new NotSupportedException("Type is not supported");
            }
        }

        public static void Write(this BinaryWriter w, IPEndPoint value)
        {
            w.Write(value.Address.GetAddressBytes());
            w.Write<ushort>(value.Port);
        }

        public static byte[] ToArray(this BinaryWriter w)
        {
            var memoryStream = w.BaseStream as MemoryStream;
            if (memoryStream == null)
                throw new InvalidOperationException("BaseStream must be a MemoryStream");
            return memoryStream.ToArray();
        }

        public static BinaryWriter Create()
        {
            return new BinaryWriter(new MemoryStream());
        }

        public static void Fill(this BinaryWriter @this, int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (count == 0)
                return;

            const int stackAllocThreshold = 256;
            if (count <= stackAllocThreshold)
            {
                Span<byte> buffer = stackalloc byte[count];
                buffer.Clear();
                @this.Write(buffer);
            }
            else
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(count);
                try
                {
                    Array.Clear(buffer, 0, count);
                    @this.Write(buffer, 0, count);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        public static bool IsEOF(this BinaryWriter @this)
        {
            return @this.BaseStream.IsEOF();
        }
    }
}
