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
    public sealed class AckPollingService : BackgroundService
    {
        private readonly IAckQueueRepository _ackQueueRepository;
        private readonly IFixEngine _fixEngine;
        private readonly ILogger<AckPollingService> _logger;

        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);
        private readonly HashSet<string> _loggedOnSessions = new();

        public AckPollingService(
            IAckQueueRepository ackQueueRepository,
            IFixEngine fixEngine,
            ILogger<AckPollingService> logger)
        {
            _ackQueueRepository = ackQueueRepository ?? throw new ArgumentNullException(nameof(ackQueueRepository));
            _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
                lock (_loggedOnSessions) { _loggedOnSessions.Add(e.SessionKey); }
                _logger.LogInformation("Session {SessionKey} is now LoggedOn - ACK sending enabled", e.SessionKey);
            }
            else if (e.NewStatus is SessionStatus.Stopped or SessionStatus.Disconnecting or SessionStatus.Error)
            {
                lock (_loggedOnSessions) { _loggedOnSessions.Remove(e.SessionKey); }
                _logger.LogInformation("Session {SessionKey} is no longer LoggedOn - ACK sending disabled", e.SessionKey);
            }
        }

        private bool IsSessionLoggedOn(string sessionKey)
        {
            lock (_loggedOnSessions) { return _loggedOnSessions.Contains(sessionKey); }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ACK Polling Service started");

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessBatchAsync(await _ackQueueRepository.GetPendingAcksAsync(maxCount: 100), stoppingToken);
                    await ProcessBatchAsync(await _ackQueueRepository.GetRejectedAcksAsync(maxCount: 100), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing ACK queue");
                }

                await Task.Delay(_pollingInterval, stoppingToken);
            }

            _logger.LogInformation("ACK Polling Service stopped");
        }

        private async Task ProcessBatchAsync(
            IEnumerable<Domain.ValueObjects.PendingAck> acks,
            CancellationToken cancellationToken)
        {
            var ackList = acks.ToList();
            if (ackList.Count == 0) return;

            foreach (var sessionGroup in ackList.GroupBy(a => a.SessionKey))
            {
                var sessionKey = sessionGroup.Key;

                if (!IsSessionLoggedOn(sessionKey))
                {
                    _logger.LogDebug("Skipping {Count} ACKs for session {SessionKey} - not logged on",
                        sessionGroup.Count(), sessionKey);
                    continue;
                }

                _logger.LogInformation("Processing {Count} {Type} ACKs for session {SessionKey}",
                    sessionGroup.Count(),
                    sessionGroup.First().IsReject ? "REJECT" : "ACCEPT",
                    sessionKey);

                foreach (var ack in sessionGroup)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    await ProcessSingleAckAsync(ack);
                }
            }
        }

        private async Task ProcessSingleAckAsync(Domain.ValueObjects.PendingAck ack)
        {
            if (!IsSessionLoggedOn(ack.SessionKey))
            {
                _logger.LogWarning("Session {SessionKey} logged off before ACK could be sent for Trade {TradeId}",
                    ack.SessionKey, ack.TradeId);
                return;
            }

            if (!ack.IsReject && string.IsNullOrEmpty(ack.InternTradeId))
            {
                _logger.LogWarning(
                    "Skipping ACK for Trade {TradeId} - AckInternalTradeId is null/empty. " +
                    "Trade may not have been processed by downstream system yet. SessionKey={SessionKey}, TradeReportId={TradeReportId}",
                    ack.TradeId, ack.SessionKey, ack.TradeReportId);
                return;
            }

            try
            {
                string arMessage;

                if (ack.IsReject)
                {
                    _logger.LogInformation(
                        "Sending AR Reject for Trade {TradeId}: TradeReportId={TradeReportId}, ExternalTradeKey={ExternalTradeKey}, Reason={RejectReason}",
                        ack.TradeId, ack.TradeReportId, ack.ExternalTradeKey, ack.RejectReason);

                    arMessage = FixMessageBuilder.BuildTradeCaptureReportAckReject(
                        tradeReportId: ack.TradeReportId,
                        externalTradeKey: ack.ExternalTradeKey,
                        rejectReason: ack.RejectReason);
                }
                else
                {
                    _logger.LogInformation(
                        "Sending AR Accept for Trade {TradeId}: TradeReportId={TradeReportId}, InternTradeId={InternTradeId}",
                        ack.TradeId, ack.TradeReportId, ack.InternTradeId);

                    arMessage = FixMessageBuilder.BuildTradeCaptureReportAck(
                        tradeReportId: ack.TradeReportId,
                        internTradeId: ack.InternTradeId);
                }

                await _fixEngine.SendMessageAsync(ack.SessionKey, arMessage);

                // Rejected trades get AckStatus.Rejected → ACK_REJECT_SENT in DB
                // Accepted trades get AckStatus.Sent → ACK_SENT in DB
                var finalStatus = ack.IsReject ? AckStatus.Rejected : AckStatus.Sent;
                await _ackQueueRepository.UpdateAckStatusAsync(ack.TradeId, finalStatus, DateTime.UtcNow);

                var eventType = ack.IsReject ? "FIX_ACK_REJECT_SENT" : "FIX_ACK_SENT";
                var details = ack.IsReject
                    ? $"FIX reject acknowledgment sent\nTradeReportID: {ack.TradeReportId}\nVolbroker ID (881): {ack.ExternalTradeKey}\nReason: {ack.RejectReason}"
                    : $"FIX acknowledgment sent\nTradeReportID: {ack.TradeReportId}\nIntern trade ID: {ack.InternTradeId}";

                await TryInsertWorkflowEventAsync(ack.TradeId, "FIX_ACK", eventType, details);

                _logger.LogInformation("AR {Type} sent successfully for Trade {TradeId}",
                    ack.IsReject ? "REJECT" : "ACCEPT", ack.TradeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send ACK for Trade {TradeId}", ack.TradeId);

                await _ackQueueRepository.UpdateAckStatusAsync(ack.TradeId, AckStatus.Failed, null);

                await TryInsertWorkflowEventAsync(ack.TradeId, "FIX_ACK", "FIX_ACK_ERROR",
                    $"FIX acknowledgment failed\nError: {ex.Message}");
            }
        }

        private async Task TryInsertWorkflowEventAsync(long tradeId, string systemCode, string eventType, string? details)
        {
            try
            {
                await _ackQueueRepository.InsertWorkflowEventAsync(tradeId, systemCode, eventType, details);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to insert workflow event for Trade {TradeId}. Event: {EventType}. " +
                    "ACK was processed but audit trail may be incomplete.",
                    tradeId, eventType);
            }
        }
    }
}