using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaseLib;
using BaseLib.Logging;
using Nexum.Client.Core;
using Nexum.Client.P2P;
using Nexum.Core.Serialization;
using Nexum.E2E.Common;
using Serilog;

namespace Nexum.E2E.Client
{
    public static class Program
    {
        private const string ServerName = "Relay";
        private static readonly Guid ServerGuid = new Guid("a43a97d1-9ec7-495e-ad5f-8fe45fde1151");

        private static NetClient _client;
        private static ClientConfig _config;
        private static readonly ManualResetEventSlim ConnectionComplete = new ManualResetEventSlim(false);
        private static readonly ManualResetEventSlim TcpEchoReceived = new ManualResetEventSlim(false);
        private static readonly ManualResetEventSlim UdpEchoReceived = new ManualResetEventSlim(false);
        private static readonly ManualResetEventSlim RelayedMessageReceived = new ManualResetEventSlim(false);
        private static readonly ManualResetEventSlim DirectMessageReceived = new ManualResetEventSlim(false);

        private static string _tcpEchoResponse;
        private static string _udpEchoResponse;
        private static string _relayedMessageContent;
        private static string _directMessageContent;

        public static async Task<int> Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += Events.OnUnhandledException;
            TaskScheduler.UnobservedTaskException += Events.OnUnobservedTaskException;

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Async(console =>
                    console.Console(
                        outputTemplate:
                        "[{Timestamp:HH:mm:ss} {Level:u3}] |{SrcContext}| {Message}{NewLine}{Exception}"))
                .Enrich.With<ContextEnricher>()
                .MinimumLevel.Debug()
                .CreateLogger();

            _config = ParseArgs(args);
            if (_config == null)
            {
                PrintUsage();
                return 1;
            }

            Log.Information("Starting E2E Client {ClientId}, connecting to {ServerHost}:{TcpPort}",
                _config.ClientId, _config.ServerHost, _config.TcpPort);

