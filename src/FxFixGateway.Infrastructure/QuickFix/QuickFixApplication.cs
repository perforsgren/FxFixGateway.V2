using System;
using System.Collections.Generic;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Events;
using QuickFix;

namespace FxFixGateway.Infrastructure.QuickFix
{
    public class QuickFixApplication : IApplication
    {
        private readonly Dictionary<SessionID, string> _sessionKeyMap;

        public event EventHandler<SessionStatusChangedEvent>? StatusChanged;
        public event EventHandler<MessageReceivedEvent>? MessageReceived;
        public event EventHandler<MessageSentEvent>? MessageSent;
        public event EventHandler<HeartbeatReceivedEvent>? HeartbeatReceived;
        public event EventHandler<ErrorOccurredEvent>? ErrorOccurred;

        public QuickFixApplication(Dictionary<SessionID, string> sessionKeyMap)
        {
            _sessionKeyMap = sessionKeyMap ?? throw new ArgumentNullException(nameof(sessionKeyMap));
        }

        public void OnCreate(SessionID sessionId)
        {
            // Session created
        }

        public void OnLogon(SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                SessionStatus.Connecting,
                SessionStatus.LoggedOn));
        }

        public void OnLogout(SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                SessionStatus.LoggedOn,
                SessionStatus.Disconnecting));
        }

        public void ToAdmin(QuickFix.Message message, SessionID sessionId)  // ← FIX: QuickFix.Message
        {
            var msgType = GetMessageType(message);

            if (msgType == "0") // Heartbeat
            {
                var sessionKey = GetSessionKey(sessionId);
                if (sessionKey != null)
                {
                    HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEvent(
                        sessionKey,
                        DateTime.UtcNow));
                }
            }
        }

        public void ToApp(QuickFix.Message message, SessionID sessionId)  // ← FIX: QuickFix.Message
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

        public void FromAdmin(QuickFix.Message message, SessionID sessionId)  // ← FIX: QuickFix.Message
        {
            // Admin messages received
        }

        public void FromApp(QuickFix.Message message, SessionID sessionId)  // ← FIX: QuickFix.Message
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            var msgType = GetMessageType(message);
            var rawMessage = message.ToString();

            MessageReceived?.Invoke(this, new MessageReceivedEvent(
                sessionKey,
                msgType,
                rawMessage,
                DateTime.UtcNow));
        }

        private string? GetSessionKey(SessionID sessionId)
        {
            return _sessionKeyMap.TryGetValue(sessionId, out var key) ? key : null;
        }

        private string GetMessageType(QuickFix.Message message)  // ← FIX: QuickFix.Message
        {
            try
            {
                var header = message.Header;
                if (header.IsSetField(QuickFix.Fields.Tags.MsgType))  // ← FIX: QuickFix.Fields.Tags
                {
                    return header.GetString(QuickFix.Fields.Tags.MsgType);  // ← FIX: QuickFix.Fields.Tags
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return "UNKNOWN";
        }
    }
}
