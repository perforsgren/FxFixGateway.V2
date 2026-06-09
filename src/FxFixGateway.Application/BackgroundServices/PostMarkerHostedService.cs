using System;
using System.Threading;
using System.Threading.Tasks;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Infrastructure.PostMarker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.BackgroundServices
{
    public sealed class PostMarkerHostedService : IHostedService, IDisposable
    {
        private readonly IPostMarkerSession _session;
        private readonly IPostMarkerPayloadHandler _payloadHandler;
        private readonly PostMarkerAckChannel _ackChannel;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PostMarkerHostedService> _logger;

        private Thread _subscriptionThread;
        private Task _ackConsumerTask;
        private CancellationTokenSource _cts;

        public PostMarkerHostedService(
            IPostMarkerSession session,
            IPostMarkerPayloadHandler payloadHandler,
            PostMarkerAckChannel ackChannel,
            IConfiguration configuration,
            ILogger<PostMarkerHostedService> logger)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _payloadHandler = payloadHandler ?? throw new ArgumentNullException(nameof(payloadHandler));
            _ackChannel = ackChannel ?? throw new ArgumentNullException(nameof(ackChannel));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var username = _configuration["PostMarker:Username"]
                ?? throw new InvalidOperationException("PostMarker:Username not configured");
            var password = _configuration["PostMarker:Password"]
                ?? throw new InvalidOperationException("PostMarker:Password not configured");
            var reconnectSecs = int.Parse(_configuration["PostMarker:ReconnectSeconds"] ?? "30");

            _session.RegisterPayloadHandler(_payloadHandler);
            _session.Connect(username, password, reconnectSecs, autoReconnect: true);
            _logger.LogInformation("[PostMarker] Connected as {User}", username);

            _ackConsumerTask = Task.Run(() => ConsumeAcksAsync(_cts.Token), _cts.Token);

            _subscriptionThread = new Thread(() =>
            {
                _logger.LogInformation("[PostMarker] StartSubscription() begin (blocking)");
                try
                {
                    _session.StartSubscription();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PostMarker] StartSubscription() exited with exception");
                }
                _logger.LogInformation("[PostMarker] StartSubscription() returned");
            })
            {
                IsBackground = true,
                Name = "PostMarker-Subscription"
            };
            _subscriptionThread.Start();

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[PostMarker] Stopping...");

            _ackChannel.Writer.Complete();
            _cts?.Cancel();

            if (_ackConsumerTask != null)
            {
                try { await Task.WhenAny(_ackConsumerTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "[PostMarker] ACK consumer did not drain cleanly"); }
            }

            _session.Disconnect();

            if (_subscriptionThread is { IsAlive: true })
                _subscriptionThread.Join(TimeSpan.FromSeconds(5));

            _logger.LogInformation("[PostMarker] Stopped");
        }

        private async Task ConsumeAcksAsync(CancellationToken ct)
        {
            while (await _ackChannel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_ackChannel.Reader.TryRead(out var item))
                {
                    try
                    {
                        _session.Acknowledge(item.SequenceNumber);
                        _logger.LogDebug("[PostMarker] ACK sent: SeqNo={SeqNo}", item.SequenceNumber);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[PostMarker] ACK failed: SeqNo={SeqNo}", item.SequenceNumber);
                    }
                }
            }
        }

        public void Dispose()
        {
            _cts?.Dispose();
            _session.Dispose();
        }
    }
}