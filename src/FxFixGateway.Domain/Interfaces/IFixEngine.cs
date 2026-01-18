using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FxFixGateway.Domain.Events;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Kontrakt för FIX-motorn (QuickFIX wrapper).
    /// Implementeras av Infrastructure-lagret.
    /// </summary>
    public interface IFixEngine
    {
        /// <summary>
        /// Initierar FIX-motorn med alla konfigurerade sessions.
        /// Anropas en gång vid startup.
        /// </summary>
        Task InitializeAsync(IEnumerable<SessionConfiguration> sessions);

        /// <summary>
        /// Startar en specifik session.
        /// </summary>
        Task StartSessionAsync(string sessionKey);

        /// <summary>
        /// Stoppar en specifik session.
        /// </summary>
        Task StopSessionAsync(string sessionKey);

        /// <summary>
        /// Startar om en session (stop + start).
        /// </summary>
        Task RestartSessionAsync(string sessionKey);

        /// <summary>
        /// Skickar ett FIX-meddelande till venue.
        /// </summary>
        Task SendMessageAsync(string sessionKey, string rawFixMessage);

        /// <summary>
        /// Stänger ner hela FIX-motorn graciöst.
        /// </summary>
        Task ShutdownAsync();

        // Events som infrastructure-lagret publicerar
        event EventHandler<SessionStatusChangedEvent>? StatusChanged;
        event EventHandler<MessageReceivedEvent>? MessageReceived;
        event EventHandler<MessageSentEvent>? MessageSent;
        event EventHandler<HeartbeatReceivedEvent>? HeartbeatReceived;
        event EventHandler<ErrorOccurredEvent>? ErrorOccurred;
    }
}
