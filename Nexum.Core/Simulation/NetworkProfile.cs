namespace Nexum.Core.Simulation
{
    public class NetworkProfile
    {
        public string Name { get; init; } = "Default";

        public int LatencyMs { get; init; }

        public int JitterMs { get; init; }

        public double PacketLossRate { get; init; }

        public double PacketDuplicateRate { get; init; }

        public double PacketReorderRate { get; init; }

        public int ReorderDelayMs { get; init; } = 50;

        public double PacketCorruptionRate { get; init; }

        public int BandwidthLimitBytesPerSecond { get; init; }

        public double BurstLossStartProbability { get; init; }

        public double BurstLossContinueProbability { get; init; }

        public int? RandomSeed { get; init; }

        public bool SimulateInbound { get; init; } = true;

        public bool SimulateOutbound { get; init; } = true;

        public NatType NatType { get; init; } = NatType.None;

        public bool IsIdeal => LatencyMs == 0 && JitterMs == 0 && PacketLossRate == 0 &&
                               PacketReorderRate == 0 && PacketDuplicateRate == 0 &&
                               PacketCorruptionRate == 0 && BandwidthLimitBytesPerSecond == 0 &&
                               BurstLossStartProbability == 0 && NatType == NatType.None;

        public override string ToString()
        {
            return Name;
        }

        #region Predefined Profiles

        public static NetworkProfile Ideal =>
            new NetworkProfile
            {
                Name = "Ideal",
                LatencyMs = 0,
                JitterMs = 0,
                PacketLossRate = 0
            };

        public static NetworkProfile HomeWifi =>
            new NetworkProfile
            {
                Name = "Home WiFi",
                LatencyMs = 10,
                JitterMs = 5,
                PacketLossRate = 0.005,
                PacketReorderRate = 0.001
            };

        public static NetworkProfile Mobile4G =>
            new NetworkProfile
            {
                Name = "Mobile 4G",
                LatencyMs = 50,
                JitterMs = 25,
                PacketLossRate = 0.02,
                PacketReorderRate = 0.01,
                BurstLossStartProbability = 0.005,
                BurstLossContinueProbability = 0.3
            };

        public static NetworkProfile PoorMobile =>
            new NetworkProfile
            {
                Name = "Poor Mobile",
                LatencyMs = 150,
                JitterMs = 75,
                PacketLossRate = 0.08,
                PacketReorderRate = 0.05,
                BurstLossStartProbability = 0.02,
                BurstLossContinueProbability = 0.5
            };

        public static NetworkProfile CongestedWifi =>
            new NetworkProfile
            {
                Name = "Congested WiFi",
                LatencyMs = 30,
                JitterMs = 40,
                PacketLossRate = 0.05,
                PacketReorderRate = 0.03,
                PacketDuplicateRate = 0.01
            };

        public static NetworkProfile StressLossy =>
            new NetworkProfile
            {
                Name = "Stress Lossy",
                LatencyMs = 50,
                JitterMs = 30,
                PacketLossRate = 0.20,
                PacketReorderRate = 0.10,
                ReorderDelayMs = 100
            };

        public static NetworkProfile HighLatency =>
            new NetworkProfile
            {
                Name = "High Latency",
                LatencyMs = 300,
                JitterMs = 50,
                PacketLossRate = 0.01
            };

        public static NetworkProfile BurstyLoss =>
            new NetworkProfile
            {
                Name = "Bursty Loss",
                LatencyMs = 40,
                JitterMs = 20,
                PacketLossRate = 0.01,
                BurstLossStartProbability = 0.05,
                BurstLossContinueProbability = 0.7
            };

        public static NetworkProfile SymmetricNat =>
            new NetworkProfile
            {
                Name = "Symmetric NAT",
                LatencyMs = 20,
                JitterMs = 10,
                PacketLossRate = 0.01,
                NatType = NatType.Symmetric
            };

        public static NetworkProfile PortRestrictedNat =>
            new NetworkProfile
            {
                Name = "Port Restricted NAT",
                LatencyMs = 15,
                JitterMs = 8,
                PacketLossRate = 0.005,
                NatType = NatType.PortRestricted
            };

        public static NetworkProfile AddressRestrictedNat =>
            new NetworkProfile
            {
                Name = "Address Restricted NAT",
                LatencyMs = 15,
                JitterMs = 8,
                PacketLossRate = 0.005,
                NatType = NatType.AddressRestricted
            };

        public static NetworkProfile FullConeNat =>
            new NetworkProfile
            {
                Name = "Full Cone NAT",
                LatencyMs = 15,
                JitterMs = 5,
                PacketLossRate = 0.002,
                NatType = NatType.FullCone
            };

        public static NetworkProfile[] All => new[]
        {
            Ideal, HomeWifi, Mobile4G, PoorMobile, CongestedWifi,
            StressLossy, HighLatency, BurstyLoss
        };

        public static NetworkProfile[] Common => new[]
        {
            Ideal, HomeWifi, Mobile4G
        };

        public static NetworkProfile[] NatProfiles => new[]
        {
            FullConeNat, AddressRestrictedNat, PortRestrictedNat, SymmetricNat
        };

        public static NetworkProfile GetByName(string name)
        {
            return name switch
            {
                "Ideal" => Ideal,
                "Home WiFi" or "HomeWifi" => HomeWifi,
                "Mobile 4G" or "Mobile4G" => Mobile4G,
                "Poor Mobile" or "PoorMobile" => PoorMobile,
                "Congested WiFi" or "CongestedWifi" => CongestedWifi,
                "Stress Lossy" or "StressLossy" => StressLossy,
                "High Latency" or "HighLatency" => HighLatency,
                "Bursty Loss" or "BurstyLoss" => BurstyLoss,
                "Symmetric NAT" or "SymmetricNat" => SymmetricNat,
                "Port Restricted NAT" or "PortRestrictedNat" => PortRestrictedNat,
                "Address Restricted NAT" or "AddressRestrictedNat" => AddressRestrictedNat,
                "Full Cone NAT" or "FullConeNat" => FullConeNat,
                _ => Ideal
            };
        }

        #endregion
    }

    public enum NatType
    {
        None,

        FullCone,

        AddressRestricted,

        PortRestricted,

        Symmetric
    }
}
