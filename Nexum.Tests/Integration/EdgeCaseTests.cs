using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Nexum.Client;
using Nexum.Core;
using Xunit;
using Xunit.Abstractions;

namespace Nexum.Tests.Integration
{
    [Collection("Integration")]
    public class EdgeCaseTests : IntegrationTestBase
    {
        public EdgeCaseTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(Timeout = 60000)]
        public async Task Client_AbruptDispose_SessionRemovedFromServer()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();

            await WaitForClientConnectionAsync(client);
            Assert.Single(Server.Sessions);

            client.Dispose();
            await WaitForConditionAsync(() => Server.Sessions.Count == 0, LongOperationTimeout);
            Assert.Empty(Server.Sessions);
        }

        [Fact(Timeout = 60000)]
        public async Task Server_Dispose_ClientsSessionsCleared()
        {
            Server = await CreateServerAsync();

            var client1 = await CreateClientAsync();
            var client2 = await CreateClientAsync();

            await WaitForClientConnectionAsync(client1);
            await WaitForClientConnectionAsync(client2);

            Assert.Equal(2, Server.Sessions.Count);
            Server.Dispose();
            await Task.Delay(500);
            Assert.Empty(Server.Sessions);
            client1.Dispose();
            client2.Dispose();
        }

        [Fact(Timeout = 60000)]
        public async Task Server_ManyClients_AllConnectSuccessfully()
        {
            const int clientCount = 10;
            Server = await CreateServerAsync();

            var clients = new List<NetClient>();

            try
            {
                for (int i = 0; i < clientCount; i++)
                {
                    var client = await CreateClientAsync();
                    clients.Add(client);
                }

                foreach (var client in clients)
                    await WaitForClientConnectionAsync(client);
                Assert.Equal(clientCount, clients.Count(c => c.HostId != 0));
                Assert.Equal(clientCount, Server.Sessions.Count);
            }
            finally
            {
                foreach (var client in clients)
                    client.Dispose();
            }
        }

        [Fact(Timeout = 60000)]
        public async Task Server_ManyMessages_AllDeliveredInOrder()
        {
            const int messageCount = 100;
            Server = await CreateServerAsync();

            var receivedOrder = new List<int>();
            object lockObj = new object();

            Server.OnRMIRecieve += (session, msg, rmiId) =>
            {
                if (rmiId == 1001)
                {
                    msg.Read(out int value);
                    lock (lockObj)
                    {
                        receivedOrder.Add(value);
                    }
                }
            };

            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);
            for (int i = 0; i < messageCount; i++)
            {
                var msg = new NetMessage();
                msg.Write(i);
                client.RmiToServer(1001, msg, EncryptMode.None);
            }

            await WaitForConditionAsync(
                () => receivedOrder.Count >= messageCount,
                LongOperationTimeout);

            Assert.Equal(messageCount, receivedOrder.Count);
            for (int i = 0; i < messageCount; i++)
                Assert.Equal(i, receivedOrder[i]);

            client.Dispose();
        }

        [Fact(Timeout = 60000)]
        public async Task Client_ConnectToNonExistentServer_FailsGracefully()
        {
            var client = new NetClient(ServerType.Relay);
            try
            {
                await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 59999));
            }
            catch
            {
            }

            await Task.Delay(2000);
            Assert.Equal(0u, client.HostId);

            client.Dispose();
        }

        [Fact(Timeout = 60000)]
        public async Task Server_RapidConnectDisconnect_HandlesCorrectly()
        {
            Server = await CreateServerAsync();
            const int cycles = 5;

            for (int i = 0; i < cycles; i++)
            {
                var client = await CreateClientAsync();
                await WaitForClientConnectionAsync(client);

                Assert.NotEqual(0u, client.HostId);

                client.Close();
                await Task.Delay(100);
                client.Dispose();
                CreatedClients.Remove(client);

                await WaitForConditionAsync(() => Server.Sessions.Count == 0, ConnectionTimeout);
            }

            Assert.Empty(Server.Sessions);

            var finalClient = await CreateClientAsync();
            await WaitForClientConnectionAsync(finalClient);
            Assert.NotEqual(0u, finalClient.HostId);
            Assert.Single(Server.Sessions);
        }
    }
}
