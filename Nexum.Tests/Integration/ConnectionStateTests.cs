using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexum.Core;
using Xunit;
using Xunit.Abstractions;

namespace Nexum.Tests.Integration
{
    [Collection("Integration")]
    public class ConnectionStateTests : IntegrationTestBase
    {
        public ConnectionStateTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(Timeout = 30000)]
        public async Task ConnectionState_FullLifecycle_AllEventsRaised()
        {
            Server = await CreateServerAsync();

            var clientStateChanges = new List<(ConnectionState Previous, ConnectionState New)>();
            var sessionStateChanges = new List<(ConnectionState Previous, ConnectionState New)>();
            var handshakingCalled = new ManualResetEventSlim(false);
            var connectedCalled = new ManualResetEventSlim(false);
            var disconnectedCalled = new ManualResetEventSlim(false);

            Server.SessionConnectionStateChanged += (_, args) =>
            {
                Output.WriteLine($"Session state: {args.PreviousState} -> {args.NewState}");
                sessionStateChanges.Add((args.PreviousState, args.NewState));
            };
            Server.OnSessionHandshaking = _ => handshakingCalled.Set();
            Server.OnSessionConnected = _ => connectedCalled.Set();
            Server.OnSessionDisconnected = _ => disconnectedCalled.Set();

            var client = await CreateClientAsync(configure: c =>
            {
                c.ConnectionStateChanged += (_, args) =>
                {
                    Output.WriteLine($"Client state: {args.PreviousState} -> {args.NewState}");
                    clientStateChanges.Add((args.PreviousState, args.NewState));
                };
            });

            await WaitForClientConnectionAsync(client);

            Assert.True(clientStateChanges.Count >= 3,
                $"Client should have at least 3 state changes, got {clientStateChanges.Count}");
            Assert.Equal((ConnectionState.Disconnected, ConnectionState.Connecting), clientStateChanges[0]);
            Assert.Equal((ConnectionState.Connecting, ConnectionState.Handshaking), clientStateChanges[1]);
            Assert.Equal((ConnectionState.Handshaking, ConnectionState.Connected), clientStateChanges[2]);
            Assert.Equal(ConnectionState.Connected, client.ConnectionState);

            Assert.True(sessionStateChanges.Count >= 2,
                $"Session should have at least 2 state changes, got {sessionStateChanges.Count}");
            Assert.Equal((ConnectionState.Disconnected, ConnectionState.Handshaking), sessionStateChanges[0]);
            Assert.Equal((ConnectionState.Handshaking, ConnectionState.Connected), sessionStateChanges[1]);
            var session = Server.Sessions.Values.First();
            Assert.Equal(ConnectionState.Connected, session.ConnectionState);

            Assert.True(handshakingCalled.IsSet, "OnSessionHandshaking should be called");
            Assert.True(connectedCalled.IsSet, "OnSessionConnected should be called");

            client.Close();
            await Task.Delay(300);
            client.Dispose();

            await WaitForConditionAsync(() => client.ConnectionState == ConnectionState.Disconnected,
                ConnectionTimeout);
            Assert.Equal(ConnectionState.Disconnected, client.ConnectionState);
            Assert.True(disconnectedCalled.Wait(ConnectionTimeout), "OnSessionDisconnected should be called");
        }
    }
}
