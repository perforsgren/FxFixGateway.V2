using System;

namespace FxFixGateway.Domain.Events
{
    /// <summary>
    /// Basklass för alla domain events.
    /// </summary>
    public abstract class DomainEventBase
    {
        public Guid EventId { get; }
        public DateTime OccurredAtUtc { get; }
        public string SessionKey { get; }

        protected DomainEventBase(string sessionKey)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
                throw new ArgumentException("SessionKey cannot be null or empty.", nameof(sessionKey));

            EventId = Guid.NewGuid();
            OccurredAtUtc = DateTime.UtcNow;
            SessionKey = sessionKey;
        }
    }
}
