using FxFixGateway.Domain.Events;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using MySqlX.XDevAPI;
using QuickFix;
using QuickFix.Transport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FxFixGateway.Infrastructure.QuickFix
{
    /// <summary>
    /// QuickFIX/n implementation av IFixEngine.
    /// Hanterar alla FIX-sessioner via en SocketInitiator.
    /// </summary>
    public class QuickFixEngine : IFixEngine, IDisposable
    {
        private readonly ILogger<QuickFixEngine>? _logger;
        private readonly string _dataDictionaryPath;

        private SocketInitiator? _initiator;
        private QuickFixApplication? _application;
        private SessionSettings? _settings;
        private Dictionary<SessionID, string>? _sessionKeyMap;
        private Dictionary<string, SessionID>? _sessionIdMap;

        private bool _initialized;
        private bool _running;

        public event EventHandler<SessionStatusChangedEvent>? StatusChanged;
        public event EventHandler<MessageReceivedEvent>? MessageReceived;
        public event EventHandler<MessageSentEvent>? MessageSent;
        public event EventHandler<HeartbeatReceivedEvent>? HeartbeatReceived;
        public event EventHandler<ErrorOccurredEvent>? ErrorOccurred;

        public QuickFixEngine(ILogger<QuickFixEngine>? logger = null, string? dataDictionaryPath = null)
        {
            _logger = logger;
            _dataDictionaryPath = dataDictionaryPath ?? GetDefaultDataDictionaryPath();
        }

        public Task InitializeAsync(IEnumerable<SessionConfiguration> sessions)
        {
            if (_initialized)
                throw new InvalidOperationException("QuickFixEngine already initialized");

            var configList = sessions.ToList();
            if (configList.Count == 0)
                throw new ArgumentException("At least one session configuration required");

            _logger?.LogInformation("Initializing QuickFIX engine with {Count} sessions", configList.Count);

            // Bygg SessionSettings
            var builder = new SessionSettingsBuilder(
                dataDictionaryPath: _dataDictionaryPath);

            _settings = builder.Build(configList);

            // Bygg session mappings
            _sessionKeyMap = new Dictionary<SessionID, string>();
            _sessionIdMap = new Dictionary<string, SessionID>();

            foreach (var config in configList)
            {
                var sessionId = new SessionID(
                    config.FixVersion,
                    config.SenderCompId,
                    config.TargetCompId);

                _sessionKeyMap[sessionId] = config.SessionKey;
                _sessionIdMap[config.SessionKey] = sessionId;

                _logger?.LogDebug("Mapped QuickFIX SessionID {SessionID} to SessionKey {SessionKey}",
                    sessionId, config.SessionKey);
            }

            // Skapa Application
            _application = new QuickFixApplication(_sessionKeyMap);
            _application.StatusChanged += OnStatusChanged;
            _application.MessageReceived += OnMessageReceived;
            _application.MessageSent += OnMessageSent;
            _application.HeartbeatReceived += OnHeartbeatReceived;
            _application.ErrorOccurred += OnErrorOccurred;

            // Skapa Initiator (men starta INTE än)
            var storeFactory = new FileStoreFactory(_settings);
            var logFactory = new FileLogFactory(_settings);
            var messageFactory = new DefaultMessageFactory();

            _initiator = new SocketInitiator(
                _application,
                storeFactory,
                _settings,
                logFactory,
                messageFactory);

            _initialized = true;
            _running = false;

            _logger?.LogInformation("QuickFIX engine initialized successfully");
            return Task.CompletedTask;
        }

        public async Task StartSessionAsync(string sessionKey)
        {
            if (!_initialized)
                throw new InvalidOperationException("QuickFixEngine not initialized");

            if (!_sessionIdMap.TryGetValue(sessionKey, out var sessionId))
            {
                _logger?.LogError("SessionKey {SessionKey} not found", sessionKey);
                ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(sessionKey, "Session not found"));
                return;
            }

            // Starta initiator om den inte kör
            if (!_running && _initiator != null)
            {
                _logger?.LogInformation("Starting QuickFIX initiator");
                _initiator.Start();
                _running = true;
                await Task.Delay(500); // Ge QuickFIX tid att starta
            }

            // Logga på sessionen
            _logger?.LogInformation("Logging on session {SessionKey}", sessionKey);

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                Domain.Enums.SessionStatus.Stopped,
                Domain.Enums.SessionStatus.Starting));

            var session = Session.LookupSession(sessionId);
            if (session != null)
            {
                session.Logon();

                StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                    sessionKey,
                    Domain.Enums.SessionStatus.Starting,
                    Domain.Enums.SessionStatus.Connecting));
            }
            else
            {
                _logger?.LogError("QuickFIX Session not found for {SessionKey}", sessionKey);
                ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(sessionKey, "QuickFIX session not found"));
            }
        }

        public async Task StopSessionAsync(string sessionKey)
        {
            if (!_initialized)
                return;

            if (!_sessionIdMap.TryGetValue(sessionKey, out var sessionId))
                return;

            _logger?.LogInformation("Logging out session {SessionKey}", sessionKey);

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                Domain.Enums.SessionStatus.LoggedOn,
                Domain.Enums.SessionStatus.Disconnecting));

            var session = Session.LookupSession(sessionId);
            if (session != null)
            {
                session.Logout("User requested logout");
            }

            await Task.Delay(300); // Ge tid för graceful disconnect

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                Domain.Enums.SessionStatus.Disconnecting,
                Domain.Enums.SessionStatus.Stopped));
        }

        public async Task RestartSessionAsync(string sessionKey)
        {
            await StopSessionAsync(sessionKey);
            await Task.Delay(500);
            await StartSessionAsync(sessionKey);
        }

        public Task SendMessageAsync(string sessionKey, string rawFixMessage)
        {
            if (!_initialized)
                throw new InvalidOperationException("QuickFixEngine not initialized");

            if (!_sessionIdMap.TryGetValue(sessionKey, out var sessionId))
            {
                _logger?.LogError("SessionKey {SessionKey} not found", sessionKey);
                return Task.CompletedTask;
            }

            try
            {
                var message = new QuickFix.Message();
                message.FromString(rawFixMessage, false, null, null, null);

                var session = Session.LookupSession(sessionId);
                if (session != null)
                {
                    session.Send(message);
                    _logger?.LogDebug("Sent message for {SessionKey}", sessionKey);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send message for {SessionKey}", sessionKey);
                ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(sessionKey, ex.Message, ex));
            }

            return Task.CompletedTask;
        }

        public Task ShutdownAsync()
        {
            _logger?.LogInformation("Shutting down QuickFIX engine");

            if (_running && _initiator != null)
            {
                _initiator.Stop();
                _running = false;
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_running && _initiator != null)
            {
                try
                {
                    _initiator.Stop();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error stopping initiator during dispose");
                }
            }

            _initiator?.Dispose();
            _initiator = null;
            _running = false;
            _initialized = false;
        }

        // Event forwarding
        private void OnStatusChanged(object? sender, SessionStatusChangedEvent e) => StatusChanged?.Invoke(this, e);
        private void OnMessageReceived(object? sender, MessageReceivedEvent e) => MessageReceived?.Invoke(this, e);
        private void OnMessageSent(object? sender, MessageSentEvent e) => MessageSent?.Invoke(this, e);
        private void OnHeartbeatReceived(object? sender, HeartbeatReceivedEvent e) => HeartbeatReceived?.Invoke(this, e);
        private void OnErrorOccurred(object? sender, ErrorOccurredEvent e) => ErrorOccurred?.Invoke(this, e);

        private string GetDefaultDataDictionaryPath()
        {
            // Kolla om FIX44_Volbroker.xml finns i working directory
            var path = Path.Combine(Directory.GetCurrentDirectory(), "FIX44_Volbroker.xml");
            return File.Exists(path) ? path : string.Empty;
        }
    }
}
