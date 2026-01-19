using System;

namespace Nexum.Core
{
    internal sealed class ReliableUdpHost
    {
        private readonly object _lock = new object();

        public ReliableUdpHost(uint firstFrameNumber)
            : this(firstFrameNumber, firstFrameNumber)
        {
        }

        public ReliableUdpHost(uint senderFirstFrameNumber, uint receiverExpectedFrameNumber)
        {
            Sender = new ReliableUdpSender(this, senderFirstFrameNumber);
            Receiver = new ReliableUdpReceiver(this, receiverExpectedFrameNumber);
        }

        public Func<double> GetAbsoluteTime { get; set; } = () => 0.0;

        public Func<double> GetRecentPing { get; set; } = () => 0.0;

        public Func<uint> GetUdpSendBufferPacketFilledCount { get; set; } = () => 0;

        public Func<bool> IsReliableChannel { get; set; } = () => false;

        public Action<ReliableUdpFrame> SendOneFrameToUdpLayer { get; set; } = _ => { };

        public ReliableUdpSender Sender { get; }

        public ReliableUdpReceiver Receiver { get; }

        public StreamQueue ReceivedStream => Receiver.ReceivedStream;
        public uint ExpectedFrameNumber => Receiver.ExpectedFrameNumber;
        public bool Failed { get; private set; }

        public event Action OnFailed;

        public void Send(byte[] data, int length)
        {
            lock (_lock)
            {
                if (Failed)
                    return;

                Sender.Send(data, length);
            }
        }

        public void TakeReceivedFrame(ReliableUdpFrame frame)
        {
            lock (_lock)
            {
                if (Failed)
                    return;

                Receiver.ProcessReceivedFrame(frame);
            }
        }

        public void FrameMove(double elapsedTime)
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

        public uint YieldFrameNumber()
        {
            lock (_lock)
            {
                return Sender.NextFrameNumber;
            }
        }

        public void FlushSendStream()
        {
            lock (_lock)
            {
                Sender.StreamToSenderWindowOnNeed(true);
            }
        }

        public void Reset(uint firstFrameNumber)
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

        public ReliableUdpStats GetStats()
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
        public int ReceivedStreamCount { get; set; }
        public uint ExpectedFrameNumber { get; set; }
        public int RecentReceiveSpeed { get; set; }
        public int SendStreamCount { get; set; }
        public int PendingFrameCount { get; set; }
        public bool Failed { get; set; }
    }
}
