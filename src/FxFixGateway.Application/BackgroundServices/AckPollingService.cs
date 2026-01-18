using System;
using System.Linq;
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

        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(1);

        public AckPollingService(
            IAckQueueRepository ackQueueRepository,
            IFixEngine fixEngine,
            ILogger<AckPollingService> logger)
        {
            _ackQueueRepository = ackQueueRepository ?? throw new ArgumentNullException(nameof(ackQueueRepository));
            _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                // Ingen pending ACKs, logga inte (för att undvika spam)
                return;
            }

            _logger.LogInformation("Processing {Count} pending ACKs", ackList.Count);

            foreach (var ack in ackList)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await ProcessSingleAckAsync(ack);
            }
        }

        private async Task ProcessSingleAckAsync(Domain.ValueObjects.PendingAck ack)
        {
            try
            {
                _logger.LogDebug("Processing ACK for Trade {TradeId}: {TradeReportId} → {InternTradeId}",
                    ack.TradeId, ack.TradeReportId, ack.InternTradeId);

                // Bygg AR-meddelande (TradeCaptureReportAck)
                var arMessage = BuildAckMessage(ack);

                // Skicka via FixEngine
                await _fixEngine.SendMessageAsync(ack.SessionKey, arMessage);

                // Uppdatera status i DB
                await _ackQueueRepository.UpdateAckStatusAsync(
                    ack.TradeId,
                    AckStatus.Sent,
                    DateTime.UtcNow);

                _logger.LogInformation("ACK sent for Trade {TradeId}: {TradeReportId}",
                    ack.TradeId, ack.TradeReportId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send ACK for Trade {TradeId}", ack.TradeId);

                // Uppdatera status till Failed
                await _ackQueueRepository.UpdateAckStatusAsync(
                    ack.TradeId,
                    AckStatus.Failed,
                    null);
            }
        }

        /// <summary>
        /// Bygger ett AR-meddelande (TradeCaptureReportAck) i FIX-format.
        /// 
        /// TODO: Detta är en förenklad version. Du kan integrera med
        /// QuickFIX message builders senare för mer robust implementation.
        /// </summary>
        private string BuildAckMessage(Domain.ValueObjects.PendingAck ack)
        {
            // Förenklad FIX AR-meddelande
            // I verkligheten skulle du använda QuickFIX message builder

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.fff");

            // FIX 4.4 TradeCaptureReportAck (MsgType=AR)
            var fixMessage = $"8=FIX.4.4|9=XXX|35=AR|" +
                           $"571={ack.TradeReportId}|" +  // TradeReportID
                           $"939=0|" +                     // TrdRptStatus (0=Accepted)
                           $"568=1|" +                     // TradeRequestID
                           $"828={ack.InternTradeId}|" +   // Custom tag för InternTradeId
                           $"52={timestamp}|" +
                           $"10=XXX|";  // Checksum (beräknas senare)

            // OBS: Detta är en stub. Riktiga FIX-meddelanden kräver:
            // - Korrekt längd (tag 9)
            // - Korrekt checksum (tag 10)
            // - SOH-separatorer (inte |)
            // - Sekvens-nummer
            // etc.

            return fixMessage;
        }
    }
}
