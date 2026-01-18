using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FxFixGateway.Domain.Events;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using FxFixGateway.Domain.Enums;

namespace FxFixGateway.Infrastructure
{
    public class MockFixEngine : IFixEngine
    {
        public event EventHandler<SessionStatusChangedEvent>? StatusChanged;
        public event EventHandler<MessageReceivedEvent>? MessageReceived;
        public event EventHandler<MessageSentEvent>? MessageSent;
        public event EventHandler<HeartbeatReceivedEvent>? HeartbeatReceived;
        public event EventHandler<ErrorOccurredEvent>? ErrorOccurred;

        public Task InitializeAsync(IEnumerable<SessionConfiguration> sessions)
        {
            return Task.CompletedTask;
        }

        public Task StartSessionAsync(string sessionKey)
        {
            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey, SessionStatus.Stopped, SessionStatus.Starting));
            return Task.CompletedTask;
        }

        public Task StopSessionAsync(string sessionKey)
        {
            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey, SessionStatus.LoggedOn, SessionStatus.Disconnecting));
            return Task.CompletedTask;
        }

        public Task RestartSessionAsync(string sessionKey)
        {
            return Task.CompletedTask;
        }

        public Task SendMessageAsync(string sessionKey, string rawFixMessage)
        {
            return Task.CompletedTask;
        }

        public Task ShutdownAsync()
        {
            return Task.CompletedTask;
        }
    }
}
