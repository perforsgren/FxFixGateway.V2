using System;
using System.Threading;
using System.Threading.Tasks;
using FxFixGateway.Domain.Constants;
using FxFixGateway.Domain.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.BackgroundServices
{
    public sealed class PostMarkerAcceptPollingService : BackgroundService
    {
        private readonly IPostMarkerSession _session;
        private readonly IPostMarkerRepository _repository;
        private readonly ILogger<PostMarkerAcceptPollingService> _logger;

        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

        public PostMarkerAcceptPollingService(
            IPostMarkerSession session,
            IPostMarkerRepository repository,
            ILogger<PostMarkerAcceptPollingService> logger)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[PostMarkerAccept] Polling service starting, initial delay {Delay}s", InitialDelay.TotalSeconds);
            await Task.Delay(InitialDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingAcceptsAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PostMarkerAccept] Unhandled error in polling loop");
                }

                await Task.Delay(PollInterval, stoppingToken);
            }

            _logger.LogInformation("[PostMarkerAccept] Polling service stopped");
        }

        private async Task ProcessPendingAcceptsAsync(CancellationToken ct)
        {
            var pending = await _repository.GetPendingAcceptsAsync(100);

            foreach (var item in pending)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    _logger.LogInformation("[PostMarkerAccept] Sending Accept: Trade={TradeId} SeqNo={SeqNo}",
                        item.TradeId, item.SequenceNo);

                    await Task.Run(() => _session.Accept(item.SequenceNo, null), ct);

                    await _repository.UpdateAcceptStatusAsync(item.TradeId, DbPostMarkerStatus.AcceptSent, DateTime.UtcNow);
                    await _repository.InsertWorkflowEventAsync(item.TradeId, "POSTMARKER_ACCEPT", "ACCEPT_SENT",
                        $"Accept sent for SeqNo={item.SequenceNo}");

                    _logger.LogInformation("[PostMarkerAccept] Accept OK: Trade={TradeId}", item.TradeId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PostMarkerAccept] Accept failed: Trade={TradeId} SeqNo={SeqNo}",
                        item.TradeId, item.SequenceNo);
                    try
                    {
                        await _repository.UpdateAcceptStatusAsync(item.TradeId, DbPostMarkerStatus.AcceptError, DateTime.UtcNow);
                        await _repository.InsertWorkflowEventAsync(item.TradeId, "POSTMARKER_ACCEPT", "ACCEPT_ERROR", ex.Message);
                    }
                    catch (Exception dbEx)
                    {
                        _logger.LogError(dbEx, "[PostMarkerAccept] Could not update error status: Trade={TradeId}", item.TradeId);
                    }
                }
            }
        }
    }
}