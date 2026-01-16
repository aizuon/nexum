using System.Threading;
using System.Threading.Tasks;
using Nexum.Client;
using Nexum.Core;
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

        private async Task<(NetClient client1, NetClient client2, P2PGroup group)>
            SetupTwoClientsInP2PGroupAsync()
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
            await WaitForClientUdpEnabledAsync(client1);
            await WaitForClientUdpEnabledAsync(client2);

            return (client1, client2, group);
        }

        [Fact(Timeout = 60000)]
        public async Task P2PGroup_FullWorkflow_TwoClients()
        {
            var (client1, client2, group) = await SetupTwoClientsInP2PGroupAsync();
            Assert.NotNull(client1.P2PGroup);
            Assert.NotNull(client2.P2PGroup);
            await WaitForConditionAsync(
                () => client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId) &&
                      client2.P2PGroup.P2PMembers.ContainsKey(client1.HostId),
                MessageTimeout);

            Assert.True(client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId),
                "Client1 should know about Client2");
            Assert.True(client2.P2PGroup.P2PMembers.ContainsKey(client1.HostId),
                "Client2 should know about Client1");
            int client1Received = 0;
            int client2Received = 0;
            var client1Event = new ManualResetEventSlim(false);
            var client2Event = new ManualResetEventSlim(false);

            client1.OnRMIRecieve += (msg, _) =>
            {
                msg.Read(out client1Received);
                client1Event.Set();
            };

            client2.OnRMIRecieve += (msg, _) =>
            {
                msg.Read(out client2Received);
                client2Event.Set();
            };
            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            var msg1 = new NetMessage();
            msg1.Write(111);
            peer1.RmiToPeer(7006, msg1, true, true);

            var msg2 = new NetMessage();
            msg2.Write(222);
            peer2.RmiToPeer(7007, msg2, true, true);

            Assert.True(client2Event.Wait(ConnectionTimeout), "Client2 should receive message from Client1");
            Assert.True(client1Event.Wait(ConnectionTimeout), "Client1 should receive message from Client2");
            Assert.Equal(111, client2Received);
            Assert.Equal(222, client1Received);
            int messageCount = 0;
            const int expectedCount = 10;
            var allReceived = new ManualResetEventSlim(false);

            client2.OnRMIRecieve += (_, _) =>
            {
                if (Interlocked.Increment(ref messageCount) >= expectedCount)
                    allReceived.Set();
            };

            for (int i = 0; i < expectedCount; i++)
            {
                var msg = new NetMessage();
                msg.Write(i);
                peer1.RmiToPeer(7008, msg, true, true);
            }

            Assert.True(allReceived.Wait(MessageTimeout),
                $"All {expectedCount} messages should be delivered");
            var session2 = Server.Sessions[client2.HostId];
            group.Leave(session2);

            await WaitForConditionAsync(
                () => !client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId),
                MessageTimeout);

            Assert.False(client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId),
                "Client1 should no longer see Client2 in the group");
        }

        [Fact(Timeout = 60000)]
        public async Task P2PDirectConnection_AfterHolepunch_MessagingWorks()
        {
            var (client1, client2, _) = await SetupTwoClientsInP2PGroupAsync();

            await WaitForConditionAsync(
                () => client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId) &&
                      client2.P2PGroup.P2PMembers.ContainsKey(client1.HostId),
                MessageTimeout);

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var peer2 = client2.P2PGroup.P2PMembers[client1.HostId];

            await WaitForP2PDirectConnectionAsync(peer1);
            await WaitForP2PDirectConnectionAsync(peer2);

            int receivedValue = 0;
            var messageReceived = new ManualResetEventSlim(false);

            client2.OnRMIRecieve += (msg, _) =>
            {
                msg.Read(out receivedValue);
                messageReceived.Set();
            };

            var testMessage = new NetMessage();
            testMessage.Write(11223);
            peer1.RmiToPeer(7003, testMessage);

            Assert.True(messageReceived.Wait(ConnectionTimeout), "Client2 should receive message");
            Assert.Equal(11223, receivedValue);
        }

        [Fact(Timeout = 60000)]
        public async Task P2PGroup_ThreeClients_AllCanCommunicate()
        {
            Server = await CreateServerAsync();

            var client1 = await CreateClientAsync();
            await WaitForClientConnectionAsync(client1);

            var client2 = await CreateClientAsync();
            await WaitForClientConnectionAsync(client2);

            var client3 = await CreateClientAsync();
            await WaitForClientConnectionAsync(client3);

            var session1 = Server.Sessions[client1.HostId];
            var session2 = Server.Sessions[client2.HostId];
            var session3 = Server.Sessions[client3.HostId];
            var group = Server.CreateP2PGroup();

            group.Join(session1);
            group.Join(session2);
            group.Join(session3);
            await WaitForClientUdpEnabledAsync(client1);
            await WaitForClientUdpEnabledAsync(client2);
            await WaitForClientUdpEnabledAsync(client3);
            await WaitForConditionAsync(
                () => client1.P2PGroup.P2PMembers.Count >= 2,
                MessageTimeout);
            Assert.Equal(2, client1.P2PGroup.P2PMembers.Count);
            Assert.Equal(2, client2.P2PGroup.P2PMembers.Count);
            Assert.Equal(2, client3.P2PGroup.P2PMembers.Count);
            int client2ReceivedFromClient1 = 0;
            int client3ReceivedFromClient1 = 0;
            var client2Received = new ManualResetEventSlim(false);
            var client3Received = new ManualResetEventSlim(false);

            client2.OnRMIRecieve += (msg, _) =>
            {
                msg.Read(out client2ReceivedFromClient1);
                client2Received.Set();
            };

            client3.OnRMIRecieve += (msg, _) =>
            {
                msg.Read(out client3ReceivedFromClient1);
                client3Received.Set();
            };
            var peer2 = client1.P2PGroup.P2PMembers[client2.HostId];
            var msg1 = new NetMessage();
            msg1.Write(100);
            peer2.RmiToPeer(7004, msg1, true, true);
            var peer3 = client1.P2PGroup.P2PMembers[client3.HostId];
            var msg2 = new NetMessage();
            msg2.Write(200);
            peer3.RmiToPeer(7005, msg2, true, true);
            Assert.True(client2Received.Wait(ConnectionTimeout));
            Assert.True(client3Received.Wait(ConnectionTimeout));
            Assert.Equal(100, client2ReceivedFromClient1);
            Assert.Equal(200, client3ReceivedFromClient1);
        }

        [Fact(Timeout = 60000)]
        public async Task P2PGroup_ClientDisconnects_RemovedFromGroup()
        {
            var (client1, client2, group) = await SetupTwoClientsInP2PGroupAsync();

            await WaitForConditionAsync(
                () => client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId),
                MessageTimeout);

            client2.Dispose();
            await WaitForConditionAsync(
                () => !client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId),
                MessageTimeout);
            Assert.False(client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId),
                "Disconnected client should be removed from P2P group");
            Assert.Single(group.P2PMembers);
        }

        [Fact(Timeout = 60000)]
        public async Task P2PGroup_RelayMessaging_WorksWhenForcedRelay()
        {
            var (client1, client2, _) = await SetupTwoClientsInP2PGroupAsync();

            await WaitForConditionAsync(
                () => client1.P2PGroup.P2PMembers.ContainsKey(client2.HostId) &&
                      client2.P2PGroup.P2PMembers.ContainsKey(client1.HostId),
                MessageTimeout);

            int receivedValue = 0;
            var messageReceived = new ManualResetEventSlim(false);

            client2.OnRMIRecieve += (msg, _) =>
            {
                msg.Read(out receivedValue);
                messageReceived.Set();
            };

            var peer1 = client1.P2PGroup.P2PMembers[client2.HostId];
            var testMessage = new NetMessage();
            testMessage.Write(55555);
            peer1.RmiToPeer(7009, testMessage, true, true);

            Assert.True(messageReceived.Wait(MessageTimeout),
                "Client2 should receive relayed message from Client1");
            Assert.Equal(55555, receivedValue);
        }
    }
}
