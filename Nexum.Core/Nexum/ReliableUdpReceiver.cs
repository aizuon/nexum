using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Nexum.Core
{
    internal sealed class ReliableUdpReceiver
    {
        private readonly HashSet<uint> _acksToSend = new HashSet<uint>(64);
        private readonly ReliableUdpHost _owner;
        private readonly List<ReceiverFrame> _receiverWindow = new List<ReceiverFrame>(32);
        private readonly List<uint> _sortedAcksCache = new List<uint>(64);

        private uint _lastReceivedDataFrameNumber;
        private double _lastSendGatheredAcksTime;

        private int _recentReceiveFrameCount;
        private double _recentReceiveFrameCountStartTime;

        public ReliableUdpReceiver(ReliableUdpHost owner, uint firstFrameNumber)
        {
            _owner = owner;
            ExpectedFrameNumber = firstFrameNumber;
            _lastReceivedDataFrameNumber = firstFrameNumber;
        }

        public StreamQueue ReceivedStream { get; } = new StreamQueue();

        public uint ExpectedFrameNumber { get; private set; }

        public int RecentReceiveSpeed { get; private set; } = ReliableUdpConfig.ReceiveSpeedBeforeUpdate;

        public void FrameMove(double currentTime, double elapsedTime)
        {
            if (_recentReceiveFrameCountStartTime == 0.0)
                _recentReceiveFrameCountStartTime = currentTime;

            if (currentTime - _recentReceiveFrameCountStartTime > ReliableUdpConfig.CalcRecentReceiveInterval)
            {
                double ratio = 0.1 / (currentTime - _recentReceiveFrameCountStartTime);
                RecentReceiveSpeed = (int)SysUtil.Lerp(RecentReceiveSpeed, _recentReceiveFrameCount, ratio);
                _recentReceiveFrameCount = 0;
                _recentReceiveFrameCountStartTime = currentTime;
            }

            if (currentTime - _lastSendGatheredAcksTime > ReliableUdpConfig.AckSendInterval)
            {
                SendGatheredAcks();
                _lastSendGatheredAcksTime = currentTime;
            }
        }

        public void ProcessReceivedFrame(ReliableUdpFrame frame)
        {
            switch (frame.Type)
            {
                case ReliableUdpFrameType.Data:
                    ProcessDataFrame(frame);
                    break;
                case ReliableUdpFrameType.Ack:
                    ProcessAckFrame(frame);
                    break;
            }
        }

        private void ProcessDataFrame(ReliableUdpFrame frame)
        {
            if (!_owner.IsReliableChannel())
                _acksToSend.Add(frame.FrameNumber);

            if (CompareFrameNumbers(frame.FrameNumber, ExpectedFrameNumber) >= 0)
                _lastReceivedDataFrameNumber = frame.FrameNumber;

            if (IsTooOldFrame(frame.FrameNumber))
                return;

            AddToReceiverWindow(frame);

            FlushInOrderFramesToStream();
        }

        private void ProcessAckFrame(ReliableUdpFrame frame)
        {
            _owner.Sender.ProcessAckFrame(frame);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddToReceiverWindow(ReliableUdpFrame frame)
        {
            int count = _receiverWindow.Count;
            for (int i = 0; i < count; i++)
            {
                if (_receiverWindow[i].FrameNumber == frame.FrameNumber)
                    return;

                if (CompareFrameNumbers(frame.FrameNumber, _receiverWindow[i].FrameNumber) < 0)
                {
                    _receiverWindow.Insert(i, new ReceiverFrame(frame));
                    _recentReceiveFrameCount++;
                    return;
                }
            }

            _receiverWindow.Add(new ReceiverFrame(frame));
            _recentReceiveFrameCount++;
        }

        private void FlushInOrderFramesToStream()
        {
            while (_receiverWindow.Count > 0)
            {
                var frame = _receiverWindow[0];
                if (frame.FrameNumber != ExpectedFrameNumber)
                    break;

                ReceivedStream.PushBack(frame.Data, frame.Data.Length);
                _receiverWindow.RemoveAt(0);
                ExpectedFrameNumber++;
            }
        }

        private void SendGatheredAcks()
        {
            if (_acksToSend.Count == 0)
                return;

            _sortedAcksCache.Clear();
            _sortedAcksCache.AddRange(_acksToSend);
            _sortedAcksCache.Sort();
            _acksToSend.Clear();

            int index = 0;
            while (index < _sortedAcksCache.Count)
            {
                var frame = new ReliableUdpFrame
                {
                    Type = ReliableUdpFrameType.Ack,
                    FrameNumber = 0,
                    AckedFrameNumbers = new CompressedFrameNumbers(),
                    ExpectedFrameNumber = ExpectedFrameNumber,
                    RecentReceiveSpeed = RecentReceiveSpeed
                };

                int count = 0;
                while (index < _sortedAcksCache.Count && count < ReliableUdpConfig.MaxAckCountInOneFrame)
                {
                    frame.AckedFrameNumbers.AddSortedNumber(_sortedAcksCache[index]);
                    index++;
                    count++;
                }

                _owner.SendFrameToUdp(frame);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsTooOldFrame(uint frameNumber)
        {
            return CompareFrameNumbers(frameNumber, ExpectedFrameNumber) < 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareFrameNumbers(uint a, uint b)
        {
            uint diff = a - b;
            if (diff == 0)
                return 0;
            return diff <= int.MaxValue ? 1 : -1;
        }

        private sealed class ReceiverFrame
        {
            public readonly byte[] Data;
            public readonly uint FrameNumber;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ReceiverFrame(ReliableUdpFrame frame)
            {
                FrameNumber = frame.FrameNumber;
                if (frame.Data == null || frame.Data.Length == 0)
                {
                    Data = Array.Empty<byte>();
                }
                else
                {
                    Data = GC.AllocateUninitializedArray<byte>(frame.Data.Length);
                    Buffer.BlockCopy(frame.Data, 0, Data, 0, frame.Data.Length);
                }
            }
        }
    }
}
