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
                .MinimumLevel.Information()                          // ← var: Debug
                .WriteTo.File(
                    path: "logs/gateway-.txt",
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 50 * 1024 * 1024,           // Max 50 MB per fil
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 7)                       // Behåll 7 dagars loggar
                .CreateLogger();
        }

        public static void Close()
        {
            Log.CloseAndFlush();
        }
    }
}
