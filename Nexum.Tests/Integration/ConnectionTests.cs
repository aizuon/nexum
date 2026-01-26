using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Core.Configuration;
using Nexum.Core.Serialization;
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

        [Fact(Timeout = 30000)]
        public async Task TcpConnection_ClientConnects_SessionEstablished()
        {
            var customSettings = new NetSettings
            {
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
            Assert.True(session.IsConnected, "Session should be connected");
            Assert.Equal(Server.ServerInstanceGuid, client.ServerInstanceGuid);

            Assert.NotNull(client.NetSettings);
            Assert.Equal(customSettings.MessageMaxLength, client.NetSettings.MessageMaxLength);
            Assert.Equal(customSettings.IdleTimeout, client.NetSettings.IdleTimeout);
        }

        [Fact(Timeout = 30000)]
        public async Task TcpConnection_ClientDisconnects_SessionRemoved()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            Assert.Single(Server.Sessions);

            await client.CloseAsync();
            await Task.Delay(300);
            client.Dispose();

            await WaitForConditionAsync(() => Server.Sessions.Count == 0, GetAdjustedTimeout(ConnectionTimeout));
            Assert.Empty(Server.Sessions);
        }

        [Fact(Timeout = 30000)]
        public async Task UnencryptedMessaging_Bidirectional_MessagesExchanged()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            int serverReceived = 0;
            int clientReceived = 0;
            var serverDone = new ManualResetEventSlim(false);
            var clientDone = new ManualResetEventSlim(false);
            const int expectedMessages = 5;

            client.OnRmiReceive += (_, _) =>
            {
                if (Interlocked.Increment(ref clientReceived) >= expectedMessages)
                    clientDone.Set();
            };

            Server.OnRmiReceive += (_, _, _) =>
            {
                if (Interlocked.Increment(ref serverReceived) >= expectedMessages)
                    serverDone.Set();
            };

            var session = Server.Sessions.Values.First();
            for (int i = 0; i < expectedMessages; i++)
            {
                var clientMsg = new NetMessage();
                clientMsg.Write(i);
                client.RmiToServer(4001, clientMsg, EncryptMode.None);

                var serverMsg = new NetMessage();
                serverMsg.Write(i);
                session.RmiToClient(4002, serverMsg, EncryptMode.None);
            }

            Assert.True(serverDone.Wait(GetAdjustedTimeout(MessageTimeout)), "Server should receive all messages");
            Assert.True(clientDone.Wait(GetAdjustedTimeout(MessageTimeout)), "Client should receive all messages");
            Assert.Equal(expectedMessages, serverReceived);
            Assert.Equal(expectedMessages, clientReceived);
        }
    }
}