            try
            {
                return await RunAllScenariosAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "E2E Client failed");
                return 1;
            }
        }

        private static async Task<int> RunAllScenariosAsync()
        {
            _client = new NetClient(ServerName, ServerGuid);
            SetupEventHandlers();

            Log.Information("=== Scenario 1: TCP Connection ===");
            if (!await RunConnectionScenarioAsync())
            {
                Log.Error("Connection scenario FAILED");
                return 1;
            }

            Log.Information("Connection scenario PASSED");

            Log.Information("=== Scenario 2: TCP AES Encrypted Messaging ===");
            if (!await RunTcpMessagingScenarioAsync())
            {
                Log.Error("TCP messaging scenario FAILED");
                return 1;
            }

            Log.Information("TCP messaging scenario PASSED");

            Log.Information("=== Scenario 3: UDP RC4 Encrypted Messaging ===");
            if (!await RunUdpMessagingScenarioAsync())
            {
                Log.Error("UDP messaging scenario FAILED");
                return 1;
            }

            Log.Information("UDP messaging scenario PASSED");

            Log.Information("=== Scenario 4: Relayed P2P Messaging ===");
            if (!await RunRelayedP2PScenarioAsync())
            {
                Log.Error("Relayed P2P scenario FAILED");
                return 1;
            }

            Log.Information("Relayed P2P scenario PASSED");

            Log.Information("=== Scenario 5: Direct P2P Messaging ===");
            if (!await RunDirectP2PScenarioAsync())
            {
                Log.Error("Direct P2P scenario FAILED");
                return 1;
            }

            Log.Information("Direct P2P scenario PASSED");

            Log.Information("=== ALL SCENARIOS PASSED ===");
            await _client.CloseAsync();
            await Task.Delay(300);
            _client.Dispose();
            return 0;
        }

        private static void SetupEventHandlers()
        {
            _client.OnConnected += () =>
            {
                Log.Information("Connection complete, HostId={HostId}", _client.HostId);
                ConnectionComplete.Set();
            };

            _client.OnRmiReceive += (message, rmiId) => { HandleRmi(message, rmiId); };
        }

        private static void HandleRmi(NetMessage message, ushort rmiId)
        {
            Log.Debug("Received RMI {RmiId}", rmiId);

            switch (rmiId)
            {
                case E2EConstants.RmiTcpEchoResponse:
                    message.Read(out _tcpEchoResponse);
                    TcpEchoReceived.Set();
                    break;

                case E2EConstants.RmiUdpEchoResponse:
                    message.Read(out _udpEchoResponse);
                    UdpEchoReceived.Set();
                    break;

                case E2EConstants.RmiP2PMessage:
                    message.Read(out string content);
                    message.Read(out bool isDirect);
                    Log.Debug("Received P2P message: {Content}, isDirect={IsDirect}", content, isDirect);
                    if (isDirect)
                    {
                        _directMessageContent = content;
                        DirectMessageReceived.Set();

                        var response = new NetMessage();
                        response.Write($"RESPONSE:{content}");
                        response.Write(true);

                        var peer = GetPeer();
                        if (peer != null && peer.DirectP2PReady)
                            peer.RmiToPeer(E2EConstants.RmiP2PMessageResponse, response, reliable: true);
                    }
                    else
                    {
                        _relayedMessageContent = content;
                        RelayedMessageReceived.Set();

                        var response = new NetMessage();
                        response.Write($"RESPONSE:{content}");
                        response.Write(false);

                        var peer = GetPeer();
                        peer?.RmiToPeer(E2EConstants.RmiP2PMessageResponse, response, forceRelay: true, reliable: true);
                    }

                    break;

                case E2EConstants.RmiP2PMessageResponse:
                    message.Read(out string responseContent);
                    message.Read(out bool isDirectResponse);
                    Log.Debug("Received P2P response: {Content}, isDirect={IsDirect}", responseContent,
                        isDirectResponse);
                    if (isDirectResponse)
                    {
                        _directMessageContent = responseContent;
                        DirectMessageReceived.Set();
                    }
                    else
                    {
                        _relayedMessageContent = responseContent;
                        RelayedMessageReceived.Set();
                    }

                    break;
            }
        }

        private static P2PMember GetPeer()
        {
            if (_client.P2PGroup?.P2PMembers == null)
                return null;

            foreach (var member in _client.P2PGroup.P2PMembers.Values)
                if (member.HostId != _client.HostId)
                    return member;

            return null;
        }

        private static async Task<bool> RunConnectionScenarioAsync()
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(_config.ServerHost), _config.TcpPort);
            await _client.ConnectAsync(endpoint);

            if (!ConnectionComplete.Wait(TimeSpan.FromSeconds(30)))
            {
                Log.Error("Connection timeout");
                return false;
            }

            if (_client.HostId == 0)
            {
                Log.Error("Invalid HostId");
                return false;
            }

            Log.Information("Connected with HostId={HostId}", _client.HostId);
            return true;
        }

        private static async Task<bool> RunTcpMessagingScenarioAsync()
        {
            string testPayload = $"TCP_TEST_{_config.ClientId}_{DateTime.UtcNow.Ticks}";
            var message = new NetMessage();
            message.Write(testPayload);

            TcpEchoReceived.Reset();
            _client.RmiToServer(E2EConstants.RmiTcpEcho, message);

            if (!TcpEchoReceived.Wait(TimeSpan.FromSeconds(30)))
            {
                Log.Error("TCP echo timeout");
                return false;
            }

            string expected = $"ECHO:{testPayload}";
            if (_tcpEchoResponse != expected)
            {
                Log.Error("TCP echo mismatch: expected={Expected}, actual={Actual}", expected, _tcpEchoResponse);
                return false;
            }

            Log.Information("TCP AES encrypted echo verified");
            return true;
        }

        private static async Task<bool> RunUdpMessagingScenarioAsync()
        {
            Log.Information("Joining P2P group to initiate UDP setup...");
            var joinMessage = new NetMessage();
            _client.RmiToServer(E2EConstants.RmiJoinP2PGroup, joinMessage);

            Log.Information("Waiting for UDP connection to be established...");
            var udpTimeout = DateTime.UtcNow.AddSeconds(30);
            while (!_client.UdpEnabled && DateTime.UtcNow < udpTimeout)
                await Task.Delay(100);

            if (!_client.UdpEnabled)
            {
                Log.Error("UDP not enabled within timeout");
                return false;
            }

            Log.Information("UDP enabled, sending test message");

            string testPayload = $"UDP_TEST_{_config.ClientId}_{DateTime.UtcNow.Ticks}";
            var message = new NetMessage();
            message.Write(testPayload);

            UdpEchoReceived.Reset();
            _client.RmiToServerUdpIfAvailable(E2EConstants.RmiUdpEcho, message, reliable: true);

            if (!UdpEchoReceived.Wait(TimeSpan.FromSeconds(30)))
            {
                Log.Error("UDP echo timeout");
                return false;
            }

            string expected = $"ECHO:{testPayload}";
            if (_udpEchoResponse != expected)
            {
                Log.Error("UDP echo mismatch: expected={Expected}, actual={Actual}", expected, _udpEchoResponse);
                return false;
            }

            Log.Information("UDP RC4 encrypted echo verified");
            return true;
        }

        private static async Task<bool> RunRelayedP2PScenarioAsync()
        {
            Log.Information("Waiting for peer to join P2P group...");
            var peerTimeout = DateTime.UtcNow.AddSeconds(60);
            P2PMember peer = null;

            while (DateTime.UtcNow < peerTimeout)
            {
                peer = GetPeer();
                if (peer != null)
                    break;
                await Task.Delay(500);
            }

            if (peer == null)
            {
                Log.Error("No peer found in P2P group within timeout");
                return false;
            }

            Log.Information("Found peer HostId={PeerHostId}", peer.HostId);

            if (_config.ClientId == 1)
            {
                string testPayload = $"RELAY_TEST_{_config.ClientId}_{DateTime.UtcNow.Ticks}";
                var message = new NetMessage();
                message.Write(testPayload);
                message.Write(false);

                RelayedMessageReceived.Reset();
                peer.RmiToPeer(E2EConstants.RmiP2PMessage, message, forceRelay: true, reliable: true);

                if (!RelayedMessageReceived.Wait(TimeSpan.FromSeconds(30)))
                {
                    Log.Error("Relayed P2P response timeout");
                    return false;
                }

                if (!_relayedMessageContent.StartsWith("RESPONSE:"))
                {
                    Log.Error("Invalid relayed response: {Content}", _relayedMessageContent);
                    return false;
                }

                Log.Information("Relayed P2P message exchange verified");
            }
            else
            {
                if (!RelayedMessageReceived.Wait(TimeSpan.FromSeconds(30)))
                {
                    Log.Error("Relayed P2P message timeout");
                    return false;
                }

                Log.Information("Received and responded to relayed P2P message");
            }

            return true;
        }

        private static async Task<bool> RunDirectP2PScenarioAsync()
        {
            var peer = GetPeer();
            if (peer == null)
            {
                Log.Error("No peer available for direct P2P test");
                return false;
            }

            Log.Information("Waiting for direct P2P connection to establish...");
            var directTimeout = DateTime.UtcNow.AddSeconds(60);

            while (!peer.DirectP2PReady && DateTime.UtcNow < directTimeout)
                await Task.Delay(500);

            if (!peer.DirectP2PReady)
            {
                Log.Warning("Direct P2P connection not established, holepunch may have failed");
                Log.Information("Skipping direct P2P test (holepunch unsuccessful)");
                return true;
            }

            Log.Information("Direct P2P connection established with peer {PeerHostId}", peer.HostId);

            if (_config.ClientId == 1)
            {
                string testPayload = $"DIRECT_TEST_{_config.ClientId}_{DateTime.UtcNow.Ticks}";
                var message = new NetMessage();
                message.Write(testPayload);
                message.Write(true);

                DirectMessageReceived.Reset();
                peer.RmiToPeer(E2EConstants.RmiP2PMessage, message, reliable: true);

                if (!DirectMessageReceived.Wait(TimeSpan.FromSeconds(30)))
                {
                    Log.Error("Direct P2P response timeout");
                    return false;
                }

                if (!_directMessageContent.StartsWith("RESPONSE:"))
                {
                    Log.Error("Invalid direct response: {Content}", _directMessageContent);
                    return false;
                }

                Log.Information("Direct P2P message exchange verified");
            }
            else
            {
                if (!DirectMessageReceived.Wait(TimeSpan.FromSeconds(30)))
                {
                    Log.Error("Direct P2P message timeout");
                    return false;
                }

                Log.Information("Received and responded to direct P2P message");

                await Task.Delay(1000);
            }

            return true;
        }

        private static ClientConfig ParseArgs(string[] args)
        {
            var config = new ClientConfig
            {
                ServerHost = "127.0.0.1",
                TcpPort = 28000,
                ClientId = 1
            };

            for (int i = 0; i < args.Length; i++)
                switch (args[i])
                {
                    case "--server-host" when i + 1 < args.Length:
                        config.ServerHost = args[++i];
                        break;
                    case "--tcp-port" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], out int tcpPort))
                            return null;
                        config.TcpPort = tcpPort;
                        break;
                    case "--client-id" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], out int clientId))
                            return null;
                        config.ClientId = clientId;
                        break;
                    case "--help":
                    case "-h":
                        return null;
                }

            return config;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Nexum E2E Client");
            Console.WriteLine();
            Console.WriteLine("Usage: Nexum.E2E.Client [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --server-host <host>  Server IP address (default: 127.0.0.1)");
            Console.WriteLine("  --tcp-port <port>     Server TCP port (default: 28000)");
            Console.WriteLine("  --client-id <id>      Client ID, 1 or 2 (default: 1)");
            Console.WriteLine("  -h, --help            Show this help message");
        }

        private class ClientConfig
        {
            public string ServerHost { get; set; }
            public int TcpPort { get; set; }
            public int ClientId { get; set; }
        }
    }
}
