using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Core;
using Xunit;
using Xunit.Abstractions;

namespace Nexum.Tests.Integration
{
    [Collection("Integration")]
    public class KeyExchangeTests : IntegrationTestBase
    {
        public KeyExchangeTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(Timeout = 30000)]
        public async Task KeyExchange_OnConnection_KeysEstablished()
        {
            var customSettings = new NetSettings
            {
                EncryptedMessageKeyLength = 128,
                FastEncryptedMessageKeyLength = 256
            };
            Server = await CreateServerAsync(netSettings: customSettings);
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();

            Assert.NotNull(client.Crypt);
            Assert.NotNull(client.Crypt.GetKey());
            Assert.NotNull(client.Crypt.GetFastKey());
            Assert.NotNull(session.Crypt);
            Assert.NotNull(session.Crypt.GetKey());
            Assert.Equal(client.Crypt.GetKey(), session.Crypt.GetKey());
            Assert.Equal(client.Crypt.GetFastKey(), session.Crypt.GetFastKey());

            Assert.Equal(128 / 8, client.Crypt.GetKey().Length);
            Assert.Equal(256 / 8, client.Crypt.GetFastKey().Length);
        }

        [Fact(Timeout = 30000)]
        public async Task KeyExchange_MultipleClients_UniqueKeysPerSession()
        {
            Server = await CreateServerAsync();

            var client1 = await CreateClientAsync();
            await WaitForClientConnectionAsync(client1);

            var client2 = await CreateClientAsync();
            await WaitForClientConnectionAsync(client2);

            Assert.NotEqual(client1.Crypt.GetKey(), client2.Crypt.GetKey());
            Assert.NotEqual(client1.Crypt.GetFastKey(), client2.Crypt.GetFastKey());

            var session1 = Server.Sessions[client1.HostId];
            var session2 = Server.Sessions[client2.HostId];
            Assert.Equal(client1.Crypt.GetKey(), session1.Crypt.GetKey());
            Assert.Equal(client2.Crypt.GetKey(), session2.Crypt.GetKey());
        }

        [Fact(Timeout = 30000)]
        public async Task EncryptedMessaging_AllModes_WorkCorrectly()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            int secureReceived = 0;
            int fastReceived = 0;
            var allReceived = new CountdownEvent(2);

            Server.OnRMIReceive += (_, message, rmiId) =>
            {
                message.Read(out int value);
                switch (rmiId)
                {
                    case 5001:
                        secureReceived = value;
                        allReceived.Signal();
                        break;
                    case 5002:
                        fastReceived = value;
                        allReceived.Signal();
                        break;
                }
            };

            var secureMessage = new NetMessage();
            secureMessage.Write(111);
            client.RmiToServer(5001, secureMessage);

            var fastMessage = new NetMessage();
            fastMessage.Write(222);
            client.RmiToServer(5002, fastMessage, EncryptMode.Fast);

            Assert.True(allReceived.Wait(GetAdjustedTimeout(MessageTimeout)),
                "All encrypted messages should be received");
            Assert.Equal(111, secureReceived);
            Assert.Equal(222, fastReceived);
        }
    }
}
