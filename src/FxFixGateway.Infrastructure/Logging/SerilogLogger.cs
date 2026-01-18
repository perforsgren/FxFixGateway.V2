using Microsoft.Extensions.Logging;
using Serilog;
using System;

namespace FxFixGateway.Infrastructure.Logging
{
    /// <summary>
    /// Wrapper för Serilog.
    /// Konfigureras vid startup i Application-lagret.
    /// </summary>
    public static class SerilogConfiguration
    {
        public static void Configure()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/gateway-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }

        public static void Close()
        {
            Log.CloseAndFlush();
        }
    }
}
