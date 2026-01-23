using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaseLib;
using Nexum.Server;
using Serilog;

namespace ExampleServer
{
    public static class Program
    {
        private const string ServerName = "Relay";
        private static readonly Guid ServerGuid = new Guid("a43a97d1-9ec7-495e-ad5f-8fe45fde1151");

        private static NetServer _server;

        public static async Task Main()
        {
            AppDomain.CurrentDomain.UnhandledException += Events.OnUnhandledException;
            TaskScheduler.UnobservedTaskException += Events.OnUnobservedTaskException;
            Console.CancelKeyPress += Events.OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit += OnExit;

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Async(console =>
                    console.Console(
                        outputTemplate:
                        "[{Timestamp:HH:mm:ss} {Level:u3}] |{SrcContext}| {Message}{NewLine}{Exception}"))
                .Enrich.With<ContextEnricher>()
#if DEBUG
                .MinimumLevel.Verbose()
#else
                         .MinimumLevel.Information()
#endif
                .CreateLogger();

            _server = new NetServer(ServerName, ServerGuid);
            _server.OnRMIReceive += (session, _, rmiId) =>
            {
                switch (rmiId)
                {
                    case 1:
                        if (_server.P2PGroups.Count == 0)
                            _server.CreateP2PGroup();

                        _server.P2PGroups.Values.First().Join(session);

                        break;
                }
            };
            await _server.ListenAsync(new IPEndPoint(IPAddress.Loopback, 28000),
                new uint[] { 29000, 29001, 29002, 29003 });

            await Task.Delay(Timeout.Infinite);
        }

        private static async void OnExit(object sender, EventArgs e)
        {
            _server?.Dispose();

            await Log.CloseAndFlushAsync();
        }
    }
}
