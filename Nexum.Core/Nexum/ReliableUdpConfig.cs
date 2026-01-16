using System;

namespace Nexum.Core
{
    internal static class ReliableUdpConfig
    {
        public static double FirstResendCoolTime = 0.5;

        public static double EnlargeResendCoolTimeRatio = 1.3;

        public static double MinResendCoolTime = 0.1;

        public static double MaxResendCoolTime = 2.0;

        public static double FrameMoveInterval = 0.01;

        public static int ReceiveSpeedBeforeUpdate = 100;

        public static double CalcRecentReceiveInterval = 1.0;

        public static double BrakeMaxSendSpeedThreshold = 0.8;

        public static double StreamToSenderWindowCoalesceInterval = 0.05;

        public static bool HighPriorityAckFrame = true;

        public static bool HighPriorityDataFrame = true;

        public static double ResendLimitRatio = 0.1;

        public static int MinResendLimitCount = 1000;

        public static int MaxResendLimitCount = 3000;

        public static int MaxRetryCount = 20;

        public static double MaxRetryElapsedTime = 30.0;

        public static double AckSendInterval = 0.05;

        public static double UdpPingInterval = 4.3;

        public static double CsPingInterval = UdpPingInterval;

        public static double P2PPingInterval = UdpPingInterval;

        public static double LagLinearProgrammingFactor = 0.8;
        public static int FrameLength => Math.Max(1300, FragmentConfig.MtuLength - 100);

        public static int MaxAckCountInOneFrame => (FragmentConfig.MtuLength - 10) / 5;

        public static int MaxSendSpeedInFrameCount => 10485760 / FragmentConfig.MtuLength;
    }
}
