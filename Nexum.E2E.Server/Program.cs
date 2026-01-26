using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaseLib;
using BaseLib.Logging;
using Nexum.Core.Serialization;
using Nexum.E2E.Common;
using Nexum.Server.Core;
using Nexum.Server.Sessions;
using Serilog;

namespace Nexum.E2E.Server
{
    public static class Program
    {
        private const string ServerName = "Relay";
        private static readonly Guid ServerGuid = new Guid("a43a97d1-9ec7-495e-ad5f-8fe45fde1151");

        private static readonly ManualResetEventSlim ShutdownEvent = new ManualResetEventSlim(false);

        public static async Task<int> Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += Events.OnUnhandledException;
            TaskScheduler.UnobservedTaskException += Events.OnUnobservedTaskException;
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                ShutdownEvent.Set();
            };

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Async(console =>
                    console.Console(
                        outputTemplate:
                        "[{Timestamp:HH:mm:ss} {Level:u3}] |{SrcContext}| {Message}{NewLine}{Exception}"))
                .Enrich.With<ContextEnricher>()
                .MinimumLevel.Debug()
                .CreateLogger();

            var config = ParseArgs(args);
            if (config == null)
            {
                PrintUsage();
                return 1;
            }

            Log.Information("Starting E2E Server on {BindIp}:{TcpPort}, UDP ports: {UdpPorts}",
                config.BindIp, config.TcpPort, string.Join(",", config.UdpPorts));

            try
            {
                var server = new NetServer(ServerName, ServerGuid);

                server.OnSessionConnected += session =>
                {
                    Log.Information("Client connected: HostId={HostId}", session.HostId);
                };

                server.OnSessionDisconnected += session =>
                {
                    Log.Information("Client disconnected: HostId={HostId}", session.HostId);
                };

                server.OnRmiReceive += (session, message, rmiId) => { HandleRmi(server, session, message, rmiId); };

                var endpoint = new IPEndPoint(IPAddress.Parse(config.BindIp), config.TcpPort);
                await server.ListenAsync(endpoint, config.UdpPorts);

                Log.Information("E2E Server started successfully, waiting for shutdown signal...");

                ShutdownEvent.Wait();

                Log.Information("Shutdown signal received, stopping server...");
                server.Dispose();

                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "E2E Server failed to start");
                return 1;
            }
        }

        private static void HandleRmi(NetServer server, NetSession session, NetMessage message, ushort rmiId)
        {
            Log.Debug("Received RMI {RmiId} from HostId={HostId}", rmiId, session.HostId);

            switch (rmiId)
            {
                case E2EConstants.RmiJoinP2PGroup:
                    HandleJoinP2PGroup(server, session);
                    break;

                case E2EConstants.RmiTcpEcho:
                    HandleTcpEcho(session, message);
                    break;

                case E2EConstants.RmiUdpEcho:
                    HandleUdpEcho(session, message);
                    break;
            }
        }

        private static void HandleJoinP2PGroup(NetServer server, NetSession session)
        {
            Log.Information("Client {HostId} requesting to join P2P group", session.HostId);

            if (server.P2PGroups.Count == 0)
            {
                var group = server.CreateP2PGroup();
                Log.Information("Created P2P group {GroupId}", group.HostId);
            }

            var p2pGroup = server.P2PGroups.Values.First();
            p2pGroup.Join(session);
            Log.Information("Client {HostId} joined P2P group {GroupId}", session.HostId, p2pGroup.HostId);
        }

        private static void HandleTcpEcho(NetSession session, NetMessage message)
        {
            message.Read(out string payload);
            Log.Debug("TCP Echo from {HostId}: {Payload}", session.HostId, payload);

            var response = new NetMessage();
            response.Write($"ECHO:{payload}");
            session.RmiToClient(E2EConstants.RmiTcpEchoResponse, response);
        }

        private static void HandleUdpEcho(NetSession session, NetMessage message)
        {
            message.Read(out string payload);
            Log.Debug("UDP Echo from {HostId}: {Payload}", session.HostId, payload);

            var response = new NetMessage();
            response.Write($"ECHO:{payload}");
            session.RmiToClientUdpIfAvailable(E2EConstants.RmiUdpEchoResponse, response, reliable: true);
        }

        private static ServerConfig ParseArgs(string[] args)
        {
            var config = new ServerConfig
            {
                BindIp = "0.0.0.0",
                TcpPort = 28000,
                UdpPorts = new uint[] { 29000, 29001, 29002, 29003 }
            };

            for (int i = 0; i < args.Length; i++)
                switch (args[i])
                {
                    case "--bind-ip" when i + 1 < args.Length:
                        config.BindIp = args[++i];
                        break;
                    case "--tcp-port" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], out int tcpPort))
                            return null;
                        config.TcpPort = tcpPort;
                        break;
                    case "--udp-ports" when i + 1 < args.Length:
                        var ports = new List<uint>();
                        foreach (string p in args[++i].Split(','))
                        {
                            if (!uint.TryParse(p.Trim(), out uint port))
                                return null;
                            ports.Add(port);
                        }

                        config.UdpPorts = ports.ToArray();
                        break;
                    case "--help":
                    case "-h":
                        return null;
                }

            return config;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Nexum E2E Server");
            Console.WriteLine();
            Console.WriteLine("Usage: Nexum.E2E.Server [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --bind-ip <ip>        IP address to bind to (default: 0.0.0.0)");
            Console.WriteLine("  --tcp-port <port>     TCP port to listen on (default: 28000)");
            Console.WriteLine("  --udp-ports <ports>   Comma-separated UDP ports (default: 29000,29001,29002,29003)");
            Console.WriteLine("  -h, --help            Show this help message");
        }

        private class ServerConfig
        {
            public string BindIp { get; set; }
            public int TcpPort { get; set; }
            public uint[] UdpPorts { get; set; }
        }
    }
}
