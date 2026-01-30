using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;

namespace Nexum.Core.Holepunching
{
    internal static class HolepunchHelper
    {
        internal static void SendBurstMessages(
            NetMessage message,
            Action<NetMessage> sendAction,
            int delayMs = HolepunchConfig.BurstDelayMs,
            int burstCount = HolepunchConfig.BurstCount)
        {
            Task.Run(async () =>
            {
                for (int i = 0; i < burstCount; i++)
                {
                    sendAction(message);
                    if (i < burstCount - 1)
                        await Task.Delay(delayMs);
                }
            });
        }

        internal static void SendBurstMessagesWithCheck(
            NetMessage message,
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
                    sendAction(message);
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

        internal static IPEndPoint[] GeneratePredictedEndpoints(IPEndPoint knownEndpoint, int count, int portRange)
        {
            if (knownEndpoint == null || count <= 0)
                return Array.Empty<IPEndPoint>();

            var endpoints = new List<IPEndPoint>(count);
            int basePort = knownEndpoint.Port;

            for (int i = 1; i <= portRange && endpoints.Count < count; i++)
            {
                int portAbove = basePort + i;
                if (portAbove <= 65535 && portAbove != basePort)
                    endpoints.Add(new IPEndPoint(knownEndpoint.Address, portAbove));

                if (endpoints.Count >= count)
                    break;

                int portBelow = basePort - i;
                if (portBelow >= 1024 && portBelow != basePort)
                    endpoints.Add(new IPEndPoint(knownEndpoint.Address, portBelow));
            }

            return endpoints.ToArray();
        }
    }
}
