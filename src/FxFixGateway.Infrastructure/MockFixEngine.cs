using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FxFixGateway.Domain.Events;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using FxFixGateway.Domain.Enums;

namespace FxFixGateway.Infrastructure
{
    public class MockFixEngine : IFixEngine
    {
        private readonly Dictionary<string, Timer> _heartbeatTimers = new();
        private readonly Dictionary<string, SessionStatus> _sessionStates = new();
        private readonly Random _random = new();

        public event EventHandler<SessionStatusChangedEvent>? StatusChanged;
        public event EventHandler<MessageReceivedEvent>? MessageReceived;
        public event EventHandler<MessageSentEvent>? MessageSent;
        public event EventHandler<HeartbeatReceivedEvent>? HeartbeatReceived;
        public event EventHandler<ErrorOccurredEvent>? ErrorOccurred;

        public Task InitializeAsync(IEnumerable<SessionConfiguration> sessions)
        {
            foreach (var session in sessions)
            {
                _sessionStates[session.SessionKey] = SessionStatus.Stopped;
            }
            return Task.CompletedTask;
        }

        public async Task StartSessionAsync(string sessionKey)
        {
            // Simulera state transitions med delays
            RaiseStatusChanged(sessionKey, SessionStatus.Stopped, SessionStatus.Starting);
            await Task.Delay(500);

            RaiseStatusChanged(sessionKey, SessionStatus.Starting, SessionStatus.Connecting);
            await Task.Delay(1000);

            // Simulera 10% chans till connection error
            if (_random.Next(100) < 10)
            {
                RaiseStatusChanged(sessionKey, SessionStatus.Connecting, SessionStatus.Error);
                ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(sessionKey, "Connection refused (simulated)"));
                return;
            }

            RaiseStatusChanged(sessionKey, SessionStatus.Connecting, SessionStatus.LoggedOn);

            // Starta heartbeat timer (30 sekunder intervall)
            StartHeartbeatTimer(sessionKey);
        }

        public async Task StopSessionAsync(string sessionKey)
        {
            StopHeartbeatTimer(sessionKey);

            var currentStatus = _sessionStates.TryGetValue(sessionKey, out var status) ? status : SessionStatus.LoggedOn;

            RaiseStatusChanged(sessionKey, currentStatus, SessionStatus.Disconnecting);
            await Task.Delay(300);

            RaiseStatusChanged(sessionKey, SessionStatus.Disconnecting, SessionStatus.Stopped);
        }

        public async Task RestartSessionAsync(string sessionKey)
        {
            await StopSessionAsync(sessionKey);
            await Task.Delay(500);
            await StartSessionAsync(sessionKey);
        }

        public Task SendMessageAsync(string sessionKey, string rawFixMessage)
        {
            // Simulera att meddelande skickas
            MessageSent?.Invoke(this, new MessageSentEvent(
                sessionKey,
                "AR",  // TradeCaptureReportAck
                rawFixMessage,
                DateTime.UtcNow));

            return Task.CompletedTask;
        }

        public Task ShutdownAsync()
        {
            // Stoppa alla heartbeat timers
            foreach (var timer in _heartbeatTimers.Values)
            {
                timer.Dispose();
            }
            _heartbeatTimers.Clear();

            return Task.CompletedTask;
        }

        private void RaiseStatusChanged(string sessionKey, SessionStatus oldStatus, SessionStatus newStatus)
        {
            _sessionStates[sessionKey] = newStatus;
            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(sessionKey, oldStatus, newStatus));
        }

        private void StartHeartbeatTimer(string sessionKey)
        {
            StopHeartbeatTimer(sessionKey);

            var timer = new Timer(_ =>
            {
                HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEvent(sessionKey, DateTime.UtcNow));
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

            _heartbeatTimers[sessionKey] = timer;
        }

        private void StopHeartbeatTimer(string sessionKey)
        {
            if (_heartbeatTimers.TryGetValue(sessionKey, out var timer))
            {
                timer.Dispose();
                _heartbeatTimers.Remove(sessionKey);
            }
        }
    }
}
