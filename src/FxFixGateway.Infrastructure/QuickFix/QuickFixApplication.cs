using System;
using System.Collections.Generic;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Events;
using QF = global::QuickFix;

namespace FxFixGateway.Infrastructure.QuickFix
{
    public class QuickFixApplication : QF.IApplication
    {
        private readonly Dictionary<QF.SessionID, string> _sessionKeyMap;

        public event EventHandler<SessionStatusChangedEvent>? StatusChanged;
        public event EventHandler<MessageReceivedEvent>? MessageReceived;
        public event EventHandler<MessageSentEvent>? MessageSent;
        public event EventHandler<HeartbeatReceivedEvent>? HeartbeatReceived;
        public event EventHandler<ErrorOccurredEvent>? ErrorOccurred;

        public QuickFixApplication(Dictionary<QF.SessionID, string> sessionKeyMap)
        {
            _sessionKeyMap = sessionKeyMap ?? throw new ArgumentNullException(nameof(sessionKeyMap));
        }

        public void OnCreate(QF.SessionID sessionId)
        {
            // Session created
        }

        public void OnLogon(QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                SessionStatus.Connecting,
                SessionStatus.LoggedOn));
        }

        public void OnLogout(QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                SessionStatus.LoggedOn,
                SessionStatus.Stopped));
        }

        /// <summary>
        /// Called when sending admin messages (Logon, Logout, Heartbeat, etc.)
        /// </summary>
        public void ToAdmin(QF.Message message, QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            var msgType = GetMessageType(message);
            var rawMessage = message.ToString();

            // Log ALL outgoing admin messages (Logon=A, Logout=5, Heartbeat=0, etc.)
            MessageSent?.Invoke(this, new MessageSentEvent(
                sessionKey,
                msgType,
                rawMessage,
                DateTime.UtcNow));

            // Also fire heartbeat event for UI updates
            if (msgType == "0")
            {
                HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEvent(
                    sessionKey,
                    DateTime.UtcNow));
            }
        }

        /// <summary>
        /// Called when sending application messages (AE, AR, etc.)
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
        /// Called when receiving admin messages (Logon, Logout, Heartbeat, etc.)
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

            // Also fire heartbeat event for UI updates
            if (msgType == "0")
            {
                HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEvent(
                    sessionKey,
                    DateTime.UtcNow));
            }
        }

        /// <summary>
        /// Called when receiving application messages (AE, AR, etc.)
        /// </summary>
        public void FromApp(QF.Message message, QF.SessionID sessionId)
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
    }
}
