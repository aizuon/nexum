using System;
using Nexum.Core.Configuration;

namespace Nexum.Core.ReliableUdp
{
    internal sealed class ReliableUdpHost
    {
        private readonly object _receiverLock = new object();
        private readonly object _senderLock = new object();
        private volatile bool _failed;

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
        internal bool Failed => _failed;

        internal event Action OnFailed;

        internal void Send(byte[] data, int length)
        {
            if (_failed)
                return;

            lock (_senderLock)
            {
                if (_failed)
                    return;

                Sender.Send(data, length);
            }
        }

        internal void TakeReceivedFrame(ReliableUdpFrame frame)
        {
            if (_failed)
                return;

            if (frame.Type == ReliableUdpFrameType.Ack)
                lock (_senderLock)
                {
                    if (_failed)
                        return;

                    Receiver.ProcessReceivedFrame(frame);
                }
            else
                lock (_receiverLock)
                {
                    if (_failed)
                        return;

                    Receiver.ProcessReceivedFrame(frame);
                }
        }

        internal void FrameMove(double elapsedTime)
        {
            if (_failed)
                return;

            Action failedCallback = null;
            double currentTime = GetAbsoluteTime();

            lock (_receiverLock)
            {
                if (!_failed)
                    Receiver.FrameMove(currentTime, elapsedTime);
            }

            lock (_senderLock)
            {
                if (_failed)
                    return;

                Sender.FrameMove(currentTime, elapsedTime);

                if (Sender.HasFailed(currentTime))
                {
                    _failed = true;
                    failedCallback = OnFailed;
                }
            }

            failedCallback?.Invoke();
        }

        internal uint YieldFrameNumber()
        {
            return Sender.NextFrameNumber;
        }

        internal void FlushSendStream()
        {
            if (_failed)
                return;

            lock (_senderLock)
            {
                Sender.StreamToSenderWindowOnNeed(true);
            }
        }

        internal void Reset(uint firstFrameNumber)
        {
            _failed = false;
        }

        internal void SendFrameToUdp(ReliableUdpFrame frame)
        {
            SendOneFrameToUdpLayer(frame);
        }

        internal ReliableUdpStats GetStats()
        {
            return new ReliableUdpStats
            {
                ReceivedStreamCount = Receiver.ReceivedStream.Length,
                ExpectedFrameNumber = Receiver.ExpectedFrameNumber,
                RecentReceiveSpeed = Receiver.RecentReceiveSpeed,
                SendStreamCount = Sender.SendStreamLength,
                PendingFrameCount = Sender.PendingFrameCount,
                Failed = _failed
            };
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
