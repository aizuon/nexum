using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Client.Core;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;
using Nexum.Core.Simulation;
using Xunit;
using Xunit.Abstractions;
using P2PGroup = Nexum.Server.P2P.P2PGroup;

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

        public static IEnumerable<object[]> AllNetworkProfiles =>
            new List<object[]>
            {
                new object[] { "Ideal", true },
                new object[] { "HomeWifi", true },
                new object[] { "Mobile4G", true },
                new object[] { "PoorMobile", true },
                new object[] { "PortRestrictedNat", true },
                new object[] { "SymmetricNat", false }
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

        [Fact(Timeout = 180000)]
        public async Task P2PConnection_Jit_InitializedOnFirstPeerRmi()
        {
            var profile = NetworkProfile.GetByName("Ideal");
            SetupNetworkSimulation(profile);

            var netSettings = new NetSettings
            {
                DirectP2PStartCondition = DirectP2PStartCondition.Jit
            };

            Server = await CreateServerAsync(netSettings: netSettings);

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

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            bool becameDirectWithoutRmi = await WaitForConditionAsync(
                () => peer1.DirectP2P || peer2.DirectP2P,
                TimeSpan.FromSeconds(2));

            Assert.False(becameDirectWithoutRmi,
                "Direct P2P should not be auto-initiated when DirectP2PStartCondition is Jit");

            int received = 0;
            var receivedEvent = new ManualResetEventSlim(false);
            client2.OnRmiReceive += (msg, rmiId) =>
            {
                if (rmiId != 7010)
                    return;
                msg.Read(out received);
                receivedEvent.Set();
            };

            var testMessage = new NetMessage();
            testMessage.Write(1234);
            peer1.RmiToPeer(7010, testMessage, reliable: true);

            Assert.True(receivedEvent.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                "Peer RMI should be delivered (relayed initially if needed)");
            Assert.Equal(1234, received);

            await WaitForP2PDirectConnectionAsync(peer1, GetAdjustedTimeout(UdpSetupTimeout));
            await WaitForP2PDirectConnectionAsync(peer2, GetAdjustedTimeout(UdpSetupTimeout));

            Assert.True(peer1.DirectP2PReady, "Peer1 should become DirectP2PReady after first peer RMI in JIT mode");
            Assert.True(peer2.DirectP2PReady, "Peer2 should become DirectP2PReady after first peer RMI in JIT mode");
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

            client1.OnRmiReceive += (msg, _) =>
            {
                msg.Read(out client1Received);
                client1Done.Set();
            };

            client2.OnRmiReceive += (msg, _) =>
            {
                msg.Read(out client2Received);
                client2Done.Set();
            };

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            var peerMessage1 = new NetMessage();
            peerMessage1.Write(111);
            peer1.RmiToPeer(7001, peerMessage1, forceRelay: true, reliable: true);

            var peerMessage2 = new NetMessage();
            peerMessage2.Write(222);
            peer2.RmiToPeer(7002, peerMessage2, forceRelay: true, reliable: true);

            Assert.True(client2Done.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                $"[{profileName}] Client2 should receive relayed message");
            Assert.True(client1Done.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                $"[{profileName}] Client1 should receive relayed message");
            Assert.Equal(111, client2Received);
            Assert.Equal(222, client1Received);

            LogSimulationStatistics();
        }

        [Theory(Timeout = 180000)]
        [MemberData(nameof(AllNetworkProfiles))]
        public async Task P2PMessaging_MessagesDelivered(string profileName, bool expectDirectP2P)
        {
            var profile = NetworkProfile.GetByName(profileName);
            SetupNetworkSimulation(profile);

            var (client1, client2, _) = await SetupTwoClientsInP2PGroupAsync(expectDirectP2P);

            if (!expectDirectP2P)
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

            if (expectDirectP2P)
            {
                Assert.True(peer1.DirectP2P, $"[{profileName}] Peer1 should have direct P2P connection");
                Assert.True(peer2.DirectP2P, $"[{profileName}] Peer2 should have direct P2P connection");
            }
            else
            {
                Output.WriteLine(
                    $"[{profileName}] DirectP2P status - Peer1: {peer1.DirectP2P}, Peer2: {peer2.DirectP2P}");
            }

            int client1Received = 0;
            int client2Received = 0;
            var client1Done = new ManualResetEventSlim(false);
            var client2Done = new ManualResetEventSlim(false);

            client1.OnRmiReceive += (msg, _) =>
            {
                msg.Read(out client1Received);
                client1Done.Set();
            };

            client2.OnRmiReceive += (msg, _) =>
            {
                msg.Read(out client2Received);
                client2Done.Set();
            };

            var peerMessage1 = new NetMessage();
            peerMessage1.Write(333);
            peer1.RmiToPeer(7003, peerMessage1, reliable: true);

            var peerMessage2 = new NetMessage();
            peerMessage2.Write(444);
            peer2.RmiToPeer(7004, peerMessage2, reliable: true);

            Assert.True(client2Done.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                $"[{profileName}] Client2 should receive P2P message");
            Assert.True(client1Done.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                $"[{profileName}] Client1 should receive P2P message");
            Assert.Equal(333, client2Received);
            Assert.Equal(444, client1Received);

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
                GetAdjustedTimeout(MessageTimeout));

            var session2 = Server.Sessions[client2.HostId];
            group.Leave(session2);

            await WaitForConditionAsync(
                () => !client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId),
                GetAdjustedTimeout(MessageTimeout));

            Assert.False(client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId));
        }

        [Fact(Timeout = 90000)]
        public async Task P2PGroup_ClientDisconnects_RemovedFromPeers()
        {
            var (client1, client2, group) = await SetupTwoClientsInP2PGroupAsync();

            await WaitForConditionAsync(
                () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true,
                GetAdjustedTimeout(MessageTimeout));

            client2.Dispose();

            await WaitForConditionAsync(
                () => !client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId),
                GetAdjustedTimeout(MessageTimeout));

            Assert.False(client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId));
            Assert.Single(group.P2PMembers);
        }

        [Fact(Timeout = 90000)]
        public async Task P2PMessaging_EncryptedMessaging_MessagesDelivered()
        {
            var netSettings = new NetSettings
            {
                EnableP2PEncryptedMessaging = true,
                EncryptedMessageKeyLength = 256,
                FastEncryptedMessageKeyLength = 512
            };

            Server = await CreateServerAsync(netSettings: netSettings);

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

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            await WaitForP2PDirectConnectionAsync(peer1, GetAdjustedTimeout(UdpSetupTimeout));
            await WaitForP2PDirectConnectionAsync(peer2, GetAdjustedTimeout(UdpSetupTimeout));

            Assert.True(peer1.DirectP2P, "Peer1 should have direct P2P connection");
            Assert.True(peer2.DirectP2P, "Peer2 should have direct P2P connection");

            int client1Received = 0;
            int client2Received = 0;
            var client1Done = new ManualResetEventSlim(false);
            var client2Done = new ManualResetEventSlim(false);

            client1.OnRmiReceive += (msg, _) =>
            {
                msg.Read(out client1Received);
                client1Done.Set();
            };

            client2.OnRmiReceive += (msg, _) =>
            {
                msg.Read(out client2Received);
                client2Done.Set();
            };

            var peerMessage1 = new NetMessage();
            peerMessage1.Write(555);
            peer1.RmiToPeer(7005, peerMessage1, EncryptMode.Secure, reliable: true);

            var peerMessage2 = new NetMessage();
            peerMessage2.Write(666);
            peer2.RmiToPeer(7006, peerMessage2, reliable: true);

            Assert.True(client2Done.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                "Client2 should receive encrypted P2P message");
            Assert.True(client1Done.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                "Client1 should receive encrypted P2P message");
            Assert.Equal(555, client2Received);
            Assert.Equal(666, client1Received);

            Output.WriteLine($"P2P encrypted communication successful. " +
                             $"Peer1 DirectP2P: {peer1.DirectP2P}, Peer2 DirectP2P: {peer2.DirectP2P}");
        }

        [Fact(Timeout = 180000)]
        public async Task P2PUdpSocketRecycling_ReusesLocalPortAfterPeerLeavesAndRejoins()
        {
            var profile = NetworkProfile.GetByName("Ideal");
            SetupNetworkSimulation(profile);

            Server = await CreateServerAsync(withUdp: true);

            var client1 = await CreateClientAsync();
            Assert.True(await WaitForClientConnectionAsync(client1), "Client1 should connect");

            var client2 = await CreateClientAsync();
            Assert.True(await WaitForClientConnectionAsync(client2), "Client2 should connect");

            var session1 = Server.Sessions[client1.HostId];
            var session2 = Server.Sessions[client2.HostId];

            var group = Server.CreateP2PGroup();
            group.Join(session1);
            group.Join(session2);

            Assert.True(await WaitForClientUdpEnabledAsync(client1, GetAdjustedTimeout(UdpSetupTimeout)),
                "Client1 UDP should be enabled");
            Assert.True(await WaitForClientUdpEnabledAsync(client2, GetAdjustedTimeout(UdpSetupTimeout)),
                "Client2 UDP should be enabled");

            Assert.True(await WaitForConditionAsync(
                    () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true &&
                          client2.P2PGroup?.P2PMembers.ContainsKey(client1.HostId) == true,
                    GetAdjustedTimeout(MessageTimeout)),
                "Both clients should see each other as P2P members");

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            Assert.True(await WaitForP2PDirectConnectionAsync(peer1, GetAdjustedTimeout(UdpSetupTimeout)),
                "Peer1 should establish direct P2P");
            Assert.True(await WaitForP2PDirectConnectionAsync(peer2, GetAdjustedTimeout(UdpSetupTimeout)),
                "Peer2 should establish direct P2P");

            Assert.True(peer1.DirectP2PReady, "Peer1 should be DirectP2PReady");
            Assert.True(peer2.DirectP2PReady, "Peer2 should be DirectP2PReady");

            int originalPort = peer1.SelfUdpLocalSocket?.Port ?? 0;
            Assert.True(originalPort > 0, "Peer1 should have a valid P2P local UDP port");

            group.Leave(session2);

            Assert.True(await WaitForConditionAsync(
                    () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == false,
                    GetAdjustedTimeout(MessageTimeout)),
                "Client1 should remove client2 from P2P members after leave");

            int originalPortKey = originalPort;

            Assert.True(await WaitForConditionAsync(
                    () => client1.RecycledSockets.ContainsKey(originalPortKey),
                    GetAdjustedTimeout(MessageTimeout)),
                $"Client1 should recycle its P2P UDP socket on port {originalPort}");

            Assert.True(client1.RecycledSockets.TryGetValue(originalPortKey, out var recycled),
                "Recycled socket should be present");
            Assert.NotNull(recycled);
            Assert.NotNull(recycled.Channel);
            Assert.True(recycled.Channel.Active, "Recycled UDP channel should remain active");

            group.Join(session2);

            Assert.True(await WaitForConditionAsync(
                    () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true &&
                          client2.P2PGroup?.P2PMembers.ContainsKey(client1.HostId) == true,
                    GetAdjustedTimeout(MessageTimeout)),
                "After re-join, both clients should see each other as P2P members");

            var peer1B = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2B = client2.P2PGroup.P2PMembers[client1.HostId];

            Assert.True(await WaitForP2PDirectConnectionAsync(peer1B, GetAdjustedTimeout(UdpSetupTimeout)),
                "Peer1 should re-establish direct P2P after re-join");
            Assert.True(await WaitForP2PDirectConnectionAsync(peer2B, GetAdjustedTimeout(UdpSetupTimeout)),
                "Peer2 should re-establish direct P2P after re-join");

            Assert.True(peer1B.DirectP2PReady, "Peer1 should be DirectP2PReady after re-join");
            Assert.True(peer1B.LocalPortReuseSuccess,
                "Peer1 should report successful local UDP port reuse for P2P after recycle");
            Assert.Equal(originalPort, peer1B.SelfUdpLocalSocket?.Port ?? 0);

            Assert.False(client1.RecycledSockets.ContainsKey(originalPortKey),
                "Recycled socket entry should be consumed when the port is reused");
        }

        [Fact(Timeout = 180000)]
        public async Task P2PRecycleComplete_RecycledTrue_RestoresDirectP2PWithoutHolepunch()
        {
            var profile = NetworkProfile.GetByName("Ideal");
            SetupNetworkSimulation(profile);

            Server = await CreateServerAsync(withUdp: true);

            var client1 = await CreateClientAsync();
            Assert.True(await WaitForClientConnectionAsync(client1), "Client1 should connect");

            var client2 = await CreateClientAsync();
            Assert.True(await WaitForClientConnectionAsync(client2), "Client2 should connect");

            var session1 = Server.Sessions[client1.HostId];
            var session2 = Server.Sessions[client2.HostId];

            var group = Server.CreateP2PGroup();
            group.Join(session1);
            group.Join(session2);

            Assert.True(await WaitForClientUdpEnabledAsync(client1, GetAdjustedTimeout(UdpSetupTimeout)),
                "Client1 UDP should be enabled");
            Assert.True(await WaitForClientUdpEnabledAsync(client2, GetAdjustedTimeout(UdpSetupTimeout)),
                "Client2 UDP should be enabled");

            Assert.True(await WaitForConditionAsync(
                    () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true &&
                          client2.P2PGroup?.P2PMembers.ContainsKey(client1.HostId) == true,
                    GetAdjustedTimeout(MessageTimeout)),
                "Both clients should see each other as P2P members");

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            Assert.True(await WaitForP2PDirectConnectionAsync(peer1, GetAdjustedTimeout(UdpSetupTimeout)),
                "Peer1 should establish direct P2P");
            Assert.True(await WaitForP2PDirectConnectionAsync(peer2, GetAdjustedTimeout(UdpSetupTimeout)),
                "Peer2 should establish direct P2P");

            var previousPeer1SendAddr = peer1.PeerLocalToRemoteSocket;
            var previousPeer1RecvAddr = peer1.PeerRemoteToLocalSocket;
            Assert.NotNull(previousPeer1SendAddr);
            Assert.NotNull(previousPeer1RecvAddr);

            int peer1Port = peer1.SelfUdpLocalSocket?.Port ?? 0;
            int peer2Port = peer2.SelfUdpLocalSocket?.Port ?? 0;
            Assert.True(peer1Port > 0, "Peer1 should have a valid P2P local UDP port");
            Assert.True(peer2Port > 0, "Peer2 should have a valid P2P local UDP port");

            group.Leave(session2);

            Assert.True(await WaitForConditionAsync(
                    () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == false,
                    GetAdjustedTimeout(MessageTimeout)),
                "Client1 should remove client2 from P2P members after leave");

            Assert.True(await WaitForConditionAsync(
                    () => client1.RecycledSockets.ContainsKey(peer1Port) &&
                          client2.RecycledSockets.ContainsKey(peer2Port),
                    GetAdjustedTimeout(MessageTimeout)),
                "Both clients should recycle their P2P UDP sockets after leave");

            group.Join(session2);

            Assert.True(await WaitForConditionAsync(
                    () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true &&
                          client2.P2PGroup?.P2PMembers.ContainsKey(client1.HostId) == true,
                    GetAdjustedTimeout(MessageTimeout)),
                "After re-join, both clients should see each other as P2P members");

            var peer1B = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2B = client2.P2PGroup.P2PMembers[client1.HostId];

            Assert.True(await WaitForConditionAsync(
                    () => peer1B.DirectP2PReady && peer2B.DirectP2PReady,
                    TimeSpan.FromSeconds(2)),
                "Direct P2P should be restored quickly via recycle without a new holepunch");

            Assert.False(peer1B.P2PHolepunchInitiated, "Recycled=true should not start a new holepunch (peer1)");
            Assert.False(peer2B.P2PHolepunchInitiated, "Recycled=true should not start a new holepunch (peer2)");

            Assert.Equal(previousPeer1SendAddr, peer1B.PeerLocalToRemoteSocket);
            Assert.Equal(previousPeer1RecvAddr, peer1B.PeerRemoteToLocalSocket);

            Assert.True(peer1B.LocalPortReuseSuccess, "Peer1 should reuse its local P2P UDP port");
            Assert.True(peer2B.LocalPortReuseSuccess, "Peer2 should reuse its local P2P UDP port");
        }
    }
}
