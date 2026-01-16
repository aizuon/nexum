using System;
using System.Net;
using System.Threading.Tasks;
using BaseLib;
using Nexum.Client;
using Nexum.Core;
using Serilog;

namespace TestClient
{
    public static class Program
    {
        public static async Task Main()
        {
            AppDomain.CurrentDomain.UnhandledException += Events.OnUnhandledException;
            TaskScheduler.UnobservedTaskException += Events.OnUnobservedTaskException;
            Console.CancelKeyPress += Events.OnCancelKeyPress;

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

            var client = new NetClient(ServerType.Relay);
            client.OnConnectionComplete += () =>
            {
                var enterServiceReq = new NetMessage();

                client.RmiToServer(1, enterServiceReq);
            };
            await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 28000));

            Console.ReadLine();
        }
    }
}
