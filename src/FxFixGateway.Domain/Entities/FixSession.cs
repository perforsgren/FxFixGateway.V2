using System;
using System.Collections.Generic;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Events;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Domain.Entities
{
    public sealed class FixSession
    {
        private readonly List<DomainEventBase> _domainEvents = new List<DomainEventBase>();

        public SessionConfiguration Configuration { get; private set; }
        public SessionStatus Status { get; private set; }
        public string LastError { get; private set; }

        // Timestamp properties
        public DateTime? LastLogonTime { get; private set; }
        public DateTime? LastLogoutTime { get; private; }
        public DateTime? LastHeartbeatTime { get; private set; }
        public DateTime? LastMessageTime { get; private set; }

        public IReadOnlyCollection<DomainEventBase> DomainEvents => _domainEvents.AsReadOnly();

        public FixSession(SessionConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Status = SessionStatus.Stopped;
        }

        public void Start()
        {
            if (Status != SessionStatus.Stopped && Status != SessionStatus.Error)
            {
                throw new InvalidOperationException($"Cannot start session in {Status} status");
            }

            Status = SessionStatus.Starting;
            LastError = null;
            RaiseDomainEvent(new SessionStatusChangedEvent(Configuration.SessionKey, Status, DateTime.UtcNow));
        }

        public void Stop()
        {
            if (Status == SessionStatus.Stopped)
            {
                throw new InvalidOperationException("Session is already stopped");
            }

            Status = SessionStatus.Disconnecting;
            RaiseDomainEvent(new SessionStatusChangedEvent(Configuration.SessionKey, Status, DateTime.UtcNow));
        }

        public void MarkAsLoggedOn()
        {
            Status = SessionStatus.LoggedOn;
            LastLogonTime = DateTime.UtcNow;
            LastError = null;
            RaiseDomainEvent(new SessionStatusChangedEvent(Configuration.SessionKey, Status, DateTime.UtcNow));
        }

        public void MarkAsDisconnected()
        {
            Status = SessionStatus.Stopped;
            LastLogoutTime = DateTime.UtcNow;
            RaiseDomainEvent(new SessionStatusChangedEvent(Configuration.SessionKey, Status, DateTime.UtcNow));
        }

        public void MarkAsError(string errorMessage)
        {
            Status = SessionStatus.Error;
            LastError = errorMessage;
            RaiseDomainEvent(new SessionStatusChangedEvent(Configuration.SessionKey, Status, DateTime.UtcNow));
            RaiseDomainEvent(new ErrorOccurredEvent(Configuration.SessionKey, errorMessage, DateTime.UtcNow));
        }

        public void RecordHeartbeat()
        {
            LastHeartbeatTime = DateTime.UtcNow;
            RaiseDomainEvent(new HeartbeatReceivedEvent(Configuration.SessionKey, DateTime.UtcNow));
        }

        public void RecordMessageReceived(string messageType, string rawMessage)
        {
            LastMessageTime = DateTime.UtcNow;
            RaiseDomainEvent(new MessageReceivedEvent(
                Configuration.SessionKey,
                messageType,
                rawMessage,
                DateTime.UtcNow));
        }

        public void RecordMessageSent(string messageType, string rawMessage)
        {
            LastMessageTime = DateTime.UtcNow;
            RaiseDomainEvent(new MessageSentEvent(
                Configuration.SessionKey,
                messageType,
                rawMessage,
                DateTime.UtcNow));
        }

        public void UpdateConfiguration(SessionConfiguration newConfiguration)
        {
            if (newConfiguration == null)
            {
                throw new ArgumentNullException(nameof(newConfiguration));
            }

            if (newConfiguration.SessionKey != Configuration.SessionKey)
            {
                throw new InvalidOperationException("Cannot change SessionKey");
            }

            Configuration = newConfiguration;
            RaiseDomainEvent(new ConfigurationUpdatedEvent(Configuration.SessionKey, DateTime.UtcNow));
        }

        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }

        private void RaiseDomainEvent(DomainEventBase domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }
    }
}
