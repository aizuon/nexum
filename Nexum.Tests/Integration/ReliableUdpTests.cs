using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Core;
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

        [Fact(Timeout = 60000)]
        public async Task ReliableUdp_ClientToServer_MessageDelivered()
        {
            Server = await CreateServerAsync(withUdp: true);
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();

            p2pGroup.Join(session);

            bool clientUdpEnabled = await WaitForClientUdpEnabledAsync(client);
            bool sessionUdpEnabled = await WaitForSessionUdpEnabledAsync(session);
            Assert.True(clientUdpEnabled, "Client UDP should be enabled after holepunch");
            Assert.True(sessionUdpEnabled, "Session UDP should be enabled after holepunch");

            await WaitForConditionAsync(
                () => client.ToServerReliableUdp != null && session.ToClientReliableUdp != null,
                ConnectionTimeout);

            Assert.NotNull(client.ToServerReliableUdp);
            Assert.NotNull(session.ToClientReliableUdp);

            int receivedValue = 0;
            var messageReceived = new ManualResetEventSlim(false);

            Server.OnRMIRecieve += (_, msg, _) =>
            {
                msg.Read(out receivedValue);
                messageReceived.Set();
            };

            var testMessage = new NetMessage();
            testMessage.Write(42424);
            client.RmiToServerUdpIfAvailable(7001, testMessage, reliable: true);

            await Task.Delay(200);

            Assert.True(messageReceived.Wait(MessageTimeout), "Server should receive reliable UDP message");
            Assert.Equal(42424, receivedValue);
        }

        [Fact(Timeout = 60000)]
        public async Task ReliableUdp_ServerToClient_MessageDelivered()
        {
            Server = await CreateServerAsync(withUdp: true);
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();

            p2pGroup.Join(session);

            await WaitForClientUdpEnabledAsync(client);
            await WaitForSessionUdpEnabledAsync(session);
            await WaitForConditionAsync(
                () => client.ToServerReliableUdp != null && session.ToClientReliableUdp != null,
                ConnectionTimeout);

            int receivedValue = 0;
            var messageReceived = new ManualResetEventSlim(false);

            client.OnRMIRecieve += (msg, _) =>
            {
                msg.Read(out receivedValue);
                messageReceived.Set();
            };

            var testMessage = new NetMessage();
            testMessage.Write(98765);
            session.RmiToClientUdpIfAvailable(7002, testMessage, reliable: true);

            await Task.Delay(200);

            Assert.True(messageReceived.Wait(MessageTimeout), "Client should receive reliable UDP message");
            Assert.Equal(98765, receivedValue);
        }

        [Fact(Timeout = 60000)]
        public async Task ReliableUdp_LargePayload_TransmittedSuccessfully()
        {
            Server = await CreateServerAsync(withUdp: true);
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();

            p2pGroup.Join(session);

            await WaitForClientUdpEnabledAsync(client);
            await WaitForSessionUdpEnabledAsync(session);
            await WaitForConditionAsync(
                () => client.ToServerReliableUdp != null && session.ToClientReliableUdp != null,
                ConnectionTimeout);

            byte[] receivedData = null;
            var messageReceived = new ManualResetEventSlim(false);

            Server.OnRMIRecieve += (_, msg, _) =>
            {
                var byteArray = new ByteArray();
                msg.Read(ref byteArray);
                receivedData = byteArray.GetBuffer();
                messageReceived.Set();
            };

            byte[] largeData = new byte[5000];
            new Random(42).NextBytes(largeData);

            var testMessage = new NetMessage();
            testMessage.Write(new ByteArray(largeData));
            client.RmiToServerUdpIfAvailable(7003, testMessage, reliable: true);

            Assert.True(messageReceived.Wait(MessageTimeout),
                "Server should receive large reliable UDP message");
            Assert.NotNull(receivedData);
            Assert.Equal(largeData.Length, receivedData.Length);
            Assert.Equal(largeData, receivedData);
        }

        [Fact(Timeout = 60000)]
        public async Task ReliableUdp_MultipleMessages_AllDeliveredInOrder()
        {
            Server = await CreateServerAsync(withUdp: true);
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();

            p2pGroup.Join(session);

            await WaitForClientUdpEnabledAsync(client);
            await WaitForSessionUdpEnabledAsync(session);
            await WaitForConditionAsync(
                () => client.ToServerReliableUdp != null && session.ToClientReliableUdp != null,
                ConnectionTimeout);

            var receivedValues = new ConcurrentQueue<int>();
            int messageCount = 5;
            var allReceived = new CountdownEvent(messageCount);

            Server.OnRMIRecieve += (_, msg, _) =>
            {
                msg.Read(out int value);
                receivedValues.Enqueue(value);
                allReceived.Signal();
            };

            for (int i = 0; i < messageCount; i++)
            {
                var testMessage = new NetMessage();
                testMessage.Write(i * 100);
                client.RmiToServerUdpIfAvailable(7004, testMessage, reliable: true);
            }

            Assert.True(allReceived.Wait(MessageTimeout),
                $"All {messageCount} messages should be received, got {messageCount - allReceived.CurrentCount}");

            int[] receivedArray = receivedValues.ToArray();
            Assert.Equal(messageCount, receivedArray.Length);

            for (int i = 0; i < messageCount; i++)
                Assert.Equal(i * 100, receivedArray[i]);
        }

        [Fact(Timeout = 60000)]
        public async Task ReliableUdp_BidirectionalCommunication_BothDirectionsWork()
        {
            Server = await CreateServerAsync(withUdp: true);
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();

            p2pGroup.Join(session);

            await WaitForClientUdpEnabledAsync(client);
            await WaitForSessionUdpEnabledAsync(session);
            await WaitForConditionAsync(
                () => client.ToServerReliableUdp != null && session.ToClientReliableUdp != null,
                ConnectionTimeout);

            int clientToServerValue = 0;
            int serverToClientValue = 0;
            var clientToServerReceived = new ManualResetEventSlim(false);
            var serverToClientReceived = new ManualResetEventSlim(false);

            Server.OnRMIRecieve += (_, msg, _) =>
            {
                msg.Read(out clientToServerValue);
                clientToServerReceived.Set();
            };

            client.OnRMIRecieve += (msg, _) =>
            {
                msg.Read(out serverToClientValue);
                serverToClientReceived.Set();
            };

            var clientMsg = new NetMessage();
            clientMsg.Write(11111);
            client.RmiToServerUdpIfAvailable(7005, clientMsg, reliable: true);

            var serverMsg = new NetMessage();
            serverMsg.Write(22222);
            session.RmiToClientUdpIfAvailable(7006, serverMsg, reliable: true);

            Assert.True(clientToServerReceived.Wait(MessageTimeout),
                "Server should receive reliable UDP message from client");
            Assert.True(serverToClientReceived.Wait(MessageTimeout),
                "Client should receive reliable UDP message from server");

            Assert.Equal(11111, clientToServerValue);
            Assert.Equal(22222, serverToClientValue);
        }
    }
}
