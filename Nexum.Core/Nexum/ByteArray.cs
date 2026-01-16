using System;
using System.Runtime.CompilerServices;
using BaseLib.Extensions;

namespace Nexum.Core
{
    public class ByteArray
    {
        private byte[] _buffer = Array.Empty<byte>();

        public ByteArray()
        {
        }

        public ByteArray(ByteArray data)
        {
            _buffer = data._buffer;
        }

        public ByteArray(byte[] data)
        {
            _buffer = data.FastClone();
            WriteOffset = _buffer.Length;
        }

        public ByteArray(byte[] data, int length)
        {
            _buffer = GC.AllocateUninitializedArray<byte>(length);
            Buffer.BlockCopy(data, 0, _buffer, 0, length);
        }

        public int Length => _buffer.Length;

        public int ReadOffset { get; set; }

        public int WriteOffset { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte[] GetBuffer()
        {
            byte[] result = GC.AllocateUninitializedArray<byte>(_buffer.Length);
            Buffer.BlockCopy(_buffer, 0, result, 0, _buffer.Length);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> GetBufferSpan()
        {
            return _buffer.AsSpan(0, WriteOffset > 0 ? WriteOffset : _buffer.Length);
        }

        public virtual void Shrink()
        {
            int newLength = _buffer.Length - ReadOffset;
            byte[] newBuffer = GC.AllocateUninitializedArray<byte>(newLength);
            Buffer.BlockCopy(_buffer, ReadOffset, newBuffer, 0, newLength);
            _buffer = newBuffer;
            ReadOffset = 0;
            WriteOffset = newLength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(byte[] obj)
        {
            EnsureCapacity(WriteOffset + obj.Length);
            Buffer.BlockCopy(obj, 0, _buffer, WriteOffset, obj.Length);
            WriteOffset += obj.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(int requiredCapacity)
        {
            if (_buffer.Length < requiredCapacity)
            {
                int newCapacity = Math.Max(_buffer.Length * 2, requiredCapacity);
                byte[] newBuffer = GC.AllocateUninitializedArray<byte>(newCapacity);
                Buffer.BlockCopy(_buffer, 0, newBuffer, 0, WriteOffset);
                _buffer = newBuffer;
            }
        }

        public void WriteScalar(byte obj)
        {
            WriteScalar((long)obj);
        }

        public void WriteScalar(short obj)
        {
            WriteScalar((long)obj);
        }

        public void WriteScalar(int obj)
        {
            WriteScalar((long)obj);
        }

        public void WriteScalar(long obj)
        {
            if (sbyte.MinValue <= obj && obj <= sbyte.MaxValue)
            {
                Write((sbyte)1);
                Write((sbyte)obj);
            }
            else if (short.MinValue <= obj && obj <= short.MaxValue)
            {
                Write((sbyte)2);
                Write((short)obj);
            }
            else if (int.MinValue <= obj && obj <= int.MaxValue)
            {
                Write((sbyte)4);
                Write((int)obj);
            }
            else
            {
                Write((sbyte)8);
                Write(obj);
            }
        }

        public void Write(bool obj)
        {
            if (obj)
                Write((byte)1);
            else
                Write((byte)0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(byte obj)
        {
            EnsureCapacity(WriteOffset + 1);
            _buffer[WriteOffset++] = obj;
        }

        public void Write(sbyte obj)
        {
            Write((byte)obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(short obj)
        {
            EnsureCapacity(WriteOffset + 2);
            Unsafe.WriteUnaligned(ref _buffer[WriteOffset], obj);
            WriteOffset += 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(ushort obj)
        {
            EnsureCapacity(WriteOffset + 2);
            Unsafe.WriteUnaligned(ref _buffer[WriteOffset], obj);
            WriteOffset += 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(int obj)
        {
            EnsureCapacity(WriteOffset + 4);
            Unsafe.WriteUnaligned(ref _buffer[WriteOffset], obj);
            WriteOffset += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(uint obj)
        {
            EnsureCapacity(WriteOffset + 4);
            Unsafe.WriteUnaligned(ref _buffer[WriteOffset], obj);
            WriteOffset += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(float obj)
        {
            EnsureCapacity(WriteOffset + 4);
            Unsafe.WriteUnaligned(ref _buffer[WriteOffset], obj);
            WriteOffset += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(long obj)
        {
            EnsureCapacity(WriteOffset + 8);
            Unsafe.WriteUnaligned(ref _buffer[WriteOffset], obj);
            WriteOffset += 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(ulong obj)
        {
            EnsureCapacity(WriteOffset + 8);
            Unsafe.WriteUnaligned(ref _buffer[WriteOffset], obj);
            WriteOffset += 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(double obj)
        {
            EnsureCapacity(WriteOffset + 8);
            Unsafe.WriteUnaligned(ref _buffer[WriteOffset], obj);
            WriteOffset += 8;
        }

        public void Write(ByteArray obj)
        {
            WriteScalar(obj.Length);
            Write(obj._buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteEnum<T>(T value) where T : struct, Enum
        {
            if (Unsafe.SizeOf<T>() == 1)
                Write(Unsafe.As<T, byte>(ref value));
            else if (Unsafe.SizeOf<T>() == 2)
                Write(Unsafe.As<T, short>(ref value));
            else if (Unsafe.SizeOf<T>() == 4)
                Write(Unsafe.As<T, int>(ref value));
            else if (Unsafe.SizeOf<T>() == 8)
                Write(Unsafe.As<T, long>(ref value));
            else
                throw new NotSupportedException("Enum size is not supported");
        }

        public bool Read(ref ByteArray obj)
        {
            long length = 0;
            if (ReadScalar(ref length))
            {
                byte[] data = GC.AllocateUninitializedArray<byte>((int)length);
                if (Read(ref data, data.Length))
                {
                    obj = new ByteArray(data, data.Length);
                    return true;
                }
            }

            return false;
        }

        public bool Read(ref ByteArray obj, int length)
        {
            byte[] data = GC.AllocateUninitializedArray<byte>(length);
            if (!Read(ref data, length))
                return false;
            obj = new ByteArray(data, length);
            return true;
        }

        public bool ReadBytes(out byte[] obj, int length)
        {
            obj = GC.AllocateUninitializedArray<byte>(length);
            return Read(ref obj, length);
        }

        public bool ReadAll(out byte[] obj)
        {
            int length = _buffer.Length - ReadOffset;
            obj = new byte[length];

            return Read(ref obj, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(ref byte[] obj, int length)
        {
            if (_buffer.Length >= ReadOffset + length)
            {
                byte[] result = GC.AllocateUninitializedArray<byte>(length);
                Buffer.BlockCopy(_buffer, ReadOffset, result, 0, length);
                obj = result;
                ReadOffset += length;
                return true;
            }

            int remaining = _buffer.Length - ReadOffset;
            byte[] partial = GC.AllocateUninitializedArray<byte>(remaining);
            Buffer.BlockCopy(_buffer, ReadOffset, partial, 0, remaining);
            obj = partial;
            ReadOffset += remaining;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(ref bool obj)
        {
            if (_buffer.Length <= ReadOffset)
                return false;
            obj = _buffer[ReadOffset++] == 1;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(ref byte obj)
        {
            if (_buffer.Length <= ReadOffset)
                return false;
            obj = _buffer[ReadOffset++];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(ref sbyte obj)
        {
            if (_buffer.Length <= ReadOffset)
                return false;
            obj = (sbyte)_buffer[ReadOffset++];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(ref short obj)
        {
            if (_buffer.Length < ReadOffset + 2)
                return false;
            obj = Unsafe.ReadUnaligned<short>(ref _buffer[ReadOffset]);
            ReadOffset += 2;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(ref ushort obj)
        {
            if (_buffer.Length < ReadOffset + 2)
                return false;
            obj = Unsafe.ReadUnaligned<ushort>(ref _buffer[ReadOffset]);
            ReadOffset += 2;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(ref float obj)
        {
            if (_buffer.Length < ReadOffset + 4)
                return false;
            obj = Unsafe.ReadUnaligned<float>(ref _buffer[ReadOffset]);
            ReadOffset += 4;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(ref int obj)
        {
            if (_buffer.Length < ReadOffset + 4)
                return false;
            obj = Unsafe.ReadUnaligned<int>(ref _buffer[ReadOffset]);
            ReadOffset += 4;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(ref uint obj)
        {
            if (_buffer.Length < ReadOffset + 4)
                return false;
            obj = Unsafe.ReadUnaligned<uint>(ref _buffer[ReadOffset]);
            ReadOffset += 4;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(ref double obj)
        {
            if (_buffer.Length < ReadOffset + 8)
                return false;
            obj = Unsafe.ReadUnaligned<double>(ref _buffer[ReadOffset]);
            ReadOffset += 8;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(ref ulong obj)
        {
            if (_buffer.Length < ReadOffset + 8)
                return false;
            obj = Unsafe.ReadUnaligned<ulong>(ref _buffer[ReadOffset]);
            ReadOffset += 8;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(ref long obj)
        {
            if (_buffer.Length < ReadOffset + 8)
                return false;
            obj = Unsafe.ReadUnaligned<long>(ref _buffer[ReadOffset]);
            ReadOffset += 8;
            return true;
        }

        public bool ReadScalar(ref long obj)
        {
            sbyte num1 = 0;
            short num2 = 0;
            int num3 = 0;
            long num4 = 0;
            byte num5 = 0;
            if (!Read(ref num5))
                return false;
            switch (num5)
            {
                case 1:
                    if (!Read(ref num1))
                        return false;
                    obj = num1;
                    break;
                case 2:
                    if (!Read(ref num2))
                        return false;
                    obj = num2;
                    break;
                case 4:
                    if (!Read(ref num3))
                        return false;
                    obj = num3;
                    break;
                case 8:
                    if (!Read(ref num4))
                        return false;
                    obj = num4;
                    break;
                default:
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteAt(int offset, byte[] bytes)
        {
            if (offset + bytes.Length > _buffer.Length)
                return;
            Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
        }

        public void WriteRaw<T>(T obj) where T : struct
        {
            int length = Unsafe.SizeOf<T>();
            EnsureCapacity(WriteOffset + length);
            Unsafe.WriteUnaligned(ref _buffer[WriteOffset], obj);
            WriteOffset += length;
        }

        public T ReadRaw<T>() where T : struct
        {
            int size = Unsafe.SizeOf<T>();
            if (ReadOffset + size > _buffer.Length)
                return default(T);

            var result = Unsafe.ReadUnaligned<T>(ref _buffer[ReadOffset]);
            ReadOffset += size;
            return result;
        }
    }
}
