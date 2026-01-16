using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaseLib;
using Nexum.Client;
using Nexum.Core;
using Nexum.Server;
using Serilog;
using Xunit;
using Xunit.Abstractions;
using ClientP2PMember = Nexum.Client.P2PMember;

namespace Nexum.Tests.Integration
{
    public abstract class IntegrationTestBase : IAsyncLifetime
    {
        protected const int DefaultTcpPort = 38000;
        protected static readonly uint[] DefaultUdpPorts = { 39000, 39001, 39002, 39003 };
        protected static readonly IPAddress DefaultAddress = IPAddress.Loopback;

        // Normalized timeout constants for integration tests
        protected static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);
        protected static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);
        protected static readonly TimeSpan UdpSetupTimeout = TimeSpan.FromSeconds(15);
        protected static readonly TimeSpan LongOperationTimeout = TimeSpan.FromSeconds(30);

        private static int _portOffset;
        protected readonly List<NetClient> CreatedClients = new List<NetClient>();

        protected readonly ITestOutputHelper Output;
        protected NetServer Server;
        protected int TcpPort;
        protected uint[] UdpPorts;

        protected IntegrationTestBase(ITestOutputHelper output)
        {
            Output = output;

            Log.Logger = new LoggerConfiguration()
                .WriteTo.TestOutput(
                    output,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] |{SrcContext}| {Message}{NewLine}{Exception}")
                .Enrich.With<ContextEnricher>()
                .MinimumLevel.Debug()
                .CreateLogger();

            int offset = Interlocked.Increment(ref _portOffset);
            TcpPort = DefaultTcpPort + offset * 10;
            UdpPorts =
            [
                (uint)(DefaultUdpPorts[0] + offset * 10),
                (uint)(DefaultUdpPorts[1] + offset * 10),
                (uint)(DefaultUdpPorts[2] + offset * 10),
                (uint)(DefaultUdpPorts[3] + offset * 10)
            ];
        }

        public virtual Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public virtual async Task DisposeAsync()
        {
            var exceptions = new List<Exception>();

            foreach (var client in CreatedClients)
                try
                {
                    await DisposeClientAsync(client);
                }
                catch (Exception ex)
                {
                    Output.WriteLine($"Error disposing client: {ex.Message}");
                    exceptions.Add(ex);
                }

            CreatedClients.Clear();

            foreach (var client in NetClient.Clients.Values)
                try
                {
                    await DisposeClientAsync(client);
                }
                catch (Exception ex)
                {
                    Output.WriteLine($"Error disposing global client: {ex.Message}");
                    exceptions.Add(ex);
                }

            NetClient.Clients.Clear();

            if (Server != null)
                try
                {
                    await DisposeServerAsync(Server);
                    Server = null;
                }
                catch (Exception ex)
                {
                    Output.WriteLine($"Error disposing server: {ex.Message}");
                    exceptions.Add(ex);
                }

            await Task.Delay(100);

            if (exceptions.Count > 0)
                Output.WriteLine($"Cleanup completed with {exceptions.Count} error(s)");
        }

        private async Task DisposeClientAsync(NetClient client)
        {
            if (client == null)
                return;

            try
            {
                if (client.HostId != 0)
                {
                    client.Close();
                    await Task.Delay(50);
                }
            }
            catch
            {
            }

            try
            {
                client.Dispose();
            }
            catch
            {
            }
        }

        private async Task DisposeServerAsync(NetServer server)
        {
            if (server == null)
                return;

            try
            {
                foreach (var session in server.Sessions.Values)
                    try
                    {
                        session.Dispose();
                    }
                    catch
                    {
                    }

                server.SessionsInternal.Clear();

                await Task.Delay(50);

                server.Dispose();
            }
            catch
            {
            }
        }

        protected async Task<NetServer> CreateServerAsync(
            ServerType serverType = ServerType.Relay,
            bool allowDirectP2P = true,
            NetSettings netSettings = null,
            bool withUdp = true)
        {
            var endpoint = new IPEndPoint(DefaultAddress, TcpPort);
            var server = new NetServer(serverType, netSettings, allowDirectP2P);
            await server.ListenAsync(endpoint, withUdp ? UdpPorts : Array.Empty<uint>());
            return server;
        }

        protected async Task<NetClient> CreateClientAsync(
            ServerType serverType = ServerType.Relay)
        {
            var endpoint = new IPEndPoint(DefaultAddress, TcpPort);
            var client = new NetClient(serverType);
            await client.ConnectAsync(endpoint);
            CreatedClients.Add(client);
            return client;
        }

        protected async Task<bool> WaitForConditionAsync(
            Func<bool> condition,
            TimeSpan? timeout = null,
            TimeSpan? pollInterval = null)
        {
            var actualTimeout = timeout ?? MessageTimeout;
            var actualPollInterval = pollInterval ?? TimeSpan.FromMilliseconds(100);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < actualTimeout)
            {
                if (condition())
                    return true;

                await Task.Delay(actualPollInterval);
            }

            return condition();
        }

        protected async Task<bool> WaitForClientConnectionAsync(NetClient client, TimeSpan? timeout = null)
        {
            return await WaitForConditionAsync(() => client.HostId != 0, timeout ?? ConnectionTimeout);
        }

        protected async Task<bool> WaitForClientUdpEnabledAsync(NetClient client, TimeSpan? timeout = null)
        {
            return await WaitForConditionAsync(() => client.UdpEnabled, timeout ?? UdpSetupTimeout);
        }

        protected async Task<bool> WaitForSessionUdpEnabledAsync(NetSession session, TimeSpan? timeout = null)
        {
            return await WaitForConditionAsync(() => session.UdpEnabled, timeout ?? UdpSetupTimeout);
        }

        protected async Task<bool> WaitForP2PDirectConnectionAsync(ClientP2PMember member, TimeSpan? timeout = null)
        {
            return await WaitForConditionAsync(() => member.DirectP2P, timeout ?? UdpSetupTimeout);
        }
    }
}
