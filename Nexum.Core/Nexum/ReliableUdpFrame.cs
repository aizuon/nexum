using System;
using System.Runtime.CompilerServices;

namespace Nexum.Core
{
    internal sealed class ReliableUdpFrame
    {
        internal ReliableUdpFrame()
        {
            AckedFrameNumbers = new CompressedFrameNumbers();
        }

        internal ReliableUdpFrameType Type { get; set; }
        internal uint FrameNumber { get; set; }
        internal byte[] Data { get; set; }
        internal CompressedFrameNumbers AckedFrameNumbers { get; set; }
        internal uint ExpectedFrameNumber { get; set; }
        internal int RecentReceiveSpeed { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReliableUdpFrame Clone()
        {
            byte[] clonedData = null;
            if (Data != null && Data.Length > 0)
            {
                clonedData = GC.AllocateUninitializedArray<byte>(Data.Length);
                Buffer.BlockCopy(Data, 0, clonedData, 0, Data.Length);
            }

            return new ReliableUdpFrame
            {
                Type = Type,
                FrameNumber = FrameNumber,
                Data = clonedData,
                AckedFrameNumbers = AckedFrameNumbers?.Clone() ?? new CompressedFrameNumbers(),
                ExpectedFrameNumber = ExpectedFrameNumber,
                RecentReceiveSpeed = RecentReceiveSpeed
            };
        }
    }
}
