using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Core;
using Nexum.Core.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Nexum.Tests.Integration
{
    [Collection("Integration")]
    public class UdpFragmentationTests : IntegrationTestBase
    {
        public UdpFragmentationTests(ITestOutputHelper output) : base(output)
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

        [Theory(Timeout = 180000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task Fragmentation_ClientServer_LargePayloadReassembled(string profileName)
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

            byte[] receivedData = null;
            var messageReceived = new ManualResetEventSlim(false);

            Server.OnRMIReceive += (_, msg, _) =>
            {
                var receivedPayload = new ByteArray();
                msg.Read(ref receivedPayload);
                receivedData = receivedPayload.GetBuffer();
                messageReceived.Set();
            };

            byte[] largePayload = new byte[FragmentConfig.MtuLength * 3 + 500];
            Random.Shared.NextBytes(largePayload);

            var testMessage = new NetMessage();
            testMessage.Write(new ByteArray(largePayload));
            client.RmiToServerUdpIfAvailable(7001, testMessage, reliable: true);

            Assert.True(messageReceived.Wait(GetAdjustedTimeout(MessageTimeout)),
                $"[{profileName}] Large payload should be received");
            Assert.Equal(largePayload, receivedData);

            LogSimulationStatistics();
        }

        [Theory(Timeout = 180000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task Fragmentation_ClientServer_Bidirectional(string profileName)
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
            await WaitForSessionUdpEnabledAsync(session, GetAdjustedTimeout(UdpSetupTimeout));

            byte[] serverReceivedData = null;
            byte[] clientReceivedData = null;
            var serverReceived = new ManualResetEventSlim(false);
            var clientReceived = new ManualResetEventSlim(false);

            Server.OnRMIReceive += (_, msg, _) =>
            {
                var receivedPayload = new ByteArray();
                msg.Read(ref receivedPayload);
                serverReceivedData = receivedPayload.GetBuffer();
                serverReceived.Set();
            };

            client.OnRMIReceive += (msg, _) =>
            {
                var receivedPayload = new ByteArray();
                msg.Read(ref receivedPayload);
                clientReceivedData = receivedPayload.GetBuffer();
                clientReceived.Set();
            };

            byte[] clientPayload = new byte[FragmentConfig.MtuLength * 3 + 500];
            byte[] serverPayload = new byte[FragmentConfig.MtuLength * 4 + 500];
            Random.Shared.NextBytes(clientPayload);
            Random.Shared.NextBytes(serverPayload);

            var clientMessage = new NetMessage();
            clientMessage.Write(new ByteArray(clientPayload));
            client.RmiToServerUdpIfAvailable(7002, clientMessage, reliable: true);

            var serverMessage = new NetMessage();
            serverMessage.Write(new ByteArray(serverPayload));
            session.RmiToClientUdpIfAvailable(7003, serverMessage, reliable: true);

            Assert.True(serverReceived.Wait(GetAdjustedTimeout(MessageTimeout)),
                $"[{profileName}] Server should receive fragmented message");
            Assert.True(clientReceived.Wait(GetAdjustedTimeout(MessageTimeout)),
                $"[{profileName}] Client should receive fragmented message");
            Assert.Equal(clientPayload, serverReceivedData);
            Assert.Equal(serverPayload, clientReceivedData);

            LogSimulationStatistics();
        }

        [Theory(Timeout = 180000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task Fragmentation_P2PDirect_LargePayloadReassembled(string profileName)
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

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            await WaitForP2PDirectConnectionAsync(peer1, GetAdjustedTimeout(UdpSetupTimeout));
            await WaitForP2PDirectConnectionAsync(peer2, GetAdjustedTimeout(UdpSetupTimeout));

            byte[] receivedData = null;
            var messageReceived = new ManualResetEventSlim(false);

            client2.OnRMIReceive += (msg, _) =>
            {
                var receivedPayload = new ByteArray();
                msg.Read(ref receivedPayload);
                receivedData = receivedPayload.GetBuffer();
                messageReceived.Set();
            };

            byte[] largePayload = new byte[FragmentConfig.MtuLength * 3 + 500];
            Random.Shared.NextBytes(largePayload);

            var testMessage = new NetMessage();
            testMessage.Write(new ByteArray(largePayload));
            peer1.RmiToPeer(7004, testMessage, forceRelay: false, reliable: true);

            Assert.True(messageReceived.Wait(GetAdjustedTimeout(LongOperationTimeout)),
                $"[{profileName}] P2P direct fragmented message should be received");
            Assert.Equal(largePayload, receivedData);

            LogSimulationStatistics();
        }

        [Theory(Timeout = 180000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task Fragmentation_P2PRelayed_LargePayloadReassembled(string profileName)
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
                () => client1.P2PGroup?.P2PMembers.ContainsKey(client2.HostId) == true,
                GetAdjustedTimeout(MessageTimeout));

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];

            byte[] receivedData = null;
            var messageReceived = new ManualResetEventSlim(false);

            client2.OnRMIReceive += (msg, _) =>
            {
                var receivedPayload = new ByteArray();
                msg.Read(ref receivedPayload);
                receivedData = receivedPayload.GetBuffer();
                messageReceived.Set();
            };

            byte[] largePayload = new byte[10000];
            Random.Shared.NextBytes(largePayload);

            var testMessage = new NetMessage();
            testMessage.Write(new ByteArray(largePayload));
            peer1.RmiToPeer(7005, testMessage, forceRelay: true, reliable: true);

            Assert.True(messageReceived.Wait(GetAdjustedTimeout(MessageTimeout)),
                $"[{profileName}] P2P relayed fragmented message should be received");
            Assert.Equal(largePayload, receivedData);

            LogSimulationStatistics();
        }
    }
}
