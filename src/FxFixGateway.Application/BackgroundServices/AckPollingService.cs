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

        // TODO: Aktivera ACK polling när Trades-tabellen är skapad i databasen
        // Sätt till true när tabellen finns och systemet är redo för ACK-hantering
        private const bool ENABLE_ACK_POLLING = false;

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
            // TODO: Ta bort denna check när Trades-tabellen är skapad
            if (!ENABLE_ACK_POLLING)
            {
                _logger.LogWarning("ACK Polling Service is DISABLED. Set ENABLE_ACK_POLLING = true when Trades table exists.");
                return;
            }

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

                var arMessage = BuildAckMessage(ack);

                await _fixEngine.SendMessageAsync(ack.SessionKey, arMessage);

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

                await _ackQueueRepository.UpdateAckStatusAsync(
                    ack.TradeId,
                    AckStatus.Failed,
                    null);
            }
        }

        private string BuildAckMessage(Domain.ValueObjects.PendingAck ack)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.fff");

            var fixMessage = $"8=FIX.4.4|9=XXX|35=AR|" +
                           $"571={ack.TradeReportId}|" +
                           $"939=0|" +
                           $"568=1|" +
                           $"828={ack.InternTradeId}|" +
                           $"52={timestamp}|" +
                           $"10=XXX|";

            return fixMessage;
        }
    }
}
