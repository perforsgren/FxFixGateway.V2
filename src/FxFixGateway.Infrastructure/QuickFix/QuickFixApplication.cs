using System;
using System.Collections.Generic;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Events;
using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Services;
using FxTradeHub.Domain.Parsing;
using QF = global::QuickFix;
using System.Security.Cryptography;
using System.Text;

namespace FxFixGateway.Infrastructure.QuickFix
{
    public class QuickFixApplication : QF.IApplication
    {
        private readonly Dictionary<QF.SessionID, string> _sessionKeyMap;
        private readonly Dictionary<string, (string Username, string Password)> _sessionCredentials;
        private readonly IMessageInService? _messageInService;
        private readonly IMessageInParserOrchestrator? _orchestrator;

        public event EventHandler<SessionStatusChangedEvent>? StatusChanged;
        public event EventHandler<MessageReceivedEvent>? MessageReceived;
        public event EventHandler<MessageSentEvent>? MessageSent;
        public event EventHandler<HeartbeatReceivedEvent>? HeartbeatReceived;
        public event EventHandler<ErrorOccurredEvent>? ErrorOccurred;

        public QuickFixApplication(
            Dictionary<QF.SessionID, string> sessionKeyMap,
            Dictionary<string, (string Username, string Password)> sessionCredentials,
            IMessageInService? messageInService,
            IMessageInParserOrchestrator? orchestrator)
        {
            _sessionKeyMap = sessionKeyMap ?? throw new ArgumentNullException(nameof(sessionKeyMap));
            _sessionCredentials = sessionCredentials ?? new Dictionary<string, (string, string)>();
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

            // Lägg till credentials i Logon-meddelandet
            if (msgType == "A") // Logon
            {
                if (_sessionCredentials.TryGetValue(sessionKey, out var credentials))
                {
                    if (!string.IsNullOrEmpty(credentials.Username))
                    {
                        message.SetField(new QF.Fields.Username(credentials.Username));  // tag 553
                    }
                    if (!string.IsNullOrEmpty(credentials.Password))
                    {
                        message.SetField(new QF.Fields.Password(credentials.Password));  // tag 554
                    }
                    
                    // Sätt ResetSeqNumFlag=Y för att undvika sekvensfel
                    if (!message.IsSetField(QF.Fields.Tags.ResetSeqNumFlag))
                    {
                        message.SetField(new QF.Fields.ResetSeqNumFlag(true));  // tag 141=Y
                    }
                }
            }

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

            // Kolla om sessionen faktiskt är inloggad
            var session = QF.Session.LookupSession(sessionId);
            var isLoggedOn = session?.IsLoggedOn ?? false;
            var seqNum = message.Header.IsSetField(QF.Fields.Tags.MsgSeqNum) 
                ? message.Header.GetInt(QF.Fields.Tags.MsgSeqNum) 
                : -1;

            // Logga med mer detaljer
            System.Diagnostics.Debug.WriteLine(
                $"[ToApp] {sessionKey} MsgType={msgType} SeqNum={seqNum} IsLoggedOn={isLoggedOn}");

            if (!isLoggedOn)
            {
                // VARNING: Meddelandet kommer köas, inte skickas direkt!
                ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(
                    sessionKey,
                    $"WARNING: Sending {msgType} while session is NOT logged on - message will be queued"));
            }

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
                    var venueCode = GetVenueCode(sessionKey);

                    var entity = new MessageIn
                    {
                        SourceType = "FIX",
                        SourceVenueCode = venueCode,
                        SessionKey = sessionKey,
                        RawPayload = rawMessage,
                        RawPayloadHash = ComputeSHA256Hash(rawMessage),
                        FixMsgType = msgType,
                        ReceivedUtc = DateTime.UtcNow
                    };

                    // Venue-specific enrichment (minimal for now)
                    if (venueCode == "VOLBROKER")
                    {
                        EnrichVolbrokerAE(entity, message);
                    }
                    else if (venueCode == "FENICS")
                    {
                        EnrichFenicsAE(entity, message);
                    }

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
                "FENICS_STP_STAGE2" => "FENICS",
                _ => sessionKey // fallback to SessionKey
            };
        }


        private string ComputeSHA256Hash(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return string.Empty;

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(payload);
                var hashBytes = sha.ComputeHash(bytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        private void EnrichVolbrokerAE(MessageIn entity, QF.Message message)
        {
            // KRITISKT: SourceMessageKey = 571 (TradeReportID) - detta är vad vi ska eka i AR
            entity.SourceMessageKey = TryGetField(message, 571);
            
            // ExternalTradeKey = 818 (SecondaryTradeReportID) - Volbrokers ID
            entity.ExternalTradeKey = TryGetField(message, 818);

            // Enrichment fields
            entity.InstrumentCode = TryGetField(message, 55); // Symbol
        }

        private void EnrichFenicsAE(MessageIn entity, QF.Message message)
        {
            // KRITISKT för ACK: SourceMessageKey måste fyllas i
            // TODO: Fenics använder troligen samma tags som Volbroker, men verifiera när du får riktiga AE
            entity.SourceMessageKey = TryGetField(message, 818) ?? TryGetField(message, 571);
            entity.ExternalTradeKey = TryGetField(message, 818);

            // TODO: Enrichment för debugging (justera efter Fenics faktiska tags)
            entity.InstrumentCode = TryGetField(message, 55); // Symbol
        }



        private string? TryGetField(QF.Message message, int tag)
        {
            try
            {
                return message.IsSetField(tag) ? message.GetString(tag) : null;
            }
            catch
            {
                return null;
            }
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
