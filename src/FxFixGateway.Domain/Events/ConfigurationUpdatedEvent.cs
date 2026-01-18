using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Domain.Events
{
    /// <summary>
    /// Publiceras när en sessions konfiguration uppdateras.
    /// </summary>
    public sealed class ConfigurationUpdatedEvent : DomainEventBase
    {
        public SessionConfiguration OldConfiguration { get; }
        public SessionConfiguration NewConfiguration { get; }

        public ConfigurationUpdatedEvent(
            string sessionKey,
            SessionConfiguration oldConfiguration,
            SessionConfiguration newConfiguration)
            : base(sessionKey)
        {
            OldConfiguration = oldConfiguration;
            NewConfiguration = newConfiguration;
        }

        public override string ToString()
            => $"Configuration updated for {SessionKey}";
    }
}
