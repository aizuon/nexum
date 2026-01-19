using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexum.Core
{
    internal sealed class CompressedFrameNumbers
    {
        private readonly List<Range> _ranges = new List<Range>(8);

        public int Count => _ranges.Count;

        public int TotalFrameCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _ranges.Count; i++)
                {
                    var range = _ranges[i];
                    count += (int)(range.Right - range.Left + 1);
                }

                return count;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddSortedNumber(uint frameNumber)
        {
            if (_ranges.Count == 0)
            {
                _ranges.Add(new Range(frameNumber, frameNumber));
                return;
            }

            ref var lastRange = ref CollectionsMarshal.AsSpan(_ranges)[^1];

            if (frameNumber >= lastRange.Left && frameNumber <= lastRange.Right)
                return;

            if (frameNumber == lastRange.Right + 1)
            {
                lastRange = new Range(lastRange.Left, frameNumber);
                return;
            }

            _ranges.Add(new Range(frameNumber, frameNumber));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint[] Uncompress()
        {
            int totalCount = TotalFrameCount;
            if (totalCount == 0)
                return Array.Empty<uint>();

            uint[] result = GC.AllocateUninitializedArray<uint>(totalCount);
            int index = 0;

            for (int i = 0; i < _ranges.Count; i++)
            {
                var range = _ranges[i];
                for (uint f = range.Left; f <= range.Right; f++)
                    result[index++] = f;
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadFrom(NetMessage msg)
        {
            _ranges.Clear();

            int count = 0;
            if (!msg.Read(out count))
                return false;

            for (int i = 0; i < count; i++)
            {
                sbyte flag = 0;
                if (!msg.Read(out flag))
                    return false;

                uint left = 0;
                if (!msg.Read(out left))
                    return false;

                uint right = left;
                if (flag != 0)
                    if (!msg.Read(out right))
                        return false;

                _ranges.Add(new Range(left, right));
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteTo(NetMessage msg)
        {
            msg.Write(_ranges.Count);

            for (int i = 0; i < _ranges.Count; i++)
            {
                var range = _ranges[i];
                if (range.Left == range.Right)
                {
                    msg.Write((sbyte)0);
                    msg.Write(range.Left);
                }
                else
                {
                    msg.Write((sbyte)1);
                    msg.Write(range.Left);
                    msg.Write(range.Right);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _ranges.Clear();
        }

        public CompressedFrameNumbers Clone()
        {
            var clone = new CompressedFrameNumbers();
            for (int i = 0; i < _ranges.Count; i++)
            {
                var range = _ranges[i];
                clone._ranges.Add(new Range(range.Left, range.Right));
            }

            return clone;
        }

        private readonly struct Range
        {
            public readonly uint Left;
            public readonly uint Right;

            public Range(uint left, uint right)
            {
                Left = left;
                Right = right;
            }
        }
    }
}
