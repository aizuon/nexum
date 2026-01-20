using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BaseLib;
using Nexum.Core;
using Nexum.Server;
using Serilog;

namespace ExampleServer
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

            var server = new NetServer(ServerType.Relay);
            server.OnRMIReceive += (session, _, rmiId) =>
            {
                switch (rmiId)
                {
                    case 1:
                        if (server.P2PGroups.Count == 0)
                            server.CreateP2PGroup();

                        server.P2PGroups.Values.First().Join(session);

                        break;
                }
            };
            await server.ListenAsync(new IPEndPoint(IPAddress.Loopback, 28000),
                new uint[] { 29000, 29001, 29002, 29003 });

            Console.ReadLine();
        }
    }
}
