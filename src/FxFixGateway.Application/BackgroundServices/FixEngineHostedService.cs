using FxFixGateway.Domain.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FxFixGateway.Application.BackgroundServices
{
    public sealed class FixEngineHostedService : IHostedService
    {
        private readonly IFixEngine _fixEngine;
        private readonly ILogger<FixEngineHostedService> _logger;

        public FixEngineHostedService(
            IFixEngine fixEngine,
            ILogger<FixEngineHostedService> logger)
        {
            _fixEngine = fixEngine;
            _logger    = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FixEngineHostedService: stopping FIX engine...");
            try
            {
                var shutdownTask = _fixEngine.ShutdownAsync();
                await Task.WhenAny(shutdownTask, Task.Delay(TimeSpan.FromSeconds(8), cancellationToken));
                _logger.LogInformation("FixEngineHostedService: FIX engine stopped.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FIX engine did not stop cleanly");
            }
        }
    }
}