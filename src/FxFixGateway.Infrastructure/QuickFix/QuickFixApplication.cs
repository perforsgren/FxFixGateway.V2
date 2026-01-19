using System;
using System.Collections.Generic;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Events;
using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Services;
using FxTradeHub.Domain.Parsing;
using QF = global::QuickFix;

namespace FxFixGateway.Infrastructure.QuickFix
{
    public class QuickFixApplication : QF.IApplication
    {
        private readonly Dictionary<QF.SessionID, string> _sessionKeyMap;
        private readonly IMessageInService? _messageInService;
        private readonly IMessageInParserOrchestrator? _orchestrator;

        public event EventHandler<SessionStatusChangedEvent>? StatusChanged;
        public event EventHandler<MessageReceivedEvent>? MessageReceived;
        public event EventHandler<MessageSentEvent>? MessageSent;
        public event EventHandler<HeartbeatReceivedEvent>? HeartbeatReceived;
        public event EventHandler<ErrorOccurredEvent>? ErrorOccurred;

        public QuickFixApplication(
            Dictionary<QF.SessionID, string> sessionKeyMap,
            IMessageInService? messageInService,
            IMessageInParserOrchestrator? orchestrator)
        {
            _sessionKeyMap = sessionKeyMap ?? throw new ArgumentNullException(nameof(sessionKeyMap));
            _messageInService = messageInService;
            _orchestrator = orchestrator;
        }

        /// <summary>
        /// Called when a new session is created.
        /// </summary>
        public void OnCreate(QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            // Log session creation as an info message
            MessageReceived?.Invoke(this, new MessageReceivedEvent(
                sessionKey,
                "CREATE",
                $"Session created: {sessionId}",
                DateTime.UtcNow));
        }

        /// <summary>
        /// Called when logon is confirmed (session is fully established).
        /// </summary>
        public void OnLogon(QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            // Log "Logon confirmed" event
            MessageReceived?.Invoke(this, new MessageReceivedEvent(
                sessionKey,
                "LOGON",
                "Logon confirmed - session established",
                DateTime.UtcNow));

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                SessionStatus.Connecting,
                SessionStatus.LoggedOn));
        }

        /// <summary>
        /// Called when logout is received (session is disconnecting).
        /// </summary>
        public void OnLogout(QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            // Log "Logout received" event
            MessageReceived?.Invoke(this, new MessageReceivedEvent(
                sessionKey,
                "LOGOUT",
                "Logout received - session disconnected",
                DateTime.UtcNow));

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                SessionStatus.LoggedOn,
                SessionStatus.Stopped));
        }

        /// <summary>
        /// Called when sending admin messages (Logon, Logout, Heartbeat, TestRequest, etc.)
        /// </summary>
        public void ToAdmin(QF.Message message, QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            var msgType = GetMessageType(message);
            var rawMessage = message.ToString();

            // Log ALL outgoing admin messages
            MessageSent?.Invoke(this, new MessageSentEvent(
                sessionKey,
                msgType,
                rawMessage,
                DateTime.UtcNow));

            // Fire heartbeat event for UI updates
            if (msgType == "0")
            {
                HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEvent(
                    sessionKey,
                    DateTime.UtcNow));
            }
        }

        /// <summary>
        /// Called when sending application messages (TradeCaptureReportAck, etc.)
        /// </summary>
        public void ToApp(QF.Message message, QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            var msgType = GetMessageType(message);
            var rawMessage = message.ToString();

            MessageSent?.Invoke(this, new MessageSentEvent(
                sessionKey,
                msgType,
                rawMessage,
                DateTime.UtcNow));
        }

        /// <summary>
        /// Called when receiving admin messages (Logon, Logout, Heartbeat, TestRequest, Reject, etc.)
        /// </summary>
        public void FromAdmin(QF.Message message, QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            var msgType = GetMessageType(message);
            var rawMessage = message.ToString();

            // Log ALL incoming admin messages
            MessageReceived?.Invoke(this, new MessageReceivedEvent(
                sessionKey,
                msgType,
                rawMessage,
                DateTime.UtcNow));

            // Handle specific admin message types
            switch (msgType)
            {
                case "0": // Heartbeat
                    HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEvent(
                        sessionKey,
                        DateTime.UtcNow));
                    break;

                case "1": // TestRequest - might indicate connectivity issues
                    // Already logged above, no special handling needed
                    break;

                case "2": // ResendRequest - indicates sequence gap
                    ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(
                        sessionKey,
                        $"ResendRequest received - sequence gap detected"));
                    break;

                case "3": // Reject - session level reject
                    var rejectReason = GetRejectReason(message);
                    ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(
                        sessionKey,
                        $"Session Reject received: {rejectReason}"));
                    break;

                case "4": // SequenceReset
                    // Normal during recovery, just log it (already done above)
                    break;

                case "5": // Logout
                    // Logout message received, OnLogout will be called separately
                    break;
            }
        }

        /// <summary>
        /// Called when receiving application messages (TradeCaptureReport, ExecutionReport, etc.)
        /// </summary>
        public void FromApp(QF.Message message, QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            var msgType = GetMessageType(message);
            var rawMessage = message.ToString();

            // 1. Fire event for UI (MessageLog tab)
            MessageReceived?.Invoke(this, new MessageReceivedEvent(
                sessionKey,
                msgType,
                rawMessage,
                DateTime.UtcNow));

            // 2. Insert to trade_stp.messagein + trigger parsing (only AE)
            if (msgType == "AE" && _messageInService != null)
            {
                try
                {
                    var entity = new MessageIn
                    {
                        SourceType = "FIX",
                        SourceVenueCode = GetVenueCode(sessionKey),
                        SessionKey = sessionKey,
                        RawPayload = rawMessage,
                        FixMsgType = msgType,
                        ReceivedUtc = DateTime.UtcNow
                    };

                    var messageInId = _messageInService.InsertMessage(entity);

                    // Trigger parsing immediately
                    if (_orchestrator != null)
                    {
                        _orchestrator.ProcessMessage(messageInId);
                    }
                }
                catch (Exception ex)
                {
                    // Log error but don't crash FIX session
                    ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(
                        sessionKey,
                        $"Failed to process MessageIn: {ex.Message}",
                        ex));
                }
            }
        }

        private string GetVenueCode(string sessionKey)
        {
            return sessionKey switch
            {
                "VOLB_STP_DEV" => "VOLBROKER",
                "VOLB_STP_PROD" => "VOLBROKER",
                _ => sessionKey // fallback to SessionKey
            };
        }


        private string? GetSessionKey(QF.SessionID sessionId)
        {
            return _sessionKeyMap.TryGetValue(sessionId, out var key) ? key : null;
        }

        private string GetMessageType(QF.Message message)
        {
            try
            {
                var header = message.Header;
                if (header.IsSetField(QF.Fields.Tags.MsgType))
                {
                    return header.GetString(QF.Fields.Tags.MsgType);
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return "?";
        }

        private string GetRejectReason(QF.Message message)
        {
            try
            {
                // Tag 58 = Text (reason for reject)
                if (message.IsSetField(58))
                {
                    return message.GetString(58);
                }
                // Tag 373 = SessionRejectReason
                if (message.IsSetField(373))
                {
                    var reasonCode = message.GetInt(373);
                    return $"RejectReason={reasonCode}";
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return "Unknown reason";
        }
    }
}
