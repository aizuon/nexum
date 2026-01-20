using System;
using System.Runtime.CompilerServices;

namespace Nexum.Core
{
    internal static class ReliableUdpHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetMessage BuildFrameMessage(ReliableUdpFrame frame)
        {
            var msg = new NetMessage();
            msg.WriteEnum(MessageType.ReliableUdp_Frame);
            msg.WriteEnum(frame.Type);

            switch (frame.Type)
            {
                case ReliableUdpFrameType.Data:
                    msg.Write(frame.FrameNumber);
                    msg.WriteScalar(frame.Data?.Length ?? 0);
                    if (frame.Data != null && frame.Data.Length > 0)
                        msg.Write(frame.Data);
                    break;

                case ReliableUdpFrameType.Ack:
                    frame.AckedFrameNumbers?.WriteTo(msg);
                    msg.Write(frame.ExpectedFrameNumber);
                    msg.Write(frame.RecentReceiveSpeed);
                    break;
            }

            return msg;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ParseFrame(NetMessage msg, out ReliableUdpFrame frame)
        {
            frame = new ReliableUdpFrame();

            if (!msg.Read(out byte frameType))
                return false;

            frame.Type = (ReliableUdpFrameType)frameType;

            switch (frame.Type)
            {
                case ReliableUdpFrameType.Data:
                {
                    if (!msg.Read(out uint frameNumber))
                        return false;
                    frame.FrameNumber = frameNumber;

                    long dataLength = 0;
                    if (!msg.ReadScalar(ref dataLength))
                        return false;

                    if (dataLength > 0)
                    {
                        byte[] data = GC.AllocateUninitializedArray<byte>((int)dataLength);
                        if (!msg.Read(ref data, (int)dataLength))
                            return false;
                        frame.Data = data;
                    }
                    else
                    {
                        frame.Data = Array.Empty<byte>();
                    }

                    break;
                }

                case ReliableUdpFrameType.Ack:
                {
                    frame.AckedFrameNumbers = new CompressedFrameNumbers();
                    if (!frame.AckedFrameNumbers.ReadFrom(msg))
                        return false;

                    if (!msg.Read(out uint expectedFrameNumber))
                        return false;
                    frame.ExpectedFrameNumber = expectedFrameNumber;

                    if (!msg.Read(out int recentReceiveSpeed))
                        return false;
                    frame.RecentReceiveSpeed = recentReceiveSpeed;

                    break;
                }

                default:
                    return false;
            }

            return true;
        }

        public static byte[] WrapPayload(byte[] payload)
        {
            var msg = new NetMessage();
            msg.Write(Constants.TcpSplitter);
            msg.Write(new ByteArray(payload, true));
            return msg.GetBuffer();
        }

        public static bool UnwrapPayload(byte[] wrappedData, out byte[] payload)
        {
            payload = null;

            var msg = new NetMessage(wrappedData, true);
            if (!msg.Read(out ushort magic))
                return false;

            if (magic != Constants.TcpSplitter)
                return false;

            var unwrappedPayload = new ByteArray();
            if (!msg.Read(ref unwrappedPayload))
                return false;

            payload = unwrappedPayload.GetBuffer();
            return true;
        }
    }
}
