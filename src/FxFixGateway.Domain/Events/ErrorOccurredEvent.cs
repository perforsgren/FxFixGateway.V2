using System;

namespace FxFixGateway.Domain.Events
{
    /// <summary>
    /// Publiceras när ett fel uppstår i sessionen.
    /// </summary>
    public sealed class ErrorOccurredEvent : DomainEventBase
    {
        public string ErrorMessage { get; }
        public Exception? Exception { get; }

        public ErrorOccurredEvent(
            string sessionKey,
            string errorMessage,
            Exception? exception = null)
            : base(sessionKey)
        {
            ErrorMessage = errorMessage ?? "Unknown error";
            Exception = exception;
        }

        public override string ToString()
            => $"Error in {SessionKey}: {ErrorMessage}";
    }
}
