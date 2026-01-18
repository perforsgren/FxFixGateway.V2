using System;
using System.Threading.Tasks;
using FxFixGateway.Domain.Events;
using FxFixGateway.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.Services
{
    /// <summary>
    /// Hanterar processning av inkommande FIX-meddelanden.
    /// </summary>
    public sealed class MessageProcessingService
    {
        private readonly IFixEngine _fixEngine;
        private readonly IMessageLogger _messageLogger;
        private readonly ILogger<MessageProcessingService> _logger;

        public MessageProcessingService(
            IFixEngine fixEngine,
            IMessageLogger messageLogger,
            ILogger<MessageProcessingService> logger)
        {
            _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));
            _messageLogger = messageLogger ?? throw new ArgumentNullException(nameof(messageLogger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Auto-register event handlers
            _fixEngine.MessageReceived += OnMessageReceived;
            _fixEngine.MessageSent += OnMessageSent;

            _logger.LogInformation("MessageProcessingService initialized and listening for FIX events");
        }

        private async void OnMessageReceived(object? sender, MessageReceivedEvent e)
        {
            try
            {
                _logger.LogDebug("Received {MsgType} message for session {SessionKey}", e.MsgType, e.SessionKey);
                await _messageLogger.LogIncomingAsync(e.SessionKey, e.MsgType, e.RawMessage);

                // Om det är ett AE-meddelande → normalisera (TODO)
                if (e.MsgType == "AE")
                {
                    _logger.LogInformation("Processing TradeCaptureReport for {SessionKey}", e.SessionKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process incoming message for session {SessionKey}", e.SessionKey);
            }
        }

        private async void OnMessageSent(object? sender, MessageSentEvent e)
        {
            try
            {
                _logger.LogDebug("Sent {MsgType} message for session {SessionKey}", e.MsgType, e.SessionKey);
                await _messageLogger.LogOutgoingAsync(e.SessionKey, e.MsgType, e.RawMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log outgoing message for session {SessionKey}", e.SessionKey);
            }
        }
    }
}
