using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Interfaces;
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
        private const char SOH = '\x01';

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

                // CRITICAL: Only send ACKs if session is logged on
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

            try
            {
                _logger.LogDebug("Processing ACK for Trade {TradeId}: {TradeReportId} → {InternTradeId}",
                    ack.TradeId, ack.TradeReportId, ack.InternTradeId);

                var arMessage = BuildTradeCaptureReportAck(ack);

                _logger.LogInformation("Sending AR for Trade {TradeId}: TradeReportID={TradeReportId}, SecondaryTradeReportID={InternTradeId}",
                    ack.TradeId, ack.TradeReportId, ack.InternTradeId);

                await _fixEngine.SendMessageAsync(ack.SessionKey, arMessage);

                // Uppdatera status i tradesystemlink
                await _ackQueueRepository.UpdateAckStatusAsync(
                    ack.TradeId,
                    AckStatus.Sent,
                    DateTime.UtcNow);

                // Skriv workflow event: FIX_ACK_SENT
                await _ackQueueRepository.InsertWorkflowEventAsync(
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
                await _ackQueueRepository.InsertWorkflowEventAsync(
                    ack.TradeId,
                    "FIX_ACK",
                    "FIX_ACK_ERROR",
                    $"FIX acknowledgment failed\nError: {ex.Message}");
            }
        }

        /// <summary>
        /// Bygger ett korrekt FIX 4.4 TradeCaptureReportAck (AR) meddelande.
        /// </summary>
        private string BuildTradeCaptureReportAck(Domain.ValueObjects.PendingAck ack)
        {
            // Bygg body först (allt mellan tag 9 och tag 10)
            var body = new StringBuilder();

            // 35 = MsgType (AR = TradeCaptureReportAck)
            body.Append($"35=AR{SOH}");

            // 571 = TradeReportID (från ursprungliga AE)
            body.Append($"571={ack.TradeReportId}{SOH}");

            // 939 = TrdRptStatus (0 = Accepted)
            body.Append($"939=0{SOH}");

            // 818 = SecondaryTradeReportID (vårt interna trade ID)
            if (!string.IsNullOrEmpty(ack.InternTradeId))
            {
                body.Append($"818={ack.InternTradeId}{SOH}");
            }

            var bodyStr = body.ToString();
            var bodyLength = bodyStr.Length;

            // Bygg header
            var header = $"8=FIX.4.4{SOH}9={bodyLength}{SOH}";

            // Beräkna checksum (summa av alla bytes i header + body, modulo 256)
            var messageWithoutChecksum = header + bodyStr;
            var checksum = CalculateChecksum(messageWithoutChecksum);

            // Komplett meddelande
            var fullMessage = $"{messageWithoutChecksum}10={checksum:D3}{SOH}";

            return fullMessage;
        }

        /// <summary>
        /// Beräknar FIX checksum (summa av alla ASCII-värden modulo 256).
        /// </summary>
        private int CalculateChecksum(string message)
        {
            var sum = 0;
            foreach (var c in message)
            {
                sum += (byte)c;
            }
            return sum % 256;
        }
    }
}
