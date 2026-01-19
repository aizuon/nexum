using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Nexum.Core
{
    internal sealed class ReliableUdpSender
    {
        private readonly List<SenderFrame> _firstSenderWindow = new List<SenderFrame>(32);
        private readonly ReliableUdpHost _owner;
        private readonly List<SenderFrame> _resendWindow = new List<SenderFrame>(64);
        private readonly StreamQueue _sendStream = new StreamQueue();

        private uint _currentFrameNumber;
        private double _lastDoStreamToSenderWindowTime;
        private uint _lastExpectedFrameNumberAtSender;

        private uint _lastReceivedAckFrameNumber;
        private double _lastReceivedAckTime;
        private double _maxResendElapsedTime;

        private int _recentSendFrameToUdpCount = ReliableUdpConfig.ReceiveSpeedBeforeUpdate;
        private int _recentSendFrameToUdpSpeed = ReliableUdpConfig.ReceiveSpeedBeforeUpdate;
        private double _recentSendFrameToUdpStartTime;
        private int _remoteReceiveSpeed = ReliableUdpConfig.ReceiveSpeedBeforeUpdate;
        private int _sendSpeedLimit = ReliableUdpConfig.MaxSendSpeedInFrameCount;

        public ReliableUdpSender(ReliableUdpHost owner, uint firstFrameNumber)
        {
            _owner = owner;
            _currentFrameNumber = firstFrameNumber;
        }

        public uint NextFrameNumber => _currentFrameNumber++;

        public int PendingFrameCount => _firstSenderWindow.Count + _resendWindow.Count;
        public int SendStreamLength => _sendStream.Length;

        public void Send(byte[] data, int length)
        {
            _sendStream.PushBack(data, length);
            StreamToSenderWindowOnNeed(false);
        }

        public void FrameMove(double currentTime, double elapsedTime)
        {
            CalcRecentSendSpeed(currentTime);
            UpdateSendSpeedLimit();
            StreamToSenderWindowOnNeed(false);
            ReSendWindowToUdpOnNeed(currentTime, elapsedTime);
            FirstSenderWindowToUdpOnNeed(currentTime);
        }

        public void ProcessAckFrame(ReliableUdpFrame frame)
        {
            uint[] ackedFrames = frame.AckedFrameNumbers.Uncompress();
            int ackedCount = ackedFrames.Length;

            if (ackedCount > 0)
            {
                _lastReceivedAckFrameNumber = ackedFrames[ackedCount - 1];
                _lastReceivedAckTime = _owner.GetAbsoluteTime();
            }

            RemoveFramesBeforeExpected(frame.ExpectedFrameNumber);
            _lastExpectedFrameNumberAtSender = frame.ExpectedFrameNumber;

            for (int i = 0; i < ackedCount; i++)
                RemoveFromResendWindow(ackedFrames[i]);

            _remoteReceiveSpeed = (int)SysUtil.Lerp(_remoteReceiveSpeed, frame.RecentReceiveSpeed, 0.9) + 1;
        }

        public void StreamToSenderWindowOnNeed(bool moveNow)
        {
            double currentTime = _owner.GetAbsoluteTime();

            if (!moveNow)
            {
                bool bufferEmpty = _owner.GetUdpSendBufferPacketFilledCount() == 0;
                bool intervalElapsed = currentTime - _lastDoStreamToSenderWindowTime >
                                       ReliableUdpConfig.StreamToSenderWindowCoalesceInterval;

                if (!bufferEmpty && !intervalElapsed)
                    return;
            }

            _lastDoStreamToSenderWindowTime = currentTime;

            while (_sendStream.Length > 0)
            {
                int frameLength = Math.Min(ReliableUdpConfig.FrameLength, _sendStream.Length);
                byte[] frameData = null;
                _sendStream.GetBlockedData(ref frameData, frameLength);

                var senderFrame = new SenderFrame
                {
                    FrameNumber = NextFrameNumber,
                    Data = frameData,
                    Type = ReliableUdpFrameType.Data
                };

                _firstSenderWindow.Insert(0, senderFrame);
                _sendStream.PopFront(frameLength);
            }
        }

        public bool HasFailed(double currentTime)
        {
            int count = _resendWindow.Count;
            for (int i = 0; i < count; i++)
            {
                var frame = _resendWindow[i];
                if (frame.ResendCount >= ReliableUdpConfig.MaxRetryCount)
                    return true;

                if (frame.FirstSendTime > 0 &&
                    currentTime - frame.FirstSendTime > ReliableUdpConfig.MaxRetryElapsedTime)
                    return true;
            }

            return false;
        }

        private void CalcRecentSendSpeed(double currentTime)
        {
            if (_recentSendFrameToUdpStartTime == 0.0)
                _recentSendFrameToUdpStartTime = currentTime;

            if (currentTime - _recentSendFrameToUdpStartTime > ReliableUdpConfig.CalcRecentReceiveInterval)
            {
                double ratio = 0.1 / (currentTime - _recentSendFrameToUdpStartTime);
                _recentSendFrameToUdpSpeed =
                    (int)SysUtil.Lerp(_recentSendFrameToUdpSpeed, _recentSendFrameToUdpCount, ratio);
                _recentSendFrameToUdpCount = 0;
                _recentSendFrameToUdpStartTime = currentTime;
            }
        }

        private void UpdateSendSpeedLimit()
        {
            double sendSpeed = _recentSendFrameToUdpSpeed;
            double receiveSpeed = _remoteReceiveSpeed;

            if (sendSpeed * ReliableUdpConfig.BrakeMaxSendSpeedThreshold <= receiveSpeed)
                _sendSpeedLimit = ReliableUdpConfig.MaxSendSpeedInFrameCount;
            else
                _sendSpeedLimit = (int)receiveSpeed + 1;
        }

        private void FirstSenderWindowToUdpOnNeed(double currentTime)
        {
            double recentPing = _owner.GetRecentPing();
            bool isReliableChannel = _owner.IsReliableChannel();

            for (int i = _firstSenderWindow.Count - 1; i >= 0; i--)
            {
                var frame = _firstSenderWindow[i];
                frame.LastSendTime = currentTime;
                frame.FirstSendTime = currentTime;
                frame.ResendCoolTime = recentPing > 0 ? recentPing * 2.0 : ReliableUdpConfig.FirstResendCoolTime;
                frame.ResendCoolTime = Math.Max(frame.ResendCoolTime, ReliableUdpConfig.MinResendCoolTime);
                frame.ResendCoolTime = Math.Min(frame.ResendCoolTime, ReliableUdpConfig.MaxResendCoolTime);

                SendOneFrame(frame);

                if (!isReliableChannel)
                    _resendWindow.Add(frame);

                _firstSenderWindow.RemoveAt(i);
            }
        }

        private void ReSendWindowToUdpOnNeed(double currentTime, double elapsedTime)
        {
            double resendLimit = Math.Min(
                Math.Max(_resendWindow.Count * ReliableUdpConfig.ResendLimitRatio,
                    ReliableUdpConfig.MinResendLimitCount),
                ReliableUdpConfig.MaxResendLimitCount) * elapsedTime;

            double heartbeatInterval = ReliableUdpConfig.FrameMoveInterval * 5;
            if (elapsedTime > heartbeatInterval)
            {
                double ratio = elapsedTime / heartbeatInterval;
                resendLimit /= ratio;
            }

            int maxResends = Math.Max(1, (int)resendLimit);
            bool isReliableChannel = _owner.IsReliableChannel();

            int count = _resendWindow.Count;
            for (int i = 0; i < count && maxResends > 0; i++)
            {
                var frame = _resendWindow[i];

                if (currentTime - frame.LastSendTime > frame.ResendCoolTime)
                {
                    if (frame.FirstSendTime > 0)
                        _maxResendElapsedTime = Math.Max(_maxResendElapsedTime, currentTime - frame.FirstSendTime);

                    frame.ResendCoolTime *= ReliableUdpConfig.EnlargeResendCoolTimeRatio;
                    frame.ResendCoolTime = Math.Min(frame.ResendCoolTime, ReliableUdpConfig.MaxResendCoolTime);
                    frame.LastSendTime = currentTime;
                    frame.ResendCount++;

                    SendOneFrame(frame);

                    if (isReliableChannel)
                    {
                        _resendWindow.RemoveAt(i);
                        i--;
                        count--;
                    }

                    maxResends--;
                }
            }
        }

        private void SendOneFrame(SenderFrame frame)
        {
            var udpFrame = new ReliableUdpFrame
            {
                Type = frame.Type,
                FrameNumber = frame.FrameNumber,
                Data = frame.Data
            };

            _owner.SendFrameToUdp(udpFrame);

            if (frame.Type == ReliableUdpFrameType.Data)
                _recentSendFrameToUdpCount++;
        }

        private bool RemoveFromResendWindow(uint frameNumber)
        {
            for (int i = _resendWindow.Count - 1; i >= 0; i--)
                if (_resendWindow[i].FrameNumber == frameNumber)
                {
                    _resendWindow.RemoveAt(i);
                    return true;
                }

            return false;
        }

        private void RemoveFramesBeforeExpected(uint expectedFrameNumber)
        {
            for (int i = _resendWindow.Count - 1; i >= 0; i--)
                if (CompareFrameNumbers(_resendWindow[i].FrameNumber, expectedFrameNumber) < 0)
                    _resendWindow.RemoveAt(i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareFrameNumbers(uint a, uint b)
        {
            uint diff = a - b;
            if (diff == 0)
                return 0;
            return diff <= int.MaxValue ? 1 : -1;
        }

        private sealed class SenderFrame
        {
            public byte[] Data;
            public double FirstSendTime;
            public uint FrameNumber;
            public double LastSendTime;
            public double ResendCoolTime;
            public int ResendCount;
            public ReliableUdpFrameType Type;
        }
    }
}
