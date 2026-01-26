using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;
using Nexum.Core.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Nexum.Tests.Integration
{
    [Collection("Integration")]
    public class UdpReconnectionTests : IntegrationTestBase
    {
        private static readonly TimeSpan UdpTimeoutWait = TimeSpan.FromSeconds(
            Math.Max(ReliableUdpConfig.FallbackServerUdpToTcpTimeout, ReliableUdpConfig.FallbackP2PUdpToTcpTimeout) +
            10);

        public UdpReconnectionTests(ITestOutputHelper output) : base(output)
        {
        }

        public static IEnumerable<object[]> NetworkProfiles =>
            new List<object[]>
            {
                new object[] { "Ideal" },
                new object[] { "HomeWifi" },
                new object[] { "Mobile4G" }
            };

        [Theory(Timeout = 120000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task ClientServerUdp_ConnectionDies_AutomaticallyRestored(string profileName)
        {
            var profile = NetworkProfile.GetByName(profileName);
            SetupNetworkSimulation(profile);

            Server = await CreateServerAsync(withUdp: true);
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var group = Server.CreateP2PGroup();
            group.Join(session);

            await WaitForClientUdpEnabledAsync(client, GetAdjustedTimeout(UdpSetupTimeout));
            await WaitForSessionUdpEnabledAsync(session, GetAdjustedTimeout(UdpSetupTimeout));

            Output.WriteLine($"[{profileName}] Initial client-server UDP connection established");

            var initialUdpChannel = client.UdpChannel;
            Assert.NotNull(initialUdpChannel);

            var clientFallbackDetected = new ManualResetEventSlim(false);
            client.OnUdpDisconnected += () => { clientFallbackDetected.Set(); };

            Output.WriteLine($"[{profileName}] Closing client UDP channel to simulate connection death...");
            await client.UdpChannel.CloseAsync();

            Output.WriteLine($"[{profileName}] Waiting for client to detect UDP timeout..");

            bool clientDetectedTimeout = clientFallbackDetected.Wait(GetAdjustedTimeout(UdpTimeoutWait));

            Assert.True(clientDetectedTimeout, $"[{profileName}] Client should detect UDP timeout");
            Output.WriteLine($"[{profileName}] Client detected timeout and initiated fallback");

            await WaitForClientUdpEnabledAsync(client, GetAdjustedTimeout(ConnectionTimeout));
            await WaitForSessionUdpEnabledAsync(session, GetAdjustedTimeout(ConnectionTimeout));

            Output.WriteLine($"[{profileName}] UDP connection restored");

            Assert.NotNull(client.UdpChannel);
            Assert.NotSame(initialUdpChannel, client.UdpChannel);

            int receivedValue = 0;
            var received = new ManualResetEventSlim(false);
            Server.OnRmiReceive += (_, msg, _) =>
            {
                msg.Read(out receivedValue);
                received.Set();
            };

            var testMessage = new NetMessage();
            testMessage.Write(12345);
            client.RmiToServerUdpIfAvailable(9001, testMessage, reliable: true);

            Assert.True(received.Wait(GetAdjustedTimeout(MessageTimeout)),
                $"[{profileName}] Server should receive UDP message");
            Assert.Equal(12345, receivedValue);

            Output.WriteLine($"[{profileName}] Client-server UDP messaging verified after reconnection");
            LogSimulationStatistics();
        }

        [Theory(Timeout = 180000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task P2PUdp_ConnectionDies_AutomaticallyRestored(string profileName)
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
                GetAdjustedTimeout(ConnectionTimeout));

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            await WaitForP2PDirectConnectionAsync(peer1, GetAdjustedTimeout(UdpSetupTimeout));
            await WaitForP2PDirectConnectionAsync(peer2, GetAdjustedTimeout(UdpSetupTimeout));

            Output.WriteLine($"[{profileName}] Initial P2P connection established between clients");

            Assert.True(peer1.DirectP2P, $"[{profileName}] Peer1 should have direct P2P");
            Assert.True(peer2.DirectP2P, $"[{profileName}] Peer2 should have direct P2P");

            var peer1FallbackDetected = new ManualResetEventSlim(false);
            client1.OnP2PMemberDirectDisconnected += hostId =>
            {
                if (hostId == client2.HostId)
                    peer1FallbackDetected.Set();
            };

            Output.WriteLine($"[{profileName}] Closing peer2's P2P UDP channel to simulate connection death...");
            await peer2.PeerUdpChannel.CloseAsync();

            Output.WriteLine($"[{profileName}] Waiting for peer1 to detect P2P UDP timeout...");

            bool peer1DetectedTimeout = peer1FallbackDetected.Wait(GetAdjustedTimeout(UdpTimeoutWait));

            Assert.True(peer1DetectedTimeout,
                $"[{profileName}] Peer1 should detect P2P timeout and fall back to relay");
            Output.WriteLine($"[{profileName}] Peer1 detected timeout and initiated fallback");

            await WaitForP2PDirectConnectionAsync(peer1, GetAdjustedTimeout(ConnectionTimeout));
            await WaitForP2PDirectConnectionAsync(peer2, GetAdjustedTimeout(ConnectionTimeout));

            Output.WriteLine($"[{profileName}] P2P connection restored");

            Assert.True(peer1.DirectP2P, $"[{profileName}] Peer1 should have direct P2P after reconnection");
            Assert.True(peer2.DirectP2P, $"[{profileName}] Peer2 should have direct P2P after reconnection");

            int receivedValue = 0;
            var received = new ManualResetEventSlim(false);
            client2.OnRmiReceive += (msg, _) =>
            {
                msg.Read(out receivedValue);
                received.Set();
            };

            var testMessage = new NetMessage();
            testMessage.Write(67890);
            peer1.RmiToPeer(9002, testMessage, forceRelay: false, reliable: true);

            Assert.True(received.Wait(GetAdjustedTimeout(MessageTimeout)),
                $"[{profileName}] Client2 should receive P2P message");
            Assert.Equal(67890, receivedValue);

            Output.WriteLine($"[{profileName}] P2P messaging verified after reconnection");
            LogSimulationStatistics();
        }
    }
}
