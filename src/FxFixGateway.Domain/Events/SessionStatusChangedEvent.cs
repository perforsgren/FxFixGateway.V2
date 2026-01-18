using FxFixGateway.Domain.Enums;

namespace FxFixGateway.Domain.Events
{
    /// <summary>
    /// Publiceras när en sessions status ändras.
    /// </summary>
    public sealed class SessionStatusChangedEvent : DomainEventBase
    {
        public SessionStatus OldStatus { get; }
        public SessionStatus NewStatus { get; }

        public SessionStatusChangedEvent(
            string sessionKey,
            SessionStatus oldStatus,
            SessionStatus newStatus)
            : base(sessionKey)
        {
            OldStatus = oldStatus;
            NewStatus = newStatus;
        }

        public override string ToString()
            => $"Session {SessionKey}: {OldStatus} → {NewStatus}";
    }
}
