using System;

namespace Nexum.Core
{
    internal sealed class ReliableUdpHost
    {
        private readonly object _lock = new object();

        internal ReliableUdpHost(uint firstFrameNumber)
            : this(firstFrameNumber, firstFrameNumber)
        {
        }

        internal ReliableUdpHost(uint senderFirstFrameNumber, uint receiverExpectedFrameNumber)
        {
            Sender = new ReliableUdpSender(this, senderFirstFrameNumber);
            Receiver = new ReliableUdpReceiver(this, receiverExpectedFrameNumber);
        }

        internal Func<double> GetAbsoluteTime { get; set; } = () => 0.0;

        internal Func<double> GetRecentPing { get; set; } = () => 0.0;

        internal Func<uint> GetUdpSendBufferPacketFilledCount { get; set; } = () => 0;

        internal Func<bool> IsReliableChannel { get; set; } = () => false;

        internal Action<ReliableUdpFrame> SendOneFrameToUdpLayer { get; set; } = _ => { };

        internal ReliableUdpSender Sender { get; }

        internal ReliableUdpReceiver Receiver { get; }

        internal StreamQueue ReceivedStream => Receiver.ReceivedStream;
        internal uint ExpectedFrameNumber => Receiver.ExpectedFrameNumber;
        internal bool Failed { get; private set; }

        internal event Action OnFailed;

        internal void Send(byte[] data, int length)
        {
            lock (_lock)
            {
                if (Failed)
                    return;

                Sender.Send(data, length);
            }
        }

        internal void TakeReceivedFrame(ReliableUdpFrame frame)
        {
            lock (_lock)
            {
                if (Failed)
                    return;

                Receiver.ProcessReceivedFrame(frame);
            }
        }

        internal void FrameMove(double elapsedTime)
        {
            Action failedCallback = null;

            lock (_lock)
            {
                if (Failed)
                    return;

                double currentTime = GetAbsoluteTime();

                Receiver.FrameMove(currentTime, elapsedTime);
                Sender.FrameMove(currentTime, elapsedTime);

                if (Sender.HasFailed(currentTime))
                {
                    Failed = true;
                    failedCallback = OnFailed;
                }
            }

            failedCallback?.Invoke();
        }

        internal uint YieldFrameNumber()
        {
            lock (_lock)
            {
                return Sender.NextFrameNumber;
            }
        }

        internal void FlushSendStream()
        {
            lock (_lock)
            {
                Sender.StreamToSenderWindowOnNeed(true);
            }
        }

        internal void Reset(uint firstFrameNumber)
        {
            lock (_lock)
            {
                Failed = false;
            }
        }

        internal void SendFrameToUdp(ReliableUdpFrame frame)
        {
            SendOneFrameToUdpLayer(frame);
        }

        internal ReliableUdpStats GetStats()
        {
            lock (_lock)
            {
                return new ReliableUdpStats
                {
                    ReceivedStreamCount = Receiver.ReceivedStream.Length,
                    ExpectedFrameNumber = Receiver.ExpectedFrameNumber,
                    RecentReceiveSpeed = Receiver.RecentReceiveSpeed,
                    SendStreamCount = Sender.SendStreamLength,
                    PendingFrameCount = Sender.PendingFrameCount,
                    Failed = Failed
                };
            }
        }
    }

    internal sealed class ReliableUdpStats
    {
        internal int ReceivedStreamCount { get; set; }
        internal uint ExpectedFrameNumber { get; set; }
        internal int RecentReceiveSpeed { get; set; }
        internal int SendStreamCount { get; set; }
        internal int PendingFrameCount { get; set; }
        internal bool Failed { get; set; }
    }
}
