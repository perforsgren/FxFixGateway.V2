using System;

namespace FxFixGateway.Domain.Events
{
    /// <summary>
    /// Publiceras när ett FIX-meddelande skickas till venue.
    /// </summary>
    public sealed class MessageSentEvent : DomainEventBase
    {
        public string MsgType { get; }
        public string RawMessage { get; }
        public DateTime SentAtUtc { get; }

        public MessageSentEvent(
            string sessionKey,
            string msgType,
            string rawMessage,
            DateTime sentAtUtc)
            : base(sessionKey)
        {
            MsgType = msgType ?? string.Empty;
            RawMessage = rawMessage ?? string.Empty;
            SentAtUtc = sentAtUtc;
        }

        public override string ToString()
            => $"Message sent from {SessionKey}: {MsgType}";
    }
}
