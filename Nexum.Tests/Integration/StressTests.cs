using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Client.Core;
using Nexum.Core.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Nexum.Tests.Integration
{
    [Collection("Integration")]
    public class StressTests : IntegrationTestBase
    {
        public StressTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(Timeout = 300000)]
        public async Task StressTest_MultiClient_WholeFlow()
        {
            const int clientCount = 12;

            Output.WriteLine($"[TEST] Starting whole flow stress test with {clientCount} clients");

            Server = await CreateServerAsync();
            Output.WriteLine($"[TEST] Server created and listening on TCP port {TcpPort}");

            var clients = new List<NetClient>();
            for (int i = 0; i < clientCount; i++)
            {
                var client = await CreateClientAsync();
                clients.Add(client);
                Output.WriteLine($"[TEST] Client {i + 1}/{clientCount} created");
            }

            Output.WriteLine("[TEST] Waiting for all clients to connect...");
            var connectionTasks = clients.Select((client, index) =>
                WaitForClientConnectionAsync(client, GetAdjustedTimeout(ConnectionTimeout))
                    .ContinueWith(t =>
                    {
                        if (t.Result)
                            Output.WriteLine($"[TEST] Client {index + 1} connected with HostId {client.HostId}");
                        else
                            Output.WriteLine($"[TEST] Client {index + 1} FAILED to connect");
                        return t.Result;
                    })).ToArray();

            bool[] connectionResults = await Task.WhenAll(connectionTasks);
            Assert.True(connectionResults.All(r => r), "All clients should connect successfully");

            var group = Server.CreateP2PGroup();
            foreach (var client in clients)
            {
                var session = Server.Sessions[client.HostId];
                group.Join(session);
            }

            Output.WriteLine($"[TEST] All {clientCount} clients joined P2P group");

            Output.WriteLine("[TEST] Waiting for UDP to be enabled on all clients...");
            var udpTasks = clients.Select((client, index) =>
                WaitForClientUdpEnabledAsync(client, GetAdjustedTimeout(UdpSetupTimeout))
                    .ContinueWith(t =>
                    {
                        if (t.Result)
                            Output.WriteLine($"[TEST] Client {index + 1} (HostId {client.HostId}) UDP enabled");
                        else
                            Output.WriteLine(
                                $"[TEST] Client {index + 1} (HostId {client.HostId}) UDP FAILED to enable");
                        return t.Result;
                    })).ToArray();

            bool[] udpResults = await Task.WhenAll(udpTasks);
            Assert.True(udpResults.All(r => r), "All clients should have UDP enabled");

            Output.WriteLine("[TEST] Waiting for P2P member discovery...");
            var p2pMemberTasks = clients.Select((client, index) =>
                WaitForConditionAsync(
                        () =>
                        {
                            if (client.P2PGroup == null)
                                return false;
                            var otherHostIds = clients.Where(c => c != client).Select(c => c.HostId).ToList();
                            return otherHostIds.All(hostId => client.P2PGroup.P2PMembers.ContainsKey(hostId));
                        },
                        GetAdjustedTimeout(MessageTimeout))
                    .ContinueWith(t =>
                    {
                        if (t.Result)
                            Output.WriteLine(
                                $"[TEST] Client {index + 1} (HostId {client.HostId}) sees all P2P members");
                        else
                            Output.WriteLine(
                                $"[TEST] Client {index + 1} (HostId {client.HostId}) FAILED to see all P2P members");
                        return t.Result;
                    })).ToArray();

            bool[] p2pMemberResults = await Task.WhenAll(p2pMemberTasks);
            Assert.True(p2pMemberResults.All(r => r), "All clients should see all other clients as P2P members");

            Output.WriteLine("[TEST] Waiting for direct P2P connections between all pairs...");
            var directP2PTasks = new List<Task<bool>>();

            for (int i = 0; i < clients.Count; i++)
            {
                var client = clients[i];
                for (int j = 0; j < clients.Count; j++)
                {
                    if (i == j)
                        continue;

                    var otherClient = clients[j];
                    int clientIndex = i;
                    int otherIndex = j;

                    var task = WaitForConditionAsync(
                            () =>
                            {
                                if (client.P2PGroup?.P2PMembers.TryGetValue(otherClient.HostId, out var peer) != true)
                                    return false;
                                return peer.DirectP2PReady;
                            },
                            GetAdjustedTimeout(LongOperationTimeout))
                        .ContinueWith(t =>
                        {
                            if (t.Result)
                                Output.WriteLine(
                                    $"[TEST] Direct P2P ready: Client {clientIndex + 1} -> Client {otherIndex + 1}");
                            else
                                Output.WriteLine(
                                    $"[TEST] Direct P2P FAILED: Client {clientIndex + 1} -> Client {otherIndex + 1}");
                            return t.Result;
                        });

                    directP2PTasks.Add(task);
                }
            }

            bool[] directP2PResults = await Task.WhenAll(directP2PTasks);
            int successCount = directP2PResults.Count(r => r);
            int totalPairs = directP2PResults.Length;
            Output.WriteLine($"[TEST] Direct P2P connections: {successCount}/{totalPairs} successful");

            Assert.True(directP2PResults.All(r => r),
                $"All client pairs should have direct P2P connections. {successCount}/{totalPairs} succeeded.");

            Output.WriteLine("\n[TEST] === P2P Socket Address Dump ===");
            foreach (var client in clients)
            foreach (var otherClient in clients)
            {
                if (client == otherClient)
                    continue;

                Assert.True(client.P2PGroup.P2PMembers.ContainsKey(otherClient.HostId),
                    $"Client {client.HostId} should have peer {otherClient.HostId}");

                var peer = client.P2PGroup.P2PMembers[otherClient.HostId];
                Assert.True(peer.DirectP2P,
                    $"Client {client.HostId} -> {otherClient.HostId} should have DirectP2P");
                Assert.True(peer.DirectP2PReady,
                    $"Client {client.HostId} -> {otherClient.HostId} should be DirectP2PReady");
            }

            Output.WriteLine("[TEST] === End Socket Dump ===\n");

            Output.WriteLine("[TEST] Testing P2P messaging between all pairs...");
            const ushort testRmiId = 8001;
            var receivedMessages = new ConcurrentDictionary<(uint sender, uint receiver), int>();
            var messageReceivedEvents = new ConcurrentDictionary<(uint sender, uint receiver), ManualResetEventSlim>();

            foreach (var client in clients)
            {
                foreach (var otherClient in clients)
                {
                    if (client == otherClient)
                        continue;

                    var key = (otherClient.HostId, client.HostId);
                    messageReceivedEvents[key] = new ManualResetEventSlim(false);
                }

                var receiver = client;
                client.OnRmiReceive += (msg, rmiId) =>
                {
                    if (rmiId != testRmiId)
                        return;

                    msg.Read(out uint senderHostId);
                    msg.Read(out int value);

                    var key = (senderHostId, receiver.HostId);
                    receivedMessages[key] = value;
                    if (messageReceivedEvents.TryGetValue(key, out var evt))
                        evt.Set();
                };
            }

            int expectedValue = 1;
            var expectedValues = new Dictionary<(uint sender, uint receiver), int>();

            await Task.Delay(500);

            foreach (var client in clients)
            foreach (var otherClient in clients)
            {
                if (client == otherClient)
                    continue;

                var peer = client.P2PGroup.P2PMembers[otherClient.HostId];
                var message = new NetMessage();
                message.Write(client.HostId);
                message.Write(expectedValue);
                expectedValues[(client.HostId, otherClient.HostId)] = expectedValue;
                Output.WriteLine(
                    $"[TEST] Sending message from {client.HostId} to {otherClient.HostId} with value {expectedValue}");
                peer.RmiToPeer(testRmiId, message, reliable: true);
                expectedValue++;
            }

            var timeout = GetAdjustedTimeout(MessageTimeout);
            bool allReceived = true;
            foreach (var kvp in messageReceivedEvents)
                if (!kvp.Value.Wait(timeout))
                {
                    Output.WriteLine($"[TEST] Message from {kvp.Key.sender} to {kvp.Key.receiver} NOT received");
                    allReceived = false;
                }

            Assert.True(allReceived, "All P2P messages should be received");

            foreach (var kvp in expectedValues)
            {
                Assert.True(receivedMessages.TryGetValue(kvp.Key, out int actualValue),
                    $"Message from {kvp.Key.sender} to {kvp.Key.receiver} should be received");
                Assert.Equal(kvp.Value, actualValue);
            }

            int expectedPairs = clientCount * (clientCount - 1);
            Output.WriteLine(
                $"[TEST] Successfully exchanged {receivedMessages.Count}/{expectedPairs} P2P messages between {clientCount} clients");

            foreach (var evt in messageReceivedEvents.Values)
                evt.Dispose();
        }
    }
}
