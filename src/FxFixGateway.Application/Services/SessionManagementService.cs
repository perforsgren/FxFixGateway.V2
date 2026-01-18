using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FxFixGateway.Domain.Entities;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.Services
{
    public class SessionManagementService
    {
        private readonly IFixEngine _fixEngine;
        private readonly ISessionRepository _sessionRepository;
        private readonly ILogger<SessionManagementService> _logger;
        private readonly Dictionary<string, FixSession> _activeSessions = new Dictionary<string, FixSession>();

        public SessionManagementService(
            IFixEngine fixEngine,
            ISessionRepository sessionRepository,
            ILogger<SessionManagementService> logger)
        {
            _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Subscribe to engine events
            _fixEngine.StatusChanged += OnFixEngineStatusChanged;
            _fixEngine.HeartbeatReceived += OnFixEngineHeartbeatReceived;
            _fixEngine.ErrorOccurred += OnFixEngineErrorOccurred;
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Initializing SessionManagementService...");

            var configurations = await _sessionRepository.GetAllAsync();
            var configList = configurations.ToList();

            _logger.LogInformation("Loaded {Count} session configurations", configList.Count);

            foreach (var config in configList)
            {
                var session = new FixSession(config);
                _activeSessions[config.SessionKey] = session;
                _logger.LogDebug("Registered session: {SessionKey}", config.SessionKey);
            }

            await _fixEngine.InitializeAsync(configList);
            await AutoStartEnabledSessionsAsync();

            _logger.LogInformation("SessionManagementService initialized successfully");
        }

        public IEnumerable<FixSession> GetAllSessions()
        {
            return _activeSessions.Values;
        }

        public FixSession GetSession(string sessionKey)
        {
            return _activeSessions.TryGetValue(sessionKey, out var session) ? session : null;
        }

        public async Task StartSessionAsync(string sessionKey)
        {
            if (!_activeSessions.TryGetValue(sessionKey, out var session))
            {
                throw new InvalidOperationException($"Session {sessionKey} not found");
            }

            _logger.LogInformation("Starting session {SessionKey}", sessionKey);
            session.Start();
            await _fixEngine.StartSessionAsync(sessionKey);
        }

        public async Task StopSessionAsync(string sessionKey)
        {
            if (!_activeSessions.TryGetValue(sessionKey, out var session))
            {
                throw new InvalidOperationException($"Session {sessionKey} not found");
            }

            _logger.LogInformation("Stopping session {SessionKey}", sessionKey);
            session.Stop();
            await _fixEngine.StopSessionAsync(sessionKey);
        }

        public async Task RestartSessionAsync(string sessionKey)
        {
            _logger.LogInformation("Restarting session {SessionKey}", sessionKey);
            await StopSessionAsync(sessionKey);
            await Task.Delay(1000); // Brief pause between stop and start
            await StartSessionAsync(sessionKey);
        }

        public async Task<SessionConfiguration> SaveSessionConfigurationAsync(SessionConfiguration configuration)
        {
            _logger.LogInformation("Saving configuration for session {SessionKey}", configuration.SessionKey);

            var savedConfig = await _sessionRepository.SaveAsync(configuration);

            if (_activeSessions.TryGetValue(configuration.SessionKey, out var session))
            {
                session.UpdateConfiguration(savedConfig);
            }
            else
            {
                var newSession = new FixSession(savedConfig);
                _activeSessions[savedConfig.SessionKey] = newSession;
            }

            return savedConfig;
        }

        public async Task DeleteSessionAsync(string sessionKey)
        {
            _logger.LogInformation("Deleting session {SessionKey}", sessionKey);

            if (_activeSessions.TryGetValue(sessionKey, out var session))
            {
                if (session.Status != SessionStatus.Stopped)
                {
                    await StopSessionAsync(sessionKey);
                }

                _activeSessions.Remove(sessionKey);
            }

            await _sessionRepository.DeleteAsync(sessionKey);
        }

        private async Task AutoStartEnabledSessionsAsync()
        {
            var enabledSessions = _activeSessions.Values
                .Where(s => s.Configuration.IsEnabled)
                .ToList();

            _logger.LogInformation("Auto-starting {Count} enabled sessions", enabledSessions.Count);

            foreach (var session in enabledSessions)
            {
                try
                {
                    await StartSessionAsync(session.Configuration.SessionKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to auto-start session {SessionKey}",
                        session.Configuration.SessionKey);
                }
            }
        }

        private void OnFixEngineStatusChanged(object sender, Domain.Events.SessionStatusChangedEvent e)
        {
            if (_activeSessions.TryGetValue(e.SessionKey, out var session))
            {
                if (e.NewStatus == SessionStatus.LoggedOn)
                {
                    session.MarkAsLoggedOn();
                }
                else if (e.NewStatus == SessionStatus.Stopped)
                {
                    session.MarkAsDisconnected();
                }
            }
        }

        private void OnFixEngineHeartbeatReceived(object sender, Domain.Events.HeartbeatReceivedEvent e)
        {
            if (_activeSessions.TryGetValue(e.SessionKey, out var session))
            {
                session.RecordHeartbeat();
            }
        }

        private void OnFixEngineErrorOccurred(object sender, Domain.Events.ErrorOccurredEvent e)
        {
            if (_activeSessions.TryGetValue(e.SessionKey, out var session))
            {
                session.MarkAsError(e.ErrorMessage);
            }
        }
    }
}
