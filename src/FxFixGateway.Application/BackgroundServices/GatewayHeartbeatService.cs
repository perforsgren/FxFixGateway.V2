using FxFixGateway.Domain.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.BackgroundServices
{
    /// <summary>
    /// Hanterar per-session heartbeats mot fxvol.session_heartbeat.
    /// Startas/stoppas av QuickFixApplication via ISessionHeartbeatNotifier.
    /// </summary>
    public class GatewayHeartbeatService : BackgroundService, ISessionHeartbeatNotifier
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

        private readonly IGatewayHeartbeatRepository      _repo;
        private readonly ILogger<GatewayHeartbeatService> _logger;

        private readonly Dictionary<string, CancellationTokenSource> _sessions = new();
        private readonly object _lock = new();

        public GatewayHeartbeatService(
            IGatewayHeartbeatRepository repo,
            ILogger<GatewayHeartbeatService> logger)
        {
            _repo   = repo   ?? throw new ArgumentNullException(nameof(repo));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Krävs av BackgroundService men vi startar inget globalt här
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

        /// <summary>
        /// Anropas av QuickFixApplication.OnLogon — startar heartbeat-loop för sessionen.
        /// </summary>
        public void SessionOnline(string sessionKey)
        {
            lock (_lock)
            {
                if (_sessions.ContainsKey(sessionKey))
                    return; // Loop redan aktiv

                var cts = new CancellationTokenSource();
                _sessions[sessionKey] = cts;

                _ = Task.Run(() => RunLoopAsync(sessionKey, cts.Token));
            }

            _logger.LogInformation("[Heartbeat] Session {Session} ONLINE — heartbeat started", sessionKey);
        }

        /// <summary>
        /// Anropas av QuickFixApplication.OnLogout — stoppar loopen och markerar sessionen offline.
        /// </summary>
        public void SessionOffline(string sessionKey)
        {
            CancellationTokenSource? cts;

            lock (_lock)
            {
                if (!_sessions.TryGetValue(sessionKey, out cts))
                    return;

                _sessions.Remove(sessionKey);
            }

            cts.Cancel();
            cts.Dispose();

            // Markera offline i DB direkt utan att vänta på nästa beat
            _ = Task.Run(async () =>
            {
                try   { await _repo.SetOfflineAsync(sessionKey); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Heartbeat] Failed to set session {Session} OFFLINE", sessionKey);
                }
            });

            _logger.LogInformation("[Heartbeat] Session {Session} OFFLINE — heartbeat stopped", sessionKey);
        }

        private async Task RunLoopAsync(string sessionKey, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _repo.UpdateBeatAsync(sessionKey);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Heartbeat] Failed to update beat for session {Session}", sessionKey);
                }

                try { await Task.Delay(Interval, token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}