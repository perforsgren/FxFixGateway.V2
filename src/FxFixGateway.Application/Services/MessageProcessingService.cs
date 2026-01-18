using System;
using System.Threading.Tasks;
using FxFixGateway.Domain.Events;
using FxFixGateway.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.Services
{
    /// <summary>
    /// Hanterar processning av inkommande FIX-meddelanden.
    /// Normaliserar AE-meddelanden till trades med hjälp av FxTradeHub.
    /// </summary>
    public sealed class MessageProcessingService
    {
        private readonly IMessageLogger _messageLogger;
        private readonly ILogger<MessageProcessingService> _logger;

        // TODO: Injicera FxTradeHub när det finns tillgängligt
        // private readonly ITradeNormalizer _tradeNormalizer;

        public MessageProcessingService(
            IMessageLogger messageLogger,
            ILogger<MessageProcessingService> logger)
        {
            _messageLogger = messageLogger ?? throw new ArgumentNullException(nameof(messageLogger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Registrerar event handlers mot FixEngine.
        /// </summary>
        public void RegisterEventHandlers(IFixEngine fixEngine)
        {
            if (fixEngine == null)
                throw new ArgumentNullException(nameof(fixEngine));

            fixEngine.MessageReceived += OnMessageReceived;
            fixEngine.MessageSent += OnMessageSent;
        }

        /// <summary>
        /// Event handler för inkommande meddelanden.
        /// </summary>
        private async void OnMessageReceived(object? sender, MessageReceivedEvent e)
        {
            try
            {
                _logger.LogInformation("Received {MsgType} message for session {SessionKey}",
                    e.MsgType, e.SessionKey);

                // 1. Logga meddelandet till DB
                await _messageLogger.LogIncomingAsync(e.SessionKey, e.MsgType, e.RawMessage);

                // 2. Om det är ett AE-meddelande (TradeCaptureReport) → normalisera
                if (e.MsgType == "AE")
                {
                    await ProcessTradeCaptureReportAsync(e.SessionKey, e.RawMessage);
                }

                // 3. Andra meddelandetyper kan hanteras här
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process incoming message for session {SessionKey}", e.SessionKey);
            }
        }

        /// <summary>
        /// Event handler för utgående meddelanden.
        /// </summary>
        private async void OnMessageSent(object? sender, MessageSentEvent e)
        {
            try
            {
                _logger.LogInformation("Sent {MsgType} message for session {SessionKey}",
                    e.MsgType, e.SessionKey);

                // Logga utgående meddelande till DB
                await _messageLogger.LogOutgoingAsync(e.SessionKey, e.MsgType, e.RawMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log outgoing message for session {SessionKey}", e.SessionKey);
            }
        }

        /// <summary>
        /// Normaliserar ett AE-meddelande till en trade.
        /// </summary>
        private async Task ProcessTradeCaptureReportAsync(string sessionKey, string rawMessage)
        {
            _logger.LogDebug("Normalizing AE message for session {SessionKey}", sessionKey);

            // TODO: Integrera med FxTradeHub här
            // var trade = _tradeNormalizer.ParseAE(rawMessage);
            // await _tradeRepository.SaveAsync(trade);

            _logger.LogDebug("AE message normalized and saved to database");

            await Task.CompletedTask;
        }
    }
}
