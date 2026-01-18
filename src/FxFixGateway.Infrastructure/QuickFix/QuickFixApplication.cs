using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Events;
using MySqlX.XDevAPI;
using QuickFix;
using System;
using System.Collections.Generic;
using static System.Net.Mime.MediaTypeNames;
using Message = QuickFix.Message;

namespace FxFixGateway.Infrastructure.QuickFix
{
    /// <summary>
    /// QuickFIX IApplication implementation som översätter QuickFIX callbacks
    /// till våra domain events.
    /// </summary>
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
            // Session created - ingen action behövs här
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

        public void ToAdmin(Message message, SessionID sessionId)
        {
            // Admin messages (Logon, Heartbeat, etc.) - kan loggas här
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

        public void ToApp(Message message, SessionID sessionId)
        {
            // Application messages being SENT (AE, AR, etc.)
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

        public void FromAdmin(Message message, SessionID sessionId)
        {
            // Admin messages RECEIVED - kan ignoreras eller loggas
        }

        public void FromApp(Message message, SessionID sessionId)
        {
            // Application messages RECEIVED (AE från venue)
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

        private string GetMessageType(Message message)
        {
            try
            {
                var header = message.Header;
                if (header.IsSetField(QuickFix.Fields.Tags.MsgType))
                {
                    return header.GetString(QuickFix.Fields.Tags.MsgType);
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
