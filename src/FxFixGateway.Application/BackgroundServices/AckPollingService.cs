using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.BackgroundServices
{
    /// <summary>
    /// Background service som pollar databasen för pending ACKs
    /// och skickar AR-meddelanden via FixEngine.
    /// </summary>
    public sealed class AckPollingService : BackgroundService
    {
        private readonly IAckQueueRepository _ackQueueRepository;
        private readonly IFixEngine _fixEngine;
        private readonly ILogger<AckPollingService> _logger;

        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

        // Track which sessions are logged on
        private readonly HashSet<string> _loggedOnSessions = new();

        public AckPollingService(
            IAckQueueRepository ackQueueRepository,
            IFixEngine fixEngine,
            ILogger<AckPollingService> logger)
        {
            _ackQueueRepository = ackQueueRepository ?? throw new ArgumentNullException(nameof(ackQueueRepository));
            _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Subscribe to session status changes
            _fixEngine.StatusChanged += OnSessionStatusChanged;
        }

        public override void Dispose()
        {
            _fixEngine.StatusChanged -= OnSessionStatusChanged;
            base.Dispose();
        }

        private void OnSessionStatusChanged(object? sender, Domain.Events.SessionStatusChangedEvent e)
        {
            if (e.NewStatus == SessionStatus.LoggedOn)
            {
                lock (_loggedOnSessions)
                {
                    _loggedOnSessions.Add(e.SessionKey);
                }
                _logger.LogInformation("Session {SessionKey} is now LoggedOn - ACK sending enabled", e.SessionKey);
            }
            else if (e.NewStatus == SessionStatus.Stopped || 
                     e.NewStatus == SessionStatus.Disconnecting || 
                     e.NewStatus == SessionStatus.Error)
            {
                lock (_loggedOnSessions)
                {
                    _loggedOnSessions.Remove(e.SessionKey);
                }
                _logger.LogInformation("Session {SessionKey} is no longer LoggedOn - ACK sending disabled", e.SessionKey);
            }
        }

        private bool IsSessionLoggedOn(string sessionKey)
        {
            lock (_loggedOnSessions)
            {
                return _loggedOnSessions.Contains(sessionKey);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ACK Polling Service started");

            // Vänta lite innan vi börjar (låt andra services starta först)
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingAcksAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing pending ACKs");
                }

                // Vänta innan nästa polling
                await Task.Delay(_pollingInterval, stoppingToken);
            }

            _logger.LogInformation("ACK Polling Service stopped");
        }

        private async Task ProcessPendingAcksAsync(CancellationToken cancellationToken)
        {
            // Hämta pending ACKs från DB
            var pendingAcks = await _ackQueueRepository.GetPendingAcksAsync(maxCount: 100);
            var ackList = pendingAcks.ToList();

            if (ackList.Count == 0)
            {
                return;
            }

            // Group by session and only process sessions that are logged on
            var acksBySession = ackList.GroupBy(a => a.SessionKey);

            foreach (var sessionGroup in acksBySession)
            {
                var sessionKey = sessionGroup.Key;

                // First check: Skip entire session group if not logged on (optimization)
                // NOTE: Session could log off after this check - ProcessSingleAckAsync does a second check
                if (!IsSessionLoggedOn(sessionKey))
                {
                    _logger.LogDebug("Skipping {Count} pending ACKs for session {SessionKey} - not logged on",
                        sessionGroup.Count(), sessionKey);
                    continue;
                }

                _logger.LogInformation("Processing {Count} pending ACKs for session {SessionKey}",
                    sessionGroup.Count(), sessionKey);

                foreach (var ack in sessionGroup)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await ProcessSingleAckAsync(ack);
                }
            }
        }

        private async Task ProcessSingleAckAsync(Domain.ValueObjects.PendingAck ack)
        {
            // Double-check session is still logged on before sending
            if (!IsSessionLoggedOn(ack.SessionKey))
            {
                _logger.LogWarning("Session {SessionKey} logged off before ACK could be sent for Trade {TradeId}",
                    ack.SessionKey, ack.TradeId);
                return;
            }

            // Validate required fields - InternTradeId (tag 818) is required for a complete AR message
            if (string.IsNullOrEmpty(ack.InternTradeId))
            {
                _logger.LogWarning(
                    "Skipping ACK for Trade {TradeId} - AckInternalTradeId is null/empty. " +
                    "Trade may not have been processed by downstream system yet. SessionKey={SessionKey}, TradeReportId={TradeReportId}",
                    ack.TradeId, ack.SessionKey, ack.TradeReportId);
                return;
            }

            try
            {
                _logger.LogDebug("Processing ACK for Trade {TradeId}: {TradeReportId} → {InternTradeId}",
                    ack.TradeId, ack.TradeReportId, ack.InternTradeId);

                var arMessage = FixMessageBuilder.BuildTradeCaptureReportAck(ack.TradeReportId, ack.InternTradeId);

                _logger.LogInformation("Sending AR for Trade {TradeId}: TradeReportID={TradeReportId}, SecondaryTradeReportID={InternTradeId}",
                    ack.TradeId, ack.TradeReportId, ack.InternTradeId);

                await _fixEngine.SendMessageAsync(ack.SessionKey, arMessage);

                // Uppdatera status i tradesystemlink
                await _ackQueueRepository.UpdateAckStatusAsync(
                    ack.TradeId,
                    AckStatus.Sent,
                    DateTime.UtcNow);

                // Skriv workflow event: FIX_ACK_SENT
                await TryInsertWorkflowEventAsync(
                    ack.TradeId,
                    "FIX_ACK",
                    "FIX_ACK_SENT",
                    $"FIX acknowledgment sent\nTradeReportID: {ack.TradeReportId}\nMX3 trade ID: {ack.InternTradeId}");

                _logger.LogInformation("ACK sent successfully for Trade {TradeId}", ack.TradeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send ACK for Trade {TradeId}", ack.TradeId);

                // Uppdatera status till Failed
                await _ackQueueRepository.UpdateAckStatusAsync(
                    ack.TradeId,
                    AckStatus.Failed,
                    null);

                // Skriv workflow event: FIX_ACK_ERROR
                await TryInsertWorkflowEventAsync(
                    ack.TradeId,
                    "FIX_ACK",
                    "FIX_ACK_ERROR",
                    $"FIX acknowledgment failed\nError: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to insert a workflow event. Logs a warning if it fails but does not throw.
        /// This ensures ACK processing continues even if audit trail logging fails.
        /// </summary>
        private async Task TryInsertWorkflowEventAsync(long tradeId, string systemCode, string eventType, string? details)
        {
            try
            {
                await _ackQueueRepository.InsertWorkflowEventAsync(tradeId, systemCode, eventType, details);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to insert workflow event for Trade {TradeId}. Event: {EventType}, SystemCode: {SystemCode}. " +
                    "ACK was processed but audit trail may be incomplete.",
                    tradeId, eventType, systemCode);
            }
        }
    }
}
