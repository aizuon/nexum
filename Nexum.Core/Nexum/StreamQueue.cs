using System;
using System.Runtime.CompilerServices;

namespace Nexum.Core
{
    public sealed class StreamQueue
    {
        private readonly object _lock = new object();
        private byte[] _buffer;
        private int _count;
        private int _head;
        private int _tail;

        public StreamQueue(int initialCapacity = 4096)
        {
            _buffer = GC.AllocateUninitializedArray<byte>(initialCapacity);
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        public int Length
        {
            get
            {
                lock (_lock)
                {
                    return _count;
                }
            }
        }

        public void PushBack(byte[] data, int length)
        {
            if (length <= 0)
                return;

            lock (_lock)
            {
                EnsureCapacity(_count + length);

                int firstPart = Math.Min(length, _buffer.Length - _tail);
                if (firstPart > 0)
                    Buffer.BlockCopy(data, 0, _buffer, _tail, firstPart);

                int secondPart = length - firstPart;
                if (secondPart > 0)
                    Buffer.BlockCopy(data, firstPart, _buffer, 0, secondPart);

                _tail = (_tail + length) % _buffer.Length;
                _count += length;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PopFront(int length)
        {
            if (length <= 0)
                return;

            lock (_lock)
            {
                if (length > _count)
                    length = _count;

                _head = (_head + length) % _buffer.Length;
                _count -= length;

                if (_count == 0)
                {
                    _head = 0;
                    _tail = 0;
                }
            }
        }

        public void GetBlockedData(ref byte[] dest, int length)
        {
            lock (_lock)
            {
                if (length > _count)
                    length = _count;

                if (dest == null || dest.Length < length)
                    dest = GC.AllocateUninitializedArray<byte>(length);

                int firstPart = Math.Min(length, _buffer.Length - _head);
                if (firstPart > 0)
                    Buffer.BlockCopy(_buffer, _head, dest, 0, firstPart);

                int secondPart = length - firstPart;
                if (secondPart > 0)
                    Buffer.BlockCopy(_buffer, 0, dest, firstPart, secondPart);
            }
        }

        public byte[] PeekAll()
        {
            lock (_lock)
            {
                byte[] result = GC.AllocateUninitializedArray<byte>(_count);
                GetBlockedDataUnsafe(result, _count);
                return result;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _head = 0;
                _tail = 0;
                _count = 0;
            }
        }

        private void GetBlockedDataUnsafe(byte[] dest, int length)
        {
            if (length > _count)
                length = _count;

            int firstPart = Math.Min(length, _buffer.Length - _head);
            if (firstPart > 0)
                Buffer.BlockCopy(_buffer, _head, dest, 0, firstPart);

            int secondPart = length - firstPart;
            if (secondPart > 0)
                Buffer.BlockCopy(_buffer, 0, dest, firstPart, secondPart);
        }

        private void EnsureCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= _buffer.Length)
                return;

            int newCapacity = _buffer.Length;
            while (newCapacity < requiredCapacity)
                newCapacity *= 2;

            byte[] newBuffer = GC.AllocateUninitializedArray<byte>(newCapacity);

            if (_count > 0)
            {
                int firstPart = Math.Min(_count, _buffer.Length - _head);
                if (firstPart > 0)
                    Buffer.BlockCopy(_buffer, _head, newBuffer, 0, firstPart);

                int secondPart = _count - firstPart;
                if (secondPart > 0)
                    Buffer.BlockCopy(_buffer, 0, newBuffer, firstPart, secondPart);
            }

            _buffer = newBuffer;
            _head = 0;
            _tail = _count;
        }
    }
}
