using System.Collections.Concurrent;
using DotNetty.Transport.Channels;

namespace Nexum.Core.Simulation
{
    public static class NetworkSimulationStats
    {
        private static readonly ConcurrentDictionary<IChannel, SimulatedUdpChannelHandler> Handlers =
            new ConcurrentDictionary<IChannel, SimulatedUdpChannelHandler>();

        internal static void RegisterHandler(IChannel channel, SimulatedUdpChannelHandler handler)
        {
            if (channel != null)
                Handlers[channel] = handler;
        }

        internal static void UnregisterHandler(IChannel channel)
        {
            if (channel != null)
                Handlers.TryRemove(channel, out _);
        }

        public static NetworkSimulationStatistics GetStatistics()
        {
            long totalReceived = 0, totalDropped = 0, totalSent = 0, totalDelayed = 0, totalNatFiltered = 0;
            string profileName = "Mixed";

            foreach (var handler in Handlers.Values)
            {
                (long received, long dropped, long sent, long delayed, long natFiltered) = handler.GetStatistics();
                totalReceived += received;
                totalDropped += dropped;
                totalSent += sent;
                totalDelayed += delayed;
                totalNatFiltered += natFiltered;
            }

            return new NetworkSimulationStatistics
            {
                ProfileName = profileName,
                TotalReceived = totalReceived,
                TotalDropped = totalDropped,
                TotalSent = totalSent,
                TotalDelayed = totalDelayed,
                TotalNatFiltered = totalNatFiltered,
                DropRate = totalReceived > 0 ? (double)totalDropped / totalReceived : 0,
                HandlerCount = Handlers.Count
            };
        }

        public static void Clear()
        {
            Handlers.Clear();
        }
    }

    public class NetworkSimulationStatistics
    {
        public string ProfileName { get; init; }
        public long TotalReceived { get; init; }
        public long TotalDropped { get; init; }
        public long TotalSent { get; init; }
        public long TotalDelayed { get; init; }
        public long TotalNatFiltered { get; init; }
        public double DropRate { get; init; }
        public int HandlerCount { get; init; }

        public override string ToString()
        {
            string natInfo = TotalNatFiltered > 0 ? $", NAT Filtered: {TotalNatFiltered}" : "";
            return
                $"[{ProfileName}] Received: {TotalReceived}, Dropped: {TotalDropped} ({DropRate:P1}), Sent: {TotalSent}, Delayed: {TotalDelayed}{natInfo}";
        }
    }
}
