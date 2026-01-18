using System;

namespace FxFixGateway.Domain.Events
{
    /// <summary>
    /// Publiceras när ett FIX-meddelande (ej heartbeat) tas emot.
    /// </summary>
    public sealed class MessageReceivedEvent : DomainEventBase
    {
        public string MsgType { get; }
        public string RawMessage { get; }
        public DateTime ReceivedAtUtc { get; }

        public MessageReceivedEvent(
            string sessionKey,
            string msgType,
            string rawMessage,
            DateTime receivedAtUtc)
            : base(sessionKey)
        {
            MsgType = msgType ?? string.Empty;
            RawMessage = rawMessage ?? string.Empty;
            ReceivedAtUtc = receivedAtUtc;
        }

        public override string ToString()
            => $"Message received for {SessionKey}: {MsgType}";
    }
}
