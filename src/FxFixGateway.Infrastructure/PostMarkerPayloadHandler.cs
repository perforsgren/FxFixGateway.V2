using System;
using FxFixGateway.Domain.Interfaces;
using FxTradeHub.Domain.Parsing;
using FxTradeHub.Domain.Services;
using FxTradeHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Infrastructure.PostMarker
{
    /// <summary>
    /// Called by SdkListenerAdapter in PostMarkerSession when payload arrives.
    /// Parses, persists, enqueues transport ACK.
    /// </summary>
    public sealed class PostMarkerPayloadHandler : IPostMarkerPayloadHandler
    {
        private readonly PostMarkerAckChannel _ackChannel;
        private readonly IMessageInService _messageInService;
        private readonly IMessageInParserOrchestrator _orchestrator;
        private readonly ILogger<PostMarkerPayloadHandler> _logger;

        public PostMarkerPayloadHandler(
            PostMarkerAckChannel ackChannel,
            IMessageInService messageInService,
            IMessageInParserOrchestrator orchestrator,
            ILogger<PostMarkerPayloadHandler> logger)
        {
            _ackChannel = ackChannel ?? throw new ArgumentNullException(nameof(ackChannel));
            _messageInService = messageInService ?? throw new ArgumentNullException(nameof(messageInService));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void OnPayloadReceived(int sequenceNumber, string status, string xml)
        {
            _logger.LogInformation("[PostMarker] Payload: SeqNo={SeqNo} Status={Status}", sequenceNumber, status);

            if (status == "RECEIVED")
            {
                try
                {
                    var message = new MessageIn
                    {
                        SourceType = "FILE",
                        SourceVenueCode = "FXOHUB",
                        SourceMessageKey = sequenceNumber.ToString(),
                        ReceivedUtc = DateTime.UtcNow,
                        SourceTimestamp = DateTime.UtcNow,
                        RawPayload = xml,
                        IsAdmin = false,
                        ParsedFlag = false
                    };

                    long messageInId = _messageInService.InsertMessage(message);
                    _orchestrator.ProcessMessage(messageInId);

                    _logger.LogInformation("[PostMarker] Parsed OK: SeqNo={SeqNo} MessageInId={Id}", sequenceNumber, messageInId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PostMarker] Parse/persist failed for SeqNo={SeqNo}", sequenceNumber);
                }
                finally
                {
                    EnqueueAck(sequenceNumber);
                }
            }
            else
            {
                _logger.LogInformation("[PostMarker] Status={Status} SeqNo={SeqNo} — ACK only", status, sequenceNumber);
                EnqueueAck(sequenceNumber);
            }
        }

        private void EnqueueAck(int sequenceNumber)
        {
            if (!_ackChannel.Writer.TryWrite(new AckItem(sequenceNumber)))
            {
                _logger.LogWarning("[PostMarker] ACK channel full, blocking on SeqNo={SeqNo}", sequenceNumber);
                _ackChannel.Writer.WriteAsync(new AckItem(sequenceNumber)).AsTask().GetAwaiter().GetResult();
            }
        }
    }
}