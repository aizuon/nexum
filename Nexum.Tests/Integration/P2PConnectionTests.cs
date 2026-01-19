using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Client;
using Nexum.Core;
using Nexum.Core.Simulation;
using Xunit;
using Xunit.Abstractions;
using P2PGroup = Nexum.Server.P2PGroup;

namespace Nexum.Tests.Integration
{
    [Collection("Integration")]
    public class P2PConnectionTests : IntegrationTestBase
    {
        public P2PConnectionTests(ITestOutputHelper output) : base(output)
        {
        }

        public static IEnumerable<object[]> NetworkProfiles =>
            new List<object[]>
            {
                new object[] { "Ideal" },
                new object[] { "HomeWifi" },
                new object[] { "Mobile4G" },
                new object[] { "PoorMobile" }
            };

        public static IEnumerable<object[]> NatProfiles =>
            new List<object[]>
            {
                new object[] { "PortRestrictedNat" },
                new object[] { "SymmetricNat" }
            };

        private async Task<(NetClient client1, NetClient client2, P2PGroup group)>
            SetupTwoClientsInP2PGroupAsync(bool waitForDirectP2P = false)
        {
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

            if (waitForDirectP2P)
            {
                await WaitForConditionAsync(
                    () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true &&
                          client2.P2PGroup?.P2PMembers.ContainsKey(client1.HostId) == true,
                    GetAdjustedTimeout(MessageTimeout));

                var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
                var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

                await WaitForP2PDirectConnectionAsync(peer1, GetAdjustedTimeout(UdpSetupTimeout));
                await WaitForP2PDirectConnectionAsync(peer2, GetAdjustedTimeout(UdpSetupTimeout));
            }

            return (client1, client2, group);
        }

