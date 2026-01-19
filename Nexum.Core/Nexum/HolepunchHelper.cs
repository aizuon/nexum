using System;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Nexum.Core
{
    internal static class HolepunchHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NetMessage CreateServerHolepunchMessage(Guid magicNumber)
        {
            var msg = new NetMessage();
            msg.WriteEnum(MessageType.ServerHolepunch);
            msg.Write(magicNumber);
            return msg;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NetMessage CreateServerHolepunchAckMessage(Guid magicNumber, IPEndPoint udpEndPoint)
        {
            var msg = new NetMessage();
            msg.WriteEnum(MessageType.ServerHolepunchAck);
            msg.Write(magicNumber);
            msg.Write(udpEndPoint);
            return msg;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NetMessage CreateNotifyClientServerUdpMatchedMessage(Guid magicNumber)
        {
            var msg = new NetMessage();
            msg.WriteEnum(MessageType.NotifyClientServerUdpMatched);
            msg.Write(magicNumber);
            return msg;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NetMessage CreatePeerUdpServerHolepunchMessage(Guid magicNumber, uint targetHostId)
        {
            var msg = new NetMessage();
            msg.WriteEnum(MessageType.PeerUdp_ServerHolepunch);
            msg.Write(magicNumber);
            msg.Write(targetHostId);
            return msg;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NetMessage CreatePeerUdpServerHolepunchAckMessage(Guid magicNumber, IPEndPoint udpEndPoint,
            uint targetHostId)
        {
            var msg = new NetMessage();
            msg.WriteEnum(MessageType.PeerUdp_ServerHolepunchAck);
            msg.Write(magicNumber);
            msg.Write(udpEndPoint);
            msg.Write(targetHostId);
            return msg;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NetMessage CreatePeerUdpPeerHolepunchMessage(uint hostId, Guid peerMagicNumber,
            Guid serverGuid, IPEndPoint targetEndpoint)
        {
            var msg = new NetMessage();
            msg.WriteEnum(MessageType.PeerUdp_PeerHolepunch);
            msg.Write(hostId);
            msg.Write(peerMagicNumber);
            msg.Write(serverGuid);
            msg.Write(targetEndpoint);
            return msg;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NetMessage CreatePeerUdpPeerHolepunchAckMessage(Guid magicNumber, uint hostId,
            IPEndPoint selfUdpSocket, IPEndPoint receivedEndPoint, IPEndPoint targetEndPoint)
        {
            var msg = new NetMessage();
            msg.WriteEnum(MessageType.PeerUdp_PeerHolepunchAck);
            msg.Write(magicNumber);
            msg.Write(hostId);
            msg.Write(selfUdpSocket);
            msg.Write(receivedEndPoint);
            msg.Write(targetEndPoint);
            return msg;
        }

        internal static void SendBurstMessages(
            Func<NetMessage> messageFactory,
            Action<NetMessage> sendAction,
            int delayMs = HolepunchConfig.BurstDelayMs,
            int burstCount = HolepunchConfig.BurstCount)
        {
            Task.Run(async () =>
            {
                for (int i = 0; i < burstCount; i++)
                {
                    var msg = messageFactory();
                    sendAction(msg);
                    if (i < burstCount - 1)
                        await Task.Delay(delayMs);
                }
            });
        }

        internal static void SendBurstMessagesWithCheck(
            Func<NetMessage> messageFactory,
            Action<NetMessage> sendAction,
            Func<bool> shouldContinue,
            int delayMs = HolepunchConfig.BurstDelayMs,
            int burstCount = HolepunchConfig.BurstCount)
        {
            Task.Run(async () =>
            {
                for (int i = 0; i < burstCount; i++)
                {
                    if (!shouldContinue())
                        return;
                    var msg = messageFactory();
                    sendAction(msg);
                    if (i < burstCount - 1)
                        await Task.Delay(delayMs);
                }
            });
        }

        internal static async Task<bool> WaitForConditionWithBackoffAsync(
            Func<bool> condition,
            Func<bool> cancellationCheck,
            int maxAttempts = HolepunchConfig.MaxSocketWaitAttempts,
            int initialDelayMs = HolepunchConfig.InitialBackoffDelayMs,
            int maxDelayMs = HolepunchConfig.MaxBackoffDelayMs)
        {
            if (cancellationCheck())
                return false;

            if (condition())
                return true;

            int delayMs = initialDelayMs;

            for (int i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(delayMs);

                if (cancellationCheck())
                    return false;

                if (condition())
                    return true;

                delayMs = Math.Min(delayMs * 2, maxDelayMs);
            }

            return false;
        }

        internal static void WithOrderedLocks(uint idA, uint idB, object lockA, object lockB, Action action)
        {
            object lock1 = idA < idB ? lockA : lockB;
            object lock2 = idA < idB ? lockB : lockA;

            lock (lock1)
            lock (lock2)
            {
                action();
            }
        }

        internal static T WithOrderedLocks<T>(uint idA, uint idB, object lockA, object lockB, Func<T> func)
        {
            object lock1 = idA < idB ? lockA : lockB;
            object lock2 = idA < idB ? lockB : lockA;

            lock (lock1)
            lock (lock2)
            {
                return func();
            }
        }
    }
}
