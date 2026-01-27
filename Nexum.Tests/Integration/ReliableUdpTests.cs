using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Core.Serialization;
using Nexum.Core.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Nexum.Tests.Integration
{
    [Collection("Integration")]
    public class ReliableUdpTests : IntegrationTestBase
    {
        public ReliableUdpTests(ITestOutputHelper output) : base(output)
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
        public async Task ReliableUdp_ClientServer_MessagesDeliveredInOrder(string profileName)
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
            await WaitForConditionAsync(
                () => client.ToServerReliableUdp != null && session.ToClientReliableUdp != null,
                GetAdjustedTimeout(ConnectionTimeout));

            var receivedValues = new ConcurrentQueue<int>();
            const int messageCount = 5;
            var allReceived = new CountdownEvent(messageCount);

            Server.OnRmiReceive += (_, msg, _) =>
            {
                msg.Read(out int value);
                receivedValues.Enqueue(value);
                allReceived.Signal();
            };

            for (int i = 0; i < messageCount; i++)
            {
                var testMessage = new NetMessage();
                testMessage.Write(i * 100);
                client.RmiToServerUdpIfAvailable(7001, testMessage, reliable: true);
            }

            Assert.True(allReceived.Wait(GetAdjustedTimeout(MessageTimeout)),
                $"[{profileName}] All messages should be received, got {messageCount - allReceived.CurrentCount}");

            int[] receivedArray = receivedValues.ToArray();
            Assert.Equal(messageCount, receivedArray.Length);
            for (int i = 0; i < messageCount; i++)
                Assert.Equal(i * 100, receivedArray[i]);

            LogSimulationStatistics();
        }

        [Theory(Timeout = 180000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task ReliableUdp_ClientServer_Bidirectional(string profileName)
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
            await WaitForConditionAsync(
                () => client.ToServerReliableUdp != null && session.ToClientReliableUdp != null,
                GetAdjustedTimeout(ConnectionTimeout));

            int clientToServerValue = 0;
            int serverToClientValue = 0;
            var clientToServerReceived = new ManualResetEventSlim(false);
            var serverToClientReceived = new ManualResetEventSlim(false);

            Server.OnRmiReceive += (_, msg, _) =>
            {
                msg.Read(out clientToServerValue);
                clientToServerReceived.Set();
            };

            client.OnRmiReceive += (msg, _) =>
            {
                msg.Read(out serverToClientValue);
                serverToClientReceived.Set();
            };

            var clientMessage = new NetMessage();
            clientMessage.Write(11111);
            client.RmiToServerUdpIfAvailable(7002, clientMessage, reliable: true);

            var serverMessage = new NetMessage();
            serverMessage.Write(22222);
            session.RmiToClientUdpIfAvailable(7003, serverMessage, reliable: true);

            Assert.True(clientToServerReceived.Wait(GetAdjustedTimeout(MessageTimeout)));
            Assert.True(serverToClientReceived.Wait(GetAdjustedTimeout(MessageTimeout)));
            Assert.Equal(11111, clientToServerValue);
            Assert.Equal(22222, serverToClientValue);

            LogSimulationStatistics();
        }

        [Theory(Timeout = 180000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task ReliableUdp_P2PDirect_MessagesDelivered(string profileName)
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

            int messageCount = 0;
            const int expectedCount = 5;
            var allReceived = new ManualResetEventSlim(false);

            client2.OnRmiReceive += (_, _) =>
            {
                if (Interlocked.Increment(ref messageCount) >= expectedCount)
                    allReceived.Set();
            };

            await Task.Delay(100);

            for (int i = 0; i < expectedCount; i++)
            {
                var testMessage = new NetMessage();
                testMessage.Write(2000 + i);
                peer1.RmiToPeer(7004, testMessage, forceRelay: false, reliable: true);
                await Task.Delay(200);
            }

            Assert.True(allReceived.Wait(GetAdjustedTimeout(LongOperationTimeout)),
                $"[{profileName}] All reliable P2P messages should be delivered (received {messageCount})");

            LogSimulationStatistics();
        }
    }
}
