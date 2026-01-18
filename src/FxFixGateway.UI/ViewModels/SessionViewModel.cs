using System;
using CommunityToolkit.Mvvm.ComponentModel;
using FxFixGateway.Domain.Entities;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Interfaces;

namespace FxFixGateway.UI.ViewModels
{
    public partial class SessionViewModel : ObservableObject, IDisposable
    {
        private readonly FixSession _session;
        private readonly IFixEngine _fixEngine;

        public SessionViewModel(FixSession session, IFixEngine fixEngine)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));

            // Lyssna på FIX engine events
            _fixEngine.StatusChanged += OnFixEngineStatusChanged;
            _fixEngine.HeartbeatReceived += OnFixEngineHeartbeatReceived;
            _fixEngine.ErrorOccurred += OnFixEngineErrorOccurred;
        }

        private void OnFixEngineStatusChanged(object? sender, Domain.Events.SessionStatusChangedEvent e)
        {
            // Uppdatera bara om det är vår session
            if (e.SessionKey != SessionKey) return;

            // Notify UI om status properties
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
        }

        private void OnFixEngineHeartbeatReceived(object? sender, Domain.Events.HeartbeatReceivedEvent e)
        {
            if (e.SessionKey != SessionKey) return;

            OnPropertyChanged(nameof(LastHeartbeatFormatted));
            OnPropertyChanged(nameof(LastHeartbeat));
        }

        private void OnFixEngineErrorOccurred(object? sender, Domain.Events.ErrorOccurredEvent e)
        {
            if (e.SessionKey != SessionKey) return;

            OnPropertyChanged(nameof(LastError));
        }

        // Basic Properties
        public string SessionKey => _session.Configuration.SessionKey;
        public string VenueCode => _session.Configuration.VenueCode;
        public string ConnectionType => _session.Configuration.ConnectionType;
        public string Description => _session.Configuration.Description;

        // Connection Properties
        public string Host => _session.Configuration.Host;
        public int Port => _session.Configuration.Port;
        public string SenderCompID => _session.Configuration.SenderCompId;
        public string TargetCompID => _session.Configuration.TargetCompId;
        public string BeginString => _session.Configuration.FixVersion;

        // Timing Properties
        public int HeartbeatInterval => _session.Configuration.HeartBtIntSec;
        public int ReconnectInterval => _session.Configuration.ReconnectIntervalSeconds;
        public string StartTimeFormatted => _session.Configuration.StartTime.ToString(@"hh\:mm\:ss");
        public string EndTimeFormatted => _session.Configuration.EndTime.ToString(@"hh\:mm\:ss");

        // SSL Properties
        public bool UseSsl => _session.Configuration.UseSsl;
        public string SslServerName => _session.Configuration.SslServerName;

        // ACK Properties
        public bool AckSupported => _session.Configuration.AckSupported;
        public string AckMode => _session.Configuration.AckMode;

        // Status Properties
        public SessionStatus Status => _session.Status;
        public string StatusText => Status.ToString();
        public bool IsEnabled => _session.Configuration.IsEnabled;
        public string LastError => _session.LastError ?? "None";

        public string LastLogonFormatted => _session.LastLogonTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";
        public string LastLogoutFormatted => _session.LastLogoutTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";
        public string LastHeartbeatFormatted => _session.LastHeartbeatTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";
        public string LastHeartbeat => _session.LastHeartbeatTime?.ToString("HH:mm:ss") ?? "Never";

        // Timeout Properties
        public int LogonTimeout => 30;
        public int LogoutTimeout => 5;
        public bool ResetOnLogon => false;

        // Computed Properties
        public string StatusColor => Status switch
        {
            SessionStatus.LoggedOn => "#4CAF50",
            SessionStatus.Connecting => "#FFC107",
            SessionStatus.Starting => "#2196F3",
            SessionStatus.Disconnecting => "#FF9800",
            SessionStatus.Error => "#F44336",
            _ => "#9E9E9E"
        };

        public bool CanStart => Status == SessionStatus.Stopped || Status == SessionStatus.Error;
        public bool CanStop => Status != SessionStatus.Stopped && Status != SessionStatus.Error;

        // Access to underlying entity for commands
        public FixSession Session => _session;

        public void Dispose()
        {
            _fixEngine.StatusChanged -= OnFixEngineStatusChanged;
            _fixEngine.HeartbeatReceived -= OnFixEngineHeartbeatReceived;
            _fixEngine.ErrorOccurred -= OnFixEngineErrorOccurred;
        }
    }
}
