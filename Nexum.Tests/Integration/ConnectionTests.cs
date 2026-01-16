using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Client;
using Nexum.Core;
using Xunit;
using Xunit.Abstractions;

namespace Nexum.Tests.Integration
{
    [Collection("Integration")]
    public class ConnectionTests : IntegrationTestBase
    {
        public ConnectionTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(Timeout = 60000)]
        public async Task Client_ConnectsToServer_ConnectionFullyEstablished()
        {
            var customSettings = new NetSettings
            {
                EnableServerLog = true,
                MessageMaxLength = 2097152,
                IdleTimeout = 300
            };
            Server = await CreateServerAsync(netSettings: customSettings);
            var client = await CreateClientAsync();
            bool connected = await WaitForClientConnectionAsync(client);
            Assert.True(connected, "Client should receive a HostId after connecting");
            Assert.NotEqual(0u, client.HostId);
            Assert.Single(Server.Sessions);
            var session = Server.Sessions.Values.First();
            Assert.Equal(client.HostId, session.HostId);
            Assert.True(session.IsConnected);
            Assert.Equal(Server.ServerGuid, client.ServerGuid);
            Assert.NotNull(client.NetSettings);
            Assert.Equal(customSettings.MessageMaxLength, client.NetSettings.MessageMaxLength);
            Assert.Equal(customSettings.IdleTimeout, client.NetSettings.IdleTimeout);
            Assert.NotNull(client.Crypt);
            Assert.NotNull(client.Crypt.GetKey());
            Assert.NotNull(client.Crypt.GetFastKey());
            Assert.NotNull(session.Crypt);
            Assert.NotNull(session.Crypt.GetKey());
            Assert.Equal(client.Crypt.GetKey(), session.Crypt.GetKey());
            Assert.Equal(client.Crypt.GetFastKey(), session.Crypt.GetFastKey());
        }

        [Fact(Timeout = 60000)]
        public async Task MultipleClients_ConnectToServer_AllTrackedWithUniqueHostIds()
        {
            Server = await CreateServerAsync();
            const int clientCount = 3;
            var clients = new NetClient[clientCount];
            for (int i = 0; i < clientCount; i++)
            {
                clients[i] = new NetClient(ServerType.Relay);
                await clients[i].ConnectAsync(new IPEndPoint(DefaultAddress, TcpPort));
            }

            foreach (var client in clients)
                await WaitForClientConnectionAsync(client);
            Assert.Equal(clientCount, Server.Sessions.Count);
            var hostIds = clients.Select(c => c.HostId).ToHashSet();
            Assert.Equal(clientCount, hostIds.Count);
            Assert.DoesNotContain(0u, hostIds);
            foreach (var client in clients)
                Assert.Equal(Server.ServerGuid, client.ServerGuid);
        }

        [Fact(Timeout = 60000)]
        public async Task Client_Disconnect_SessionRemovedFromServer()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);
            Assert.Single(Server.Sessions);
            client.Close();
            await Task.Delay(300);
            client.Dispose();
            await WaitForConditionAsync(() => Server.Sessions.Count == 0, ConnectionTimeout);
            Assert.Empty(Server.Sessions);
        }

        [Fact(Timeout = 60000)]
        public async Task BidirectionalCommunication_ClientAndServer_MessagesExchanged()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            int serverMessageCount = 0;
            int clientMessageCount = 0;
            var serverMessagesReceived = new ManualResetEventSlim(false);
            var clientMessagesReceived = new ManualResetEventSlim(false);
            const int expectedMessages = 5;

            client.OnRMIRecieve += (message, rmiId) =>
            {
                if (Interlocked.Increment(ref clientMessageCount) >= expectedMessages)
                    clientMessagesReceived.Set();
            };

            Server.OnRMIRecieve += (session, message, rmiId) =>
            {
                if (Interlocked.Increment(ref serverMessageCount) >= expectedMessages)
                    serverMessagesReceived.Set();
            };
            var session = Server.Sessions.Values.First();
            for (int i = 0; i < expectedMessages; i++)
            {
                var clientMsg = new NetMessage();
                clientMsg.Write(i);
                client.RmiToServer(4001, clientMsg);

                var serverMsg = new NetMessage();
                serverMsg.Write(i);
                session.RmiToClient(4002, serverMsg);
            }

            Assert.True(serverMessagesReceived.Wait(MessageTimeout),
                "Server should receive all client messages");
            Assert.True(clientMessagesReceived.Wait(MessageTimeout),
                "Client should receive all server messages");
            Assert.Equal(expectedMessages, serverMessageCount);
            Assert.Equal(expectedMessages, clientMessageCount);
        }

        [Fact(Timeout = 60000)]
        public async Task Client_OnConnectionComplete_Invoked()
        {
            Server = await CreateServerAsync();
            bool connectionCompleteCalled = false;
            var client = await CreateClientAsync();
            client.OnConnectionComplete += () => connectionCompleteCalled = true;
            await WaitForConditionAsync(() => connectionCompleteCalled, ConnectionTimeout);
            Assert.True(connectionCompleteCalled);
        }

        [Fact(Timeout = 60000)]
        public async Task Client_ReconnectAfterDisconnect_NewSessionEstablished()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            Assert.NotEqual(0u, client.HostId);
            Assert.Single(Server.Sessions);
            byte[] originalKey = client.Crypt.GetKey();

            client.Close();
            await Task.Delay(300);
            client.Dispose();
            CreatedClients.Remove(client);

            await WaitForConditionAsync(() => Server.Sessions.Count == 0, ConnectionTimeout);
            Assert.Empty(Server.Sessions);

            var newClient = await CreateClientAsync();
            await WaitForClientConnectionAsync(newClient);

            Assert.NotEqual(0u, newClient.HostId);
            Assert.Single(Server.Sessions);
            Assert.Equal(Server.ServerGuid, newClient.ServerGuid);
            Assert.NotEqual(originalKey, newClient.Crypt.GetKey());
        }
    }
}
