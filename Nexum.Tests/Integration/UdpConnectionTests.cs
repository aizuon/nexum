using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Core;
using Nexum.Core.Simulation;
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
        public async Task UdpConnection_Holepunch_UdpEnabled(string profileName)
        {
            var profile = NetworkProfile.GetByName(profileName);
            SetupNetworkSimulation(profile);

            Server = await CreateServerAsync(withUdp: true);
            Assert.True(Server.UdpEnabled);
            Assert.Equal(UdpPorts.Length, Server.UdpSockets.Count);

            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();
            p2pGroup.Join(session);

            bool clientUdpEnabled = await WaitForClientUdpEnabledAsync(client, GetAdjustedTimeout(UdpSetupTimeout));
            bool sessionUdpEnabled = await WaitForSessionUdpEnabledAsync(session, GetAdjustedTimeout(UdpSetupTimeout));

            Assert.True(clientUdpEnabled, $"[{profileName}] Client UDP should be enabled after holepunch");
            Assert.True(sessionUdpEnabled, $"[{profileName}] Session UDP should be enabled after holepunch");
            Assert.NotNull(client.UdpChannel);
            Assert.NotNull(session.UdpSocket);
            Assert.NotNull(session.UdpEndPoint);

            LogSimulationStatistics();
        }

        [Theory(Timeout = 180000)]
        [MemberData(nameof(NetworkProfiles))]
        public async Task UdpMessaging_Bidirectional_MessagesExchanged(string profileName)
        {
            var profile = NetworkProfile.GetByName(profileName);
            SetupNetworkSimulation(profile);

            Server = await CreateServerAsync(withUdp: true);
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            var session = Server.Sessions.Values.First();
            var p2pGroup = Server.CreateP2PGroup();
            p2pGroup.Join(session);

            await WaitForClientUdpEnabledAsync(client, GetAdjustedTimeout(UdpSetupTimeout));
            await WaitForSessionUdpEnabledAsync(session, GetAdjustedTimeout(UdpSetupTimeout));

            int clientToServerValue = 0;
            int serverToClientValue = 0;
            var clientToServerReceived = new ManualResetEventSlim(false);
            var serverToClientReceived = new ManualResetEventSlim(false);

            Server.OnRMIReceive += (_, msg, _) =>
            {
                msg.Read(out clientToServerValue);
                clientToServerReceived.Set();
            };

            client.OnRMIReceive += (msg, _) =>
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

            Assert.True(clientToServerReceived.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                $"[{profileName}] Server should receive UDP message");
            Assert.True(serverToClientReceived.Wait(GetAdjustedTimeout(ConnectionTimeout)),
                $"[{profileName}] Client should receive UDP message");
            Assert.Equal(12345, clientToServerValue);
            Assert.Equal(67890, serverToClientValue);

            LogSimulationStatistics();
        }

        [Fact(Timeout = 90000)]
        public async Task UdpMessaging_WithoutP2PGroup_FallsBackToTcp()
        {
            Server = await CreateServerAsync(withUdp: true);
            var client = await CreateClientAsync();
            await WaitForClientConnectionAsync(client);

            int receivedValue = 0;
            var messageReceived = new ManualResetEventSlim(false);

            Server.OnRMIReceive += (_, msg, _) =>
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
    }
}
