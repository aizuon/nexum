using System;

namespace Nexum.Core
{
    internal static class ReliableUdpConfig
    {
        internal const double FirstResendCoolTime = 0.3;

        internal const double EnlargeResendCoolTimeRatio = 1.3;

        internal const double MinResendCoolTime = 0.05;

        internal const double MaxResendCoolTime = 2.0;

        internal const double FrameMoveInterval = 0.01;

        internal const int ReceiveSpeedBeforeUpdate = 100;

        internal const double CalcRecentReceiveInterval = 1.0;

        internal const double BrakeMaxSendSpeedThreshold = 0.7;

        internal const double StreamToSenderWindowCoalesceInterval = 0.05;

        internal const bool HighPriorityAckFrame = true;

        internal const bool HighPriorityDataFrame = true;

        internal const double ResendLimitRatio = 0.1;

        internal const int MinResendLimitCount = 500;

        internal const int MaxResendLimitCount = 2000;

        internal const int MaxRetryCount = 15;

        internal const double MaxRetryElapsedTime = 20.0;

        internal const double AckSendInterval = 0.05;

        internal const double UdpPingInterval = 2.0;

        internal const double CsPingInterval = UdpPingInterval;

        internal const double P2PPingInterval = UdpPingInterval;

        internal const double FallbackServerUdpToTcpTimeout = CsPingInterval * 4.0;

        internal const double FallbackP2PUdpToTcpTimeout = P2PPingInterval * 4.0;

        internal const int ServerUdpRepunchMaxTrialCount = 3;

        internal const double LagLinearProgrammingFactor = 0.8;
        internal static int FrameLength => Math.Max(1300, FragmentConfig.MtuLength - 100);

        internal static int MaxAckCountInOneFrame => (FragmentConfig.MtuLength - 10) / 5;

        internal static int MaxSendSpeedInFrameCount => NetConfig.MessageMaxLength / FragmentConfig.MtuLength;
    }
}