        [Theory(Timeout = 180000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task P2PConnection_Holepunch_DirectP2PEstablished(string profileName)
        {
            var profile = NetworkProfile.GetByName(profileName);
            SetupNetworkSimulation(profile);

            var (client1, client2, _) = await SetupTwoClientsInP2PGroupAsync();

            await WaitForConditionAsync(
                () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true &&
                      client2.P2PGroup?.P2PMembers.ContainsKey(client1.HostId) == true,
                GetAdjustedTimeout(MessageTimeout));

            Assert.True(client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId));
            Assert.True(client2.P2PGroup.P2PMembers.ContainsKey(client1.HostId));

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            await WaitForP2PDirectConnectionAsync(peer1, GetAdjustedTimeout(UdpSetupTimeout));
            await WaitForP2PDirectConnectionAsync(peer2, GetAdjustedTimeout(UdpSetupTimeout));

            Assert.True(peer1.DirectP2P, $"[{profileName}] Peer1 should have direct P2P connection");
            Assert.True(peer2.DirectP2P, $"[{profileName}] Peer2 should have direct P2P connection");
            Assert.True(peer1.DirectP2PReady, $"[{profileName}] Peer1 should be DirectP2PReady");
            Assert.True(peer2.DirectP2PReady, $"[{profileName}] Peer2 should be DirectP2PReady");

            LogSimulationStatistics();
        }

        [Theory(Timeout = 180000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task P2PMessaging_Relayed_MessagesDelivered(string profileName)
        {
            var profile = NetworkProfile.GetByName(profileName);
            SetupNetworkSimulation(profile);

            var (client1, client2, _) = await SetupTwoClientsInP2PGroupAsync();

            await WaitForConditionAsync(
                () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true &&
                      client2.P2PGroup?.P2PMembers.ContainsKey(client1.HostId) == true,
                GetAdjustedTimeout(MessageTimeout));

            int client1Received = 0;
            int client2Received = 0;
            var client1Done = new ManualResetEventSlim(false);
            var client2Done = new ManualResetEventSlim(false);

            client1.OnRMIReceive += (msg, _) =>
            {
                msg.Read(out client1Received);
                client1Done.Set();
            };

            client2.OnRMIReceive += (msg, _) =>
            {
                msg.Read(out client2Received);
                client2Done.Set();
            };

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            var msg1 = new NetMessage();
            msg1.Write(111);
            peer1.RmiToPeer(7001, msg1, true, true);

            var msg2 = new NetMessage();
            msg2.Write(222);
            peer2.RmiToPeer(7002, msg2, true, true);

            Assert.True(client2Done.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                $"[{profileName}] Client2 should receive relayed message");
            Assert.True(client1Done.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                $"[{profileName}] Client1 should receive relayed message");
            Assert.Equal(111, client2Received);
            Assert.Equal(222, client1Received);

            LogSimulationStatistics();
        }

        [Theory(Timeout = 180000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task P2PMessaging_Direct_MessagesDelivered(string profileName)
        {
            var profile = NetworkProfile.GetByName(profileName);
            SetupNetworkSimulation(profile);

            var (client1, client2, _) = await SetupTwoClientsInP2PGroupAsync(true);

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            Assert.True(peer1.DirectP2P);
            Assert.True(peer2.DirectP2P);

            int client1Received = 0;
            int client2Received = 0;
            var client1Done = new ManualResetEventSlim(false);
            var client2Done = new ManualResetEventSlim(false);

            client1.OnRMIReceive += (msg, _) =>
            {
                msg.Read(out client1Received);
                client1Done.Set();
            };

            client2.OnRMIReceive += (msg, _) =>
            {
                msg.Read(out client2Received);
                client2Done.Set();
            };

            var msg1 = new NetMessage();
            msg1.Write(333);
            peer1.RmiToPeer(7003, msg1);

            var msg2 = new NetMessage();
            msg2.Write(444);
            peer2.RmiToPeer(7004, msg2);

            Assert.True(client2Done.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                $"[{profileName}] Client2 should receive direct P2P message");
            Assert.True(client1Done.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                $"[{profileName}] Client1 should receive direct P2P message");
            Assert.Equal(333, client2Received);
            Assert.Equal(444, client1Received);

            LogSimulationStatistics();
        }

        [Theory(Timeout = 120000)]
        [MemberData(nameof(NatProfiles))]
        public async Task P2PConnection_WithNat_StillCommunicates(string profileName)
        {
            var profile = NetworkProfile.GetByName(profileName);
            SetupNetworkSimulation(profile);

            var (client1, client2, _) = await SetupTwoClientsInP2PGroupAsync();

            await WaitForConditionAsync(
                () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true &&
                      client2.P2PGroup?.P2PMembers.ContainsKey(client1.HostId) == true,
                GetAdjustedTimeout(MessageTimeout));

            Assert.True(client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId),
                $"[{profileName}] Client1 should see client2 as P2P peer");
            Assert.True(client2.P2PGroup.P2PMembers.ContainsKey(client1.HostId),
                $"[{profileName}] Client2 should see client1 as P2P peer");

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            int client1Received = 0;
            int client2Received = 0;
            var client1Done = new ManualResetEventSlim(false);
            var client2Done = new ManualResetEventSlim(false);

            client1.OnRMIReceive += (msg, _) =>
            {
                msg.Read(out client1Received);
                client1Done.Set();
            };

            client2.OnRMIReceive += (msg, _) =>
            {
                msg.Read(out client2Received);
                client2Done.Set();
            };

            var msg1 = new NetMessage();
            msg1.Write(555);
            peer1.RmiToPeer(7010, msg1, true, true);

            var msg2 = new NetMessage();
            msg2.Write(666);
            peer2.RmiToPeer(7011, msg2, true, true);

            Assert.True(client2Done.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                $"[{profileName}] Client2 should receive P2P message (possibly relayed)");
            Assert.True(client1Done.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                $"[{profileName}] Client1 should receive P2P message (possibly relayed)");
            Assert.Equal(555, client2Received);
            Assert.Equal(666, client1Received);

            Output.WriteLine($"[{profileName}] P2P communication successful. " +
                             $"Peer1 DirectP2P: {peer1.DirectP2P}, Peer2 DirectP2P: {peer2.DirectP2P}");
            LogSimulationStatistics();
        }

        [Fact(Timeout = 90000)]
        public async Task P2PGroup_ClientLeaves_RemovedFromPeers()
        {
            var (client1, client2, group) = await SetupTwoClientsInP2PGroupAsync();

            await WaitForConditionAsync(
                () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true,
                MessageTimeout);

            var session2 = Server.Sessions[client2.HostId];
            group.Leave(session2);

            await WaitForConditionAsync(
                () => !client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId),
                MessageTimeout);

            Assert.False(client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId));
        }

        [Fact(Timeout = 90000)]
        public async Task P2PGroup_ClientDisconnects_RemovedFromPeers()
        {
            var (client1, client2, group) = await SetupTwoClientsInP2PGroupAsync();

            await WaitForConditionAsync(
                () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true,
                MessageTimeout);

            client2.Dispose();

            await WaitForConditionAsync(
                () => !client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId),
                MessageTimeout);

            Assert.False(client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId));
            Assert.Single(group.P2PMembers);
        }
    }
}
