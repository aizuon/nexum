using System;
using System.Runtime.CompilerServices;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.ReliableUdp
{
    internal static class ReliableUdpHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NetMessage BuildFrameMessage(ReliableUdpFrame frame)
        {
            var msg = new NetMessage();
            msg.Write(MessageType.ReliableUdp_Frame);
            msg.Write(frame.Type);

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
        internal static bool ParseFrame(NetMessage msg, out ReliableUdpFrame frame)
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
                        byte[] data = null;
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

        internal static byte[] WrapPayload(ReadOnlySpan<byte> payload)
        {
            var msg = new NetMessage();
            msg.Write(Constants.TcpSplitter);
            msg.WriteByteArray(payload);
            return msg.GetBuffer();
        }

        internal static bool UnwrapPayload(byte[] wrappedData, out ReadOnlySpan<byte> payload)
        {
            payload = default(ReadOnlySpan<byte>);

            var msg = new NetMessage(wrappedData, true);
            if (!msg.Read(out ushort magic))
                return false;

            if (magic != Constants.TcpSplitter)
                return false;

            return msg.ReadByteArrayAsSpan(out payload);
        }

        internal static void ExtractMessagesFromStream(StreamQueue stream, Action<NetMessage> messageHandler)
        {
            if (stream == null || stream.Length == 0)
                return;

            while (stream.Length > 0)
            {
                byte[] streamData = stream.PeekAll();
                var tempMsg = new NetMessage(streamData, true);

                if (!tempMsg.Read(out ushort magic) || magic != Constants.TcpSplitter)
                    break;

                var streamPayload = new ByteArray();
                if (!tempMsg.Read(ref streamPayload))
                    break;

                int consumedBytes = tempMsg.ReadOffset;
                stream.PopFront(consumedBytes);

                var innerMessage = new NetMessage(streamPayload);
                messageHandler(innerMessage);
            }
        }
    }
}
