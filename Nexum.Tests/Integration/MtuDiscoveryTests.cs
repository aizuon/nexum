using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nexum.Client;
using Nexum.Core;
using Nexum.Core.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Nexum.Tests.Integration
{
    [Collection("Integration")]
    public class MtuDiscoveryTests : IntegrationTestBase
    {
        public MtuDiscoveryTests(ITestOutputHelper output) : base(output)
        {
        }

        public static IEnumerable<object[]> NetworkProfiles =>
            new List<object[]>
            {
                new object[] { "Ideal" },
                new object[] { "HomeWifi" },
                new object[] { "Mobile4G" }
            };

        private async Task<int> WaitForMtuDiscoveryAsync(NetClient client, int maxWaitSeconds = 90)
        {
            int initialMtu = client.ServerMtuDiscovery.ConfirmedMtu;
            Output.WriteLine($"Initial MTU: {initialMtu}");

            int lastLoggedMtu = initialMtu;

            for (int i = 0; i < maxWaitSeconds; i++)
            {
                await Task.Delay(1000);

                int currentMtu = client.ServerMtuDiscovery.ConfirmedMtu;
                bool isComplete = client.ServerMtuDiscovery.IsDiscoveryComplete;

                if (currentMtu != lastLoggedMtu)
                {
                    Output.WriteLine($"[{i + 1}s] MTU: {lastLoggedMtu} -> {currentMtu}");
                    lastLoggedMtu = currentMtu;
                }

                if (isComplete)
                {
                    Output.WriteLine($"[{i + 1}s] Complete. Final MTU: {currentMtu}");
                    return currentMtu;
                }
            }

            return client.ServerMtuDiscovery.ConfirmedMtu;
        }

        private async Task<(int peer1Mtu, int peer2Mtu)> WaitForP2PMtuDiscoveryAsync(
            P2PMember peer1, P2PMember peer2, int maxWaitSeconds = 90)
        {
            int lastPeer1Mtu = peer1.MtuDiscovery.ConfirmedMtu;
            int lastPeer2Mtu = peer2.MtuDiscovery.ConfirmedMtu;

            for (int i = 0; i < maxWaitSeconds; i++)
            {
                await Task.Delay(1000);

                int peer1Mtu = peer1.MtuDiscovery.ConfirmedMtu;
                int peer2Mtu = peer2.MtuDiscovery.ConfirmedMtu;

                if (peer1Mtu != lastPeer1Mtu)
                {
                    Output.WriteLine($"[{i + 1}s] Peer1 MTU: {lastPeer1Mtu} -> {peer1Mtu}");
                    lastPeer1Mtu = peer1Mtu;
                }

                if (peer2Mtu != lastPeer2Mtu)
                {
                    Output.WriteLine($"[{i + 1}s] Peer2 MTU: {lastPeer2Mtu} -> {peer2Mtu}");
                    lastPeer2Mtu = peer2Mtu;
                }

                if (peer1.MtuDiscovery.IsDiscoveryComplete && peer2.MtuDiscovery.IsDiscoveryComplete)
                {
                    Output.WriteLine($"[{i + 1}s] Both complete. Peer1: {peer1Mtu}, Peer2: {peer2Mtu}");
                    return (peer1Mtu, peer2Mtu);
                }
            }

            return (peer1.MtuDiscovery.ConfirmedMtu, peer2.MtuDiscovery.ConfirmedMtu);
        }

        [Theory(Timeout = 120000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task MtuDiscovery_ClientServer_DiscoversOptimalMtu(string profileName)
        {
            var profile = NetworkProfile.GetByName(profileName);
            SetupNetworkSimulation(profile);

            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var group = Server.CreateP2PGroup();
            group.Join(session);

            await WaitForClientUdpEnabledAsync(client, GetAdjustedTimeout(UdpSetupTimeout));

            int initialMtu = client.ServerMtuDiscovery.ConfirmedMtu;
            int finalMtu = await WaitForMtuDiscoveryAsync(client);

            Assert.True(client.ServerMtuDiscovery.IsDiscoveryComplete,
                $"[{profileName}] MTU discovery should complete");
            Assert.True(finalMtu > initialMtu,
                $"[{profileName}] Final MTU ({finalMtu}) should exceed initial ({initialMtu})");
            Assert.InRange(finalMtu, MtuConfig.MinMtu, MtuConfig.MaxMtu);

            LogSimulationStatistics();
        }

        [Theory(Timeout = 180000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task MtuDiscovery_P2P_BothPeersDiscoverOptimalMtu(string profileName)
        {
            var profile = NetworkProfile.GetByName(profileName);
            SetupNetworkSimulation(profile);

            Server = await CreateServerAsync();

            var client1 = await CreateClientAsync();
            await WaitForClientConnectionAsync(client1);

            var client2 = await CreateClientAsync();
            await WaitForClientConnectionAsync(client2);

            var session1 = Server.Sessions[client1.HostId];
            var session2 = Server.Sessions[client2.HostId];
            var group = Server.CreateP2PGroup();

            group.Join(session1);
            group.Join(session2);

            await WaitForClientUdpEnabledAsync(client1, GetAdjustedTimeout(UdpSetupTimeout));
            await WaitForClientUdpEnabledAsync(client2, GetAdjustedTimeout(UdpSetupTimeout));

            await WaitForConditionAsync(
                () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true &&
                      client2.P2PGroup?.P2PMembers.ContainsKey(client1.HostId) == true,
                GetAdjustedTimeout(MessageTimeout));

            var peer1To2 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2To1 = client2.P2PGroup.P2PMembers[client1.HostId];

            await WaitForConditionAsync(
                () => peer1To2.DirectP2P && peer2To1.DirectP2P,
                GetAdjustedTimeout(TimeSpan.FromSeconds(30)));

            (int peer1Mtu, int peer2Mtu) = await WaitForP2PMtuDiscoveryAsync(peer1To2, peer2To1);

            Assert.True(peer1To2.MtuDiscovery.IsDiscoveryComplete,
                $"[{profileName}] Peer1 MTU discovery should complete");
            Assert.True(peer2To1.MtuDiscovery.IsDiscoveryComplete,
                $"[{profileName}] Peer2 MTU discovery should complete");
            Assert.True(peer1Mtu > MtuConfig.DefaultMtu,
                $"[{profileName}] Peer1 MTU ({peer1Mtu}) should exceed default ({MtuConfig.DefaultMtu})");
            Assert.True(peer2Mtu > MtuConfig.DefaultMtu,
                $"[{profileName}] Peer2 MTU ({peer2Mtu}) should exceed default ({MtuConfig.DefaultMtu})");
            Assert.InRange(peer1Mtu, MtuConfig.MinMtu, MtuConfig.MaxMtu);
            Assert.InRange(peer2Mtu, MtuConfig.MinMtu, MtuConfig.MaxMtu);

            LogSimulationStatistics();
        }
    }
}
