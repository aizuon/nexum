using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaseLib;
using Nexum.Client;
using Nexum.Core;
using Serilog;

namespace ExampleClient
{
    public static class Program
    {
        private static NetClient _client;

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

            _client = new NetClient(ServerType.Relay);
            _client.OnConnected += () =>
            {
                var enterServiceReq = new NetMessage();

                _client.RmiToServer(1, enterServiceReq);
            };
            await _client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 28000));

            await Task.Delay(Timeout.Infinite);
        }

        private static async void OnExit(object sender, EventArgs e)
        {
            _client?.Dispose();

            await Log.CloseAndFlushAsync();
        }
    }
}
