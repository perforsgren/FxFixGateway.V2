using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Infrastructure.QuickFix;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.BackgroundServices
{
    public class AckPollingService : BackgroundService
    {
        private readonly IAckQueueRepository _ackQueueRepository;
        private readonly IFixEngine _fixEngine;
        private readonly TradeCaptureReportAckBuilder _arBuilder;
        private readonly ILogger<AckPollingService> _logger;
        private readonly int _intervalSeconds;
        private readonly int _batchSize;

        public AckPollingService(
            IAckQueueRepository ackQueueRepository,
            IFixEngine fixEngine,
            TradeCaptureReportAckBuilder arBuilder,
            IConfiguration configuration,
            ILogger<AckPollingService> logger)
        {
            _ackQueueRepository = ackQueueRepository ?? throw new ArgumentNullException(nameof(ackQueueRepository));
            _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));
            _arBuilder = arBuilder ?? throw new ArgumentNullException(nameof(arBuilder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Read config from appsettings.json
            _intervalSeconds = configuration.GetValue<int>("AckPolling:IntervalSeconds", 5);
            _batchSize = configuration.GetValue<int>("AckPolling:BatchSize", 100);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AckPollingService started (Interval: {Interval}s, BatchSize: {BatchSize})",
                _intervalSeconds, _batchSize);

            // Wait a bit for FIX engine to initialize
            await Task.Delay(5000, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var pending = (await _ackQueueRepository.GetPendingAcksAsync(_batchSize)).ToList();

                    if (pending.Count > 0)
                    {
                        _logger.LogInformation("Found {Count} pending ACKs to process", pending.Count);

                        foreach (var ack in pending)
                        {
                            try
                            {
                                // Build AR message
                                var ar = _arBuilder.BuildAccept(ack);
                                var rawAr = ar.ToString();

                                // Send via FIX engine
                                await _fixEngine.SendMessageAsync(ack.SessionKey, rawAr);

                                // Update status to ACK_SENT
                                await _ackQueueRepository.UpdateAckStatusAsync(
                                    ack.TradeId,
                                    AckStatus.ACK_SENT,
                                    DateTime.UtcNow);

                                _logger.LogInformation("Sent ACK for trade {TradeId} (TradeReportID: {TradeReportId})",
                                    ack.TradeId, ack.TradeReportId);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to send ACK for trade {TradeId}", ack.TradeId);

                                // Update status to ACK_ERROR
                                await _ackQueueRepository.UpdateAckStatusAsync(
                                    ack.TradeId,
                                    AckStatus.ACK_ERROR,
                                    null);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ACK polling loop");
                }

                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
            }

            _logger.LogInformation("AckPollingService stopped");
        }
    }
}




//        private string BuildAckMessage(Domain.ValueObjects.PendingAck ack)
//{
//    var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.fff");

//    var fixMessage = $"8=FIX.4.4|9=XXX|35=AR|" +
//                   $"571={ack.TradeReportId}|" +
//                   $"939=0|" +
//                   $"568=1|" +
//                   $"828={ack.InternTradeId}|" +
//                   $"52={timestamp}|" +
//                   $"10=XXX|";

//    return fixMessage;
