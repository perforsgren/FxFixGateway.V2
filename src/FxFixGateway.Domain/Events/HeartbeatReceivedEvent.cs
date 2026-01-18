using System;

namespace FxFixGateway.Domain.Events
{
    /// <summary>
    /// Publiceras när en heartbeat tas emot från venue.
    /// </summary>
    public sealed class HeartbeatReceivedEvent : DomainEventBase
    {
        public DateTime ReceivedAtUtc { get; }

        public HeartbeatReceivedEvent(string sessionKey, DateTime receivedAtUtc)
            : base(sessionKey)
        {
            ReceivedAtUtc = receivedAtUtc;
        }

        public override string ToString()
            => $"Heartbeat received for {SessionKey} at {ReceivedAtUtc:HH:mm:ss}";
    }
}
