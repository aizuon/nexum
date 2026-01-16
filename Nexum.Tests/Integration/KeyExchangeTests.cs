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
    public class KeyExchangeTests : IntegrationTestBase
    {
        public KeyExchangeTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(Timeout = 60000)]
        public async Task EncryptedMessaging_AllModes_WorkCorrectly()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            Assert.Equal(client.Crypt.GetKey(), session.Crypt.GetKey());
            Assert.Equal(client.Crypt.GetFastKey(), session.Crypt.GetFastKey());

            int secureClientToServer = 0;
            int secureServerToClient = 0;
            int fastClientToServer = 0;
            int fastServerToClient = 0;
            string receivedString = string.Empty;

            var allReceived = new CountdownEvent(4);

            Server.OnRMIRecieve += (_, message, rmiId) =>
            {
                if (rmiId == 5001)
                {
                    message.Read(out secureClientToServer);
                    message.ReadString(ref receivedString);
                    allReceived.Signal();
                }
                else if (rmiId == 5003)
                {
                    message.Read(out fastClientToServer);
                    allReceived.Signal();
                }
            };

            client.OnRMIRecieve += (message, rmiId) =>
            {
                if (rmiId == 5002)
                {
                    message.Read(out secureServerToClient);
                    allReceived.Signal();
                }
                else if (rmiId == 5004)
                {
                    message.Read(out fastServerToClient);
                    allReceived.Signal();
                }
            };
            var msg1 = new NetMessage();
            msg1.Write(42);
            msg1.Write("Encrypted test message");
            client.RmiToServer(5001, msg1);
            var msg2 = new NetMessage();
            msg2.Write(99);
            session.RmiToClient(5002, msg2);
            var msg3 = new NetMessage();
            msg3.Write(777);
            client.RmiToServer(5003, msg3, EncryptMode.Fast);
            var msg4 = new NetMessage();
            msg4.Write(888);
            session.RmiToClient(5004, msg4, EncryptMode.Fast);
            Assert.True(allReceived.Wait(MessageTimeout), "All encrypted messages should be received");
            Assert.Equal(42, secureClientToServer);
            Assert.Equal("Encrypted test message", receivedString);
            Assert.Equal(99, secureServerToClient);
            Assert.Equal(777, fastClientToServer);
            Assert.Equal(888, fastServerToClient);
        }

        [Fact(Timeout = 60000)]
        public async Task CustomKeyLengths_KeysGeneratedWithCorrectSizes()
        {
            var customSettings = new NetSettings
            {
                EncryptedMessageKeyLength = 128,
                FastEncryptedMessageKeyLength = 256
            };
            Server = await CreateServerAsync(netSettings: customSettings);
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);
            Assert.Equal(128 / 8, client.Crypt.GetKey().Length);
            Assert.Equal(256 / 8, client.Crypt.GetFastKey().Length);
        }

        [Fact(Timeout = 60000)]
        public async Task EncryptedMessages_LargePayload_CorrectlyTransmitted()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            byte[] receivedData = null;
            var messageReceived = new ManualResetEventSlim(false);

            Server.OnRMIRecieve += (_, message, _) =>
            {
                var byteArray = new ByteArray();
                message.Read(ref byteArray);
                receivedData = byteArray.GetBuffer();
                messageReceived.Set();
            };
            byte[] largeData = new byte[10000];
            new Random(42).NextBytes(largeData);

            var testMessage = new NetMessage();
            testMessage.Write(new ByteArray(largeData));
            client.RmiToServer(5005, testMessage);
            Assert.True(messageReceived.Wait(MessageTimeout));
            Assert.NotNull(receivedData);
            Assert.Equal(largeData.Length, receivedData.Length);
            Assert.Equal(largeData, receivedData);
        }

        [Fact(Timeout = 60000)]
        public async Task MultipleClients_EachHasUniqueKeys()
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

        [Fact(Timeout = 60000)]
        public async Task MixedEncryptionModes_SameSession_AllWorkCorrectly()
        {
            Server = await CreateServerAsync();
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            int secureReceived = 0;
            int fastReceived = 0;
            int noneReceived = 0;
            var allReceived = new CountdownEvent(3);

            Server.OnRMIRecieve += (_, message, rmiId) =>
            {
                message.Read(out int value);
                switch (rmiId)
                {
                    case 8001:
                        secureReceived = value;
                        break;
                    case 8002:
                        fastReceived = value;
                        break;
                    case 8003:
                        noneReceived = value;
                        break;
                }

                allReceived.Signal();
            };

            var msg1 = new NetMessage();
            msg1.Write(111);
            client.RmiToServer(8001, msg1);

            var msg2 = new NetMessage();
            msg2.Write(222);
            client.RmiToServer(8002, msg2, EncryptMode.Fast);

            var msg3 = new NetMessage();
            msg3.Write(333);
            client.RmiToServer(8003, msg3, EncryptMode.None);

            Assert.True(allReceived.Wait(MessageTimeout),
                "All messages with different encryption modes should be received");
            Assert.Equal(111, secureReceived);
            Assert.Equal(222, fastReceived);
            Assert.Equal(333, noneReceived);
        }
    }
}
