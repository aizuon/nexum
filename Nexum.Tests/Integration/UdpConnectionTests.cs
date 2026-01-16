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
    public class UdpConnectionTests : IntegrationTestBase
    {
        public UdpConnectionTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(Timeout = 60000)]
        public async Task UdpConnection_FullWorkflow_SetupAndMessaging()
        {
            Server = await CreateServerAsync(withUdp: true);
            Assert.True(Server.UdpEnabled);
            Assert.Equal(UdpPorts.Length, Server.UdpSockets.Count);

            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();

            p2pGroup.Join(session);
            bool clientUdpEnabled = await WaitForClientUdpEnabledAsync(client);
            bool sessionUdpEnabled = await WaitForSessionUdpEnabledAsync(session);
            Assert.True(clientUdpEnabled, "Client UDP should be enabled after holepunch");
            Assert.True(sessionUdpEnabled, "Session UDP should be enabled after holepunch");
            Assert.NotNull(client.UdpChannel);
            Assert.NotNull(session.UdpSocket);
            Assert.NotNull(session.UdpEndPoint);
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
            clientMsg.Write(12345);
            client.RmiToServerUdpIfAvailable(6001, clientMsg);
            var serverMsg = new NetMessage();
            serverMsg.Write(67890);
            session.RmiToClientUdpIfAvailable(6002, serverMsg);
            Assert.True(clientToServerReceived.Wait(ConnectionTimeout), "Server should receive UDP message");
            Assert.True(serverToClientReceived.Wait(ConnectionTimeout), "Client should receive UDP message");
            Assert.Equal(12345, clientToServerValue);
            Assert.Equal(67890, serverToClientValue);
        }

        [Fact(Timeout = 60000)]
        public async Task Server_StartsWithoutUdp_NoUdpSockets()
        {
            Server = await CreateServerAsync(withUdp: false);
            Assert.False(Server.UdpEnabled);
            Assert.Empty(Server.UdpSockets);
        }

        [Fact(Timeout = 60000)]
        public async Task UdpWithoutP2PGroup_FallsBackToTcp()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            int receivedValue = 0;
            var messageReceived = new ManualResetEventSlim(false);

            Server.OnRMIRecieve += (_, msg, _) =>
            {
                msg.Read(out receivedValue);
                messageReceived.Set();
            };

            var testMessage = new NetMessage();
            testMessage.Write(11111);
            client.RmiToServerUdpIfAvailable(6004, testMessage);

            Assert.True(messageReceived.Wait(ConnectionTimeout));
            Assert.Equal(11111, receivedValue);
            Assert.False(client.UdpEnabled, "UDP should not be enabled without P2P group");
        }

        [Fact(Timeout = 60000)]
        public async Task UdpMessage_WithLargePayload_TransmittedSuccessfully()
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
            byte[] largeData = new byte[1000];
            new Random(42).NextBytes(largeData);

            var testMessage = new NetMessage();
            testMessage.Write(new ByteArray(largeData));
            client.RmiToServerUdpIfAvailable(6005, testMessage);
            Assert.True(messageReceived.Wait(ConnectionTimeout));
            Assert.NotNull(receivedData);
            Assert.Equal(largeData.Length, receivedData.Length);
            Assert.Equal(largeData, receivedData);
        }
    }
}
