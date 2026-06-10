using FxFixGateway.Domain.Events;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using QF = global::QuickFix;
using FxTradeHub.Domain.Services;
using FxTradeHub.Domain.Parsing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FxFixGateway.Infrastructure.QuickFix
{
    public class QuickFixEngine : IFixEngine, IDisposable
    {
        private readonly ILogger<QuickFixEngine>?        _logger;
        private readonly string                          _dataDictionaryPath;
        private readonly IMessageInService?              _messageInService;
        private readonly IMessageInParserOrchestrator?   _orchestrator;
        private readonly ISecurityListService?           _securityListService;
        private readonly IMarketDataOrchestrator?        _mdOrchestrator;
        private readonly QuickFixSenderProxy?            _senderProxy;
        private readonly IMarketDataService?             _marketDataService;
        private readonly IQuoteRequestService?           _quoteRequestService;
        private readonly ISessionHeartbeatNotifier?      _heartbeatNotifier;
        private readonly IPushNotificationService?         _pushNotification;

        private readonly Dictionary<string, SSLTunnelProxy> _sslTunnels = new();

        private QF.Transport.SocketInitiator? _initiator;
        private QuickFixApplication?          _application;
        private QF.SessionSettings?           _settings;
        private Dictionary<QF.SessionID, string>? _sessionKeyMap;
        private Dictionary<string, QF.SessionID>? _sessionIdMap;
        private Dictionary<string, bool>?         _sessionAutoStart;

        private bool _initialized;
        private bool _running;

        public event EventHandler<SessionStatusChangedEvent>? StatusChanged;
        public event EventHandler<MessageReceivedEvent>?      MessageReceived;
        public event EventHandler<MessageSentEvent>?          MessageSent;
        public event EventHandler<HeartbeatReceivedEvent>?    HeartbeatReceived;
        public event EventHandler<ErrorOccurredEvent>?        ErrorOccurred;

        public QuickFixEngine(
            ILogger<QuickFixEngine>? logger                    = null,
            string? dataDictionaryPath                         = null,
            IMessageInService? messageInService                = null,
            IMessageInParserOrchestrator? orchestrator         = null,
            ISecurityListService? securityListService          = null,
            IMarketDataOrchestrator? mdOrchestrator            = null,
            QuickFixSenderProxy? senderProxy                   = null,
            IMarketDataService? marketDataService              = null,
            IQuoteRequestService? quoteRequestService          = null,
            ISessionHeartbeatNotifier? heartbeatNotifier       = null,
            IPushNotificationService? pushNotification         = null)
        {
            _logger              = logger;
            _dataDictionaryPath  = dataDictionaryPath ?? GetDefaultDataDictionaryPath();
            _messageInService    = messageInService;
            _orchestrator        = orchestrator;
            _securityListService = securityListService;
            _mdOrchestrator      = mdOrchestrator;
            _senderProxy         = senderProxy;
            _marketDataService   = marketDataService;
            _quoteRequestService = quoteRequestService;
            _heartbeatNotifier   = heartbeatNotifier;
            _pushNotification    = pushNotification;
        }

        public async Task InitializeAsync(IEnumerable<SessionConfiguration> sessions)
        {
            if (_initialized)
                throw new InvalidOperationException("QuickFixEngine already initialized");

            var configList = sessions.ToList();
            if (configList.Count == 0)
                throw new ArgumentException("At least one session configuration required");

            _logger?.LogInformation("=== QuickFIX Engine Initializing ===");
            _logger?.LogInformation("Sessions to configure: {Count}", configList.Count);

            foreach (var cfg in configList)
            {
                _logger?.LogInformation(
                    "  [{SessionKey}] {Host}:{Port} SSL={UseSsl} Tunnel={UseTunnel} Enabled={Enabled}",
                    cfg.SessionKey, cfg.Host, cfg.Port, cfg.UseSsl, cfg.UseSSLTunnel, cfg.IsEnabled);
            }

            // STARTA SSL TUNNELS FÖRST
            foreach (var config in configList)
            {
                if (config.UseSSLTunnel &&
                    !string.IsNullOrEmpty(config.SslRemoteHost) &&
                    config.SslRemotePort.HasValue &&
                    config.SslLocalPort.HasValue)
                {
                    try
                    {
                        _logger?.LogInformation("[{SessionKey}] Creating SSL tunnel...", config.SessionKey);

                        var tunnel = new SSLTunnelProxy(
                            config.SessionKey,
                            config.SslRemoteHost,
                            config.SslRemotePort.Value,
                            config.SslLocalPort.Value,
                            config.SslSniHost,
                            _logger);

                        tunnel.Start();
                        _sslTunnels[config.SessionKey] = tunnel;

                        _logger?.LogInformation("[{SessionKey}] SSL Tunnel ready", config.SessionKey);
                        await Task.Delay(500);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "[{SessionKey}] FAILED to start SSL tunnel: {Message}",
                            config.SessionKey, ex.Message);

                        foreach (var t in _sslTunnels.Values)
                        {
                            t.Dispose();
                        }
                        _sslTunnels.Clear();

                        throw;
                    }
                }
            }

            // SEDAN bygg QuickFIX settings
            _logger?.LogInformation("Building QuickFIX SessionSettings...");
            var builder = new SessionSettingsBuilder(dataDictionaryPath: _dataDictionaryPath);

            try
            {
                _settings = builder.Build(configList);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "FAILED to build SessionSettings: {Message}", ex.Message);
                throw;
            }

            foreach (var cfg in configList)
            {
                var dictFile = cfg.DataDictionaryFile ?? "(none configured)";
                var resolved = !string.IsNullOrEmpty(cfg.DataDictionaryFile) && File.Exists(cfg.DataDictionaryFile)
                    ? cfg.DataDictionaryFile
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cfg.DataDictionaryFile ?? "");
                var exists = File.Exists(resolved);
                _logger?.LogDebug(
                    "[{SessionKey}] DataDictionary: configured='{Dict}' resolved='{Resolved}' exists={Exists}",
                    cfg.SessionKey, dictFile, resolved, exists);
            }

            _sessionKeyMap = new Dictionary<global::QuickFix.SessionID, string>();
            _sessionIdMap = new Dictionary<string, global::QuickFix.SessionID>();
            _sessionAutoStart = new Dictionary<string, bool>();
            var sessionCredentials = new Dictionary<string, (string Username, string Password)>();

            foreach (var config in configList)
            {
                var sessionId = new global::QuickFix.SessionID(
                    config.FixVersion,
                    config.SenderCompId,
                    config.TargetCompId);

                _sessionKeyMap[sessionId] = config.SessionKey;
                _sessionIdMap[config.SessionKey] = sessionId;
                _sessionAutoStart[config.SessionKey] = config.IsEnabled;

                if (!string.IsNullOrEmpty(config.LogonUsername) || !string.IsNullOrEmpty(config.Password))
                {
                    sessionCredentials[config.SessionKey] = (config.LogonUsername ?? string.Empty, config.Password ?? string.Empty);
                    _logger?.LogDebug("[{SessionKey}] Credentials configured (user: '{User}', password: {HasPw})",
                        config.SessionKey, config.LogonUsername, !string.IsNullOrEmpty(config.Password));
                }

                _logger?.LogDebug("[{SessionKey}] Mapped to SessionID: {SessionID} (AutoStart={AutoStart})",
                    config.SessionKey, sessionId, config.IsEnabled);
            }

            var disabledSessionKeys = configList
                .Where(c => !c.IsEnabled)
                .Select(c => c.SessionKey)
                .ToList();

            if (_senderProxy != null)
            {
                var sender = new QuickFixSender(
                    _sessionIdMap,
                    _logger as ILogger<QuickFixSender>
                        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<QuickFixSender>.Instance);

                _senderProxy.SetInner(sender);
                _logger?.LogInformation("QuickFixSender initialized and proxy connected");
            }

            // FIX 1: lägg till _pushNotification som sista argument
            _application = new QuickFixApplication(
                _sessionKeyMap,
                sessionCredentials,
                _messageInService,
                _orchestrator,
                disabledSessionKeys,
                _securityListService,
                _mdOrchestrator,
                null,
                _marketDataService,
                _quoteRequestService,
                _heartbeatNotifier,
                _pushNotification);
            _application.StatusChanged += OnStatusChanged;
            _application.MessageReceived += OnMessageReceived;
            _application.MessageSent += OnMessageSent;
            _application.HeartbeatReceived += OnHeartbeatReceived;
            _application.ErrorOccurred += OnErrorOccurred;

            var storeFactory = new global::QuickFix.FileStoreFactory(_settings);
            var logFactory = new FxFixGateway.Infrastructure.Logging.RollingFixLogFactory(
                                   basePath: "log",
                                   maxFileSizeBytes: 20 * 1024 * 1024,
                                   retainedDays: 7);

            // FIX 2: inaktivera system proxy — QuickFix 1.10.0 försöker annars tunnla via corporate proxy
            System.Net.WebRequest.DefaultWebProxy = null;

            // FIX 3: DefaultMessageFactory istället för LenientMessageFactory (som orsakade StackOverflow)
            var messageFactory = new global::QuickFix.DefaultMessageFactory();

            _logger?.LogInformation("Creating SocketInitiator...");
            _initiator = new global::QuickFix.Transport.SocketInitiator(
                _application,
                storeFactory,
                _settings,
                logFactory,
                messageFactory);

            _logger?.LogInformation("Starting SocketInitiator...");
            try
            {
                _initiator.Start();
                _running = true;
                _logger?.LogInformation("SocketInitiator started OK");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "FAILED to start SocketInitiator: [{Type}] {Message}\n{StackTrace}",
                    ex.GetType().Name, ex.Message, ex.StackTrace);

                if (ex.InnerException != null)
                    _logger?.LogError("InnerException: [{Type}] {Message}",
                        ex.InnerException.GetType().Name, ex.InnerException.Message);
                throw;
            }

            await Task.Delay(500);

            foreach (var kvp in _sessionIdMap)
            {
                var sessionKey = kvp.Key;
                var sessionId = kvp.Value;
                var session = QF.Session.LookupSession(sessionId);

                if (session != null)
                {
                    if (_sessionAutoStart.TryGetValue(sessionKey, out var autoStart) && autoStart)
                    {
                        _logger?.LogInformation("[{SessionKey}] Auto-start ENABLED - will connect automatically", sessionKey);
                    }
                    else
                    {
                        session.Logout("Waiting for manual start");
                        _logger?.LogInformation("[{SessionKey}] Auto-start DISABLED - waiting for manual Start", sessionKey);

                        StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                            sessionKey,
                            Domain.Enums.SessionStatus.Starting,
                            Domain.Enums.SessionStatus.Stopped));
                    }
                }
                else
                {
                    _logger?.LogWarning("[{SessionKey}] Session.LookupSession returned NULL!", sessionKey);
                }
            }

            _initialized = true;
            _logger?.LogInformation("=== QuickFIX Engine Initialized Successfully ===");
        }

        public async Task StartSessionAsync(string sessionKey)
        {
            if (!_initialized)
                throw new InvalidOperationException("QuickFixEngine not initialized");

            if (!_sessionIdMap.TryGetValue(sessionKey, out var sessionId))
            {
                _logger?.LogError("[{SessionKey}] NOT FOUND in sessionIdMap", sessionKey);
                ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(sessionKey, "Session not found"));
                return;
            }

            _logger?.LogInformation("[{SessionKey}] Starting session (SessionID: {SessionID})...", sessionKey, sessionId);

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                Domain.Enums.SessionStatus.Stopped,
                Domain.Enums.SessionStatus.Starting));

            var session = QF.Session.LookupSession(sessionId);
            if (session != null)
            {
                _logger?.LogDebug("[{SessionKey}] Calling session.Logon()...", sessionKey);
                session.Logon();

                StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                    sessionKey,
                    Domain.Enums.SessionStatus.Starting,
                    Domain.Enums.SessionStatus.Connecting));
            }
            else
            {
                _logger?.LogError("[{SessionKey}] QuickFIX Session.LookupSession returned NULL", sessionKey);
                ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(sessionKey, "QuickFIX session not found"));
            }
        }

        public async Task StopSessionAsync(string sessionKey)
        {
            if (!_initialized)
                return;

            if (!_sessionIdMap.TryGetValue(sessionKey, out var sessionId))
            {
                _logger?.LogWarning("[{SessionKey}] Not found when trying to stop", sessionKey);
                return;
            }

            _logger?.LogInformation("[{SessionKey}] Stopping session...", sessionKey);

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                Domain.Enums.SessionStatus.LoggedOn,
                Domain.Enums.SessionStatus.Disconnecting));

            var session = QF.Session.LookupSession(sessionId);
            if (session != null)
            {
                session.Logout("User requested logout");
                _logger?.LogDebug("[{SessionKey}] Logout sent", sessionKey);
            }

            await Task.Delay(300);

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                Domain.Enums.SessionStatus.Disconnecting,
                Domain.Enums.SessionStatus.Stopped));
            
            _logger?.LogInformation("[{SessionKey}] Session stopped", sessionKey);
        }

        public async Task RestartSessionAsync(string sessionKey)
        {
            _logger?.LogInformation("[{SessionKey}] Restarting session...", sessionKey);
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
                _logger?.LogError("[{SessionKey}] Not found when trying to send message", sessionKey);
                return Task.CompletedTask;
            }

            try
            {
                var message = new QF.Message();
                message.FromString(rawFixMessage, false, null, null, null);

                var session = QF.Session.LookupSession(sessionId);
                if (session != null)
                {
                    session.Send(message);
                    _logger?.LogDebug("[{SessionKey}] Message sent", sessionKey);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[{SessionKey}] Failed to send message: {Message}", sessionKey, ex.Message);
                ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(sessionKey, ex.Message, ex));
            }

            return Task.CompletedTask;
        }

        public async Task ShutdownAsync()
        {
            _logger?.LogInformation("=== QuickFIX Engine Shutting Down ===");

            if (_running && _initiator != null)
            {
                _logger?.LogInformation("Stopping SocketInitiator...");

                // Stop() är synkron och blockerar tills QuickFIX skickat Logout till motparter
                _initiator.Stop();
                _running = false;

                // Vänta på att socket-trådar faktiskt avslutas (foreground threads)
                var deadline = DateTime.UtcNow.AddSeconds(4);
                while (!_initiator.IsStopped && DateTime.UtcNow < deadline)
                    await Task.Delay(100).ConfigureAwait(false);

                if (!_initiator.IsStopped)
                    _logger?.LogWarning("SocketInitiator did not fully stop — forcing dispose");

                // Dispose frigör socket-handles och avslutar interna trådar
                _initiator.Dispose();
                _initiator = null;
            }

            // Stäng SSL-tunnlar
            _logger?.LogInformation("Stopping {Count} SSL tunnels...", _sslTunnels.Count);
            foreach (var tunnel in _sslTunnels.Values)
                tunnel.Dispose();
            _sslTunnels.Clear();

            _logger?.LogInformation("=== QuickFIX Engine Shutdown Complete ===");
        }

        public void Dispose()
        {
            if (_running && _initiator != null)
            {
                try { _initiator.Stop(); } catch { /* ignore */ }
            }

            if (_application != null)
            {
                _application.StatusChanged   -= OnStatusChanged;
                _application.MessageReceived -= OnMessageReceived;
                _application.MessageSent     -= OnMessageSent;
                _application.HeartbeatReceived -= OnHeartbeatReceived;
                _application.ErrorOccurred   -= OnErrorOccurred;
            }

            _initiator?.Dispose();
            _initiator = null;

            foreach (var tunnel in _sslTunnels.Values)
                tunnel.Dispose();
            _sslTunnels.Clear();

            _running     = false;
            _initialized = false;
        }


        private void OnStatusChanged(object? sender, SessionStatusChangedEvent e) => StatusChanged?.Invoke(this, e);
        private void OnMessageReceived(object? sender, MessageReceivedEvent e) => MessageReceived?.Invoke(this, e);
        private void OnMessageSent(object? sender, MessageSentEvent e) => MessageSent?.Invoke(this, e);
        private void OnHeartbeatReceived(object? sender, HeartbeatReceivedEvent e) => HeartbeatReceived?.Invoke(this, e);
        private void OnErrorOccurred(object? sender, ErrorOccurredEvent e) => ErrorOccurred?.Invoke(this, e);

        private string GetDefaultDataDictionaryPath()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "FIX44_Volbroker.xml");
            return File.Exists(path) ? path : string.Empty;
        }
    }
}
