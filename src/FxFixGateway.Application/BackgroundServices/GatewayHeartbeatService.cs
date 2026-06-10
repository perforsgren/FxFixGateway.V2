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
        private static readonly TimeSpan Interval      = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan OfflineTimeout = TimeSpan.FromSeconds(10);

        private readonly IGatewayHeartbeatRepository      _repo;
        private readonly ILogger<GatewayHeartbeatService> _logger;

        private readonly Dictionary<string, CancellationTokenSource> _sessions = new();
        private readonly List<Task> _pendingOfflineTasks = new();
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

            // Markera offline i DB — spåra tasken så StopAsync kan awaita den.
            var offlineTask = Task.Run(async () =>
            {
                try   { await _repo.SetOfflineAsync(sessionKey); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Heartbeat] Failed to set session {Session} OFFLINE", sessionKey);
                }
            });

            lock (_lock)
                _pendingOfflineTasks.Add(offlineTask);

            _logger.LogInformation("[Heartbeat] Session {Session} OFFLINE — heartbeat stopped", sessionKey);
        }

        /// <summary>
        /// Säkerställer att alla OFFLINE-skrivningar hinns med innan host-livscykeln avslutas.
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            // Markera eventuella sessioner som inte fick OnLogout (t.ex. hard kill)
            List<string> remainingSessions;
            lock (_lock)
                remainingSessions = [.. _sessions.Keys];

            foreach (var key in remainingSessions)
                SessionOffline(key);

            // Awaita alla pågående OFFLINE DB-skrivningar med timeout
            Task[] pending;
            lock (_lock)
                pending = [.. _pendingOfflineTasks];

            if (pending.Length > 0)
            {
                _logger.LogInformation("[Heartbeat] Waiting for {Count} OFFLINE write(s) to complete...", pending.Length);
                try
                {
                    await Task.WhenAny(Task.WhenAll(pending), Task.Delay(OfflineTimeout, cancellationToken));
                    _logger.LogInformation("[Heartbeat] All OFFLINE writes completed.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Heartbeat] One or more OFFLINE writes did not complete before shutdown.");
                }
            }

            await base.StopAsync(cancellationToken);
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