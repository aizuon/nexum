using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Core;
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

        [Fact(Timeout = 60000)]
        public async Task SmallPacket_SentOverUdp_ReceivedWithoutFragmentation()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();
            p2pGroup.Join(session);

            await WaitForClientUdpEnabledAsync(client);

            byte[] receivedData = null;
            var messageReceived = new ManualResetEventSlim(false);

            Server.OnRMIRecieve += (_, msg, _) =>
            {
                var byteArray = new ByteArray();
                msg.Read(ref byteArray);
                receivedData = byteArray.GetBuffer();
                messageReceived.Set();
            };

            byte[] smallPayload = new byte[FragmentConfig.MtuLength - 100];
            Random.Shared.NextBytes(smallPayload);

            var testMessage = new NetMessage();
            testMessage.Write(new ByteArray(smallPayload));
            client.RmiToServerUdpIfAvailable(7001, testMessage);

            Assert.True(messageReceived.Wait(ConnectionTimeout), "Server should receive small UDP message");
            Assert.Equal(smallPayload, receivedData);
        }

        [Fact(Timeout = 60000)]
        public async Task LargePacket_SentOverUdp_FragmentedAndReassembled()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();
            p2pGroup.Join(session);

            await WaitForClientUdpEnabledAsync(client);

            byte[] receivedData = null;
            var messageReceived = new ManualResetEventSlim(false);

            Server.OnRMIRecieve += (_, msg, _) =>
            {
                var byteArray = new ByteArray();
                msg.Read(ref byteArray);
                receivedData = byteArray.GetBuffer();
                messageReceived.Set();
            };

            byte[] largePayload = new byte[FragmentConfig.MtuLength * 3 + 500];
            Random.Shared.NextBytes(largePayload);

            var testMessage = new NetMessage();
            testMessage.Write(new ByteArray(largePayload));
            client.RmiToServerUdpIfAvailable(7002, testMessage);

            Assert.True(messageReceived.Wait(MessageTimeout), "Server should receive fragmented UDP message");
            Assert.Equal(largePayload, receivedData);
        }

        [Fact(Timeout = 60000)]
        public async Task LargePacket_ServerToClient_FragmentedAndReassembled()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();
            p2pGroup.Join(session);

            await WaitForClientUdpEnabledAsync(client);
            await WaitForSessionUdpEnabledAsync(session);

            byte[] receivedData = null;
            var messageReceived = new ManualResetEventSlim(false);

            client.OnRMIRecieve += (msg, _) =>
            {
                var byteArray = new ByteArray();
                msg.Read(ref byteArray);
                receivedData = byteArray.GetBuffer();
                messageReceived.Set();
            };

            byte[] largePayload = new byte[FragmentConfig.MtuLength * 3 + 500];
            Random.Shared.NextBytes(largePayload);

            var testMessage = new NetMessage();
            testMessage.Write(new ByteArray(largePayload));
            session.RmiToClientUdpIfAvailable(7003, testMessage);

            Assert.True(messageReceived.Wait(MessageTimeout),
                "Client should receive fragmented UDP message from server");
            Assert.Equal(largePayload, receivedData);
        }

        [Fact(Timeout = 60000)]
        public async Task MultipleFragmentedPackets_SentConcurrently_AllReassembledCorrectly()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();
            p2pGroup.Join(session);

            await WaitForClientUdpEnabledAsync(client);

            const int messageCount = 3;
            byte[][] receivedMessages = new byte[messageCount][];
            int receivedCount = 0;
            var allReceived = new ManualResetEventSlim(false);

            Server.OnRMIRecieve += (_, msg, rmiId) =>
            {
                int index = rmiId - 7010;
                if (index >= 0 && index < messageCount)
                {
                    var byteArray = new ByteArray();
                    msg.Read(ref byteArray);
                    receivedMessages[index] = byteArray.GetBuffer();
                    if (Interlocked.Increment(ref receivedCount) == messageCount)
                        allReceived.Set();
                }
            };

            byte[][] sentPayloads = new byte[messageCount][];
            for (int i = 0; i < messageCount; i++)
            {
                sentPayloads[i] = new byte[FragmentConfig.MtuLength * (3 + i) + 500];
                Random.Shared.NextBytes(sentPayloads[i]);

                var testMessage = new NetMessage();
                testMessage.Write(new ByteArray(sentPayloads[i]));
                client.RmiToServerUdpIfAvailable((ushort)(7010 + i), testMessage);
            }

            Assert.True(allReceived.Wait(MessageTimeout), "Server should receive all fragmented messages");

            for (int i = 0; i < messageCount; i++)
            {
                Assert.NotNull(receivedMessages[i]);
                Assert.Equal(sentPayloads[i], receivedMessages[i]);
            }
        }

        [Fact(Timeout = 60000)]
        public async Task BidirectionalFragmentedMessages_BothDirections_Success()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();
            p2pGroup.Join(session);

            await WaitForClientUdpEnabledAsync(client);
            await WaitForSessionUdpEnabledAsync(session);

            byte[] serverReceivedData = null;
            byte[] clientReceivedData = null;
            var serverReceived = new ManualResetEventSlim(false);
            var clientReceived = new ManualResetEventSlim(false);

            Server.OnRMIRecieve += (_, msg, _) =>
            {
                var byteArray = new ByteArray();
                msg.Read(ref byteArray);
                serverReceivedData = byteArray.GetBuffer();
                serverReceived.Set();
            };

            client.OnRMIRecieve += (msg, _) =>
            {
                var byteArray = new ByteArray();
                msg.Read(ref byteArray);
                clientReceivedData = byteArray.GetBuffer();
                clientReceived.Set();
            };

            byte[] clientToServerPayload = new byte[FragmentConfig.MtuLength * 3 + 500];
            byte[] serverToClientPayload = new byte[FragmentConfig.MtuLength * 4 + 500];
            Random.Shared.NextBytes(clientToServerPayload);
            Random.Shared.NextBytes(serverToClientPayload);

            var clientMsg = new NetMessage();
            clientMsg.Write(new ByteArray(clientToServerPayload));
            client.RmiToServerUdpIfAvailable(7020, clientMsg);

            var serverMsg = new NetMessage();
            serverMsg.Write(new ByteArray(serverToClientPayload));
            session.RmiToClientUdpIfAvailable(7021, serverMsg);

            Assert.True(serverReceived.Wait(MessageTimeout),
                "Server should receive client's fragmented message");
            Assert.True(clientReceived.Wait(MessageTimeout),
                "Client should receive server's fragmented message");

            Assert.Equal(clientToServerPayload, serverReceivedData);
            Assert.Equal(serverToClientPayload, clientReceivedData);
        }
    }
}
