using System;
using System.Threading.Tasks;
using Serilog;

namespace BaseLib
{
    public static class Events
    {
        private static readonly ILogger Logger = Log.ForContext("SourceContext", nameof(Events));

        public static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            Logger.Error("{Exception:l}", e.Exception.ToString());
        }

        public static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            if (args.ExceptionObject is Exception ex)
                Logger.Error("{Exception:l}", ex.ToString());
            else
                Logger.Error("{Exception:l}", args.ExceptionObject?.ToString() ?? "<null>");
        }

        public static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Environment.Exit(0);
        }
    }
}
