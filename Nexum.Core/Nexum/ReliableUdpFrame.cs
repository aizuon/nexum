using System;
using System.Runtime.CompilerServices;

namespace Nexum.Core
{
    internal class ReliableUdpFrame
    {
        public ReliableUdpFrame()
        {
            AckedFrameNumbers = new CompressedFrameNumbers();
        }

        public ReliableUdpFrameType Type { get; set; }
        public uint FrameNumber { get; set; }
        public byte[] Data { get; set; }
        public CompressedFrameNumbers AckedFrameNumbers { get; set; }
        public uint ExpectedFrameNumber { get; set; }
        public int RecentReceiveSpeed { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReliableUdpFrame Clone()
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
