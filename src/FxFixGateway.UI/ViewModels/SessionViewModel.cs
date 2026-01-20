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
        private bool _disposed;

        public SessionViewModel(FixSession session, IFixEngine fixEngine)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));

            _fixEngine.StatusChanged += OnFixEngineStatusChanged;
            _fixEngine.HeartbeatReceived += OnFixEngineHeartbeatReceived;
            _fixEngine.ErrorOccurred += OnFixEngineErrorOccurred;
        }

        private void OnFixEngineStatusChanged(object? sender, Domain.Events.SessionStatusChangedEvent e)
        {
            if (e.SessionKey != SessionKey || _disposed) return;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                
                if (e.NewStatus == SessionStatus.LoggedOn)
                {
                    OnPropertyChanged(nameof(LastLogonFormatted));
                }
                else if (e.NewStatus == SessionStatus.Stopped)
                {
                    OnPropertyChanged(nameof(LastLogoutFormatted));
                }
            });
        }

        private void OnFixEngineHeartbeatReceived(object? sender, Domain.Events.HeartbeatReceivedEvent e)
        {
            if (e.SessionKey != SessionKey || _disposed) return;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                OnPropertyChanged(nameof(LastHeartbeatFormatted));
                OnPropertyChanged(nameof(LastHeartbeat));
            });
        }

        private void OnFixEngineErrorOccurred(object? sender, Domain.Events.ErrorOccurredEvent e)
        {
            if (e.SessionKey != SessionKey || _disposed) return;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                OnPropertyChanged(nameof(LastError));
            });
        }

        // Basic Properties (readonly)
        public string SessionKey => _session.Configuration.SessionKey;
        public string VenueCode => _session.Configuration.VenueCode;
        public string ConnectionType => _session.Configuration.ConnectionType;
        public string Description => _session.Configuration.Description;
        public string SenderCompID => _session.Configuration.SenderCompId;
        public string TargetCompID => _session.Configuration.TargetCompId;
        public string BeginString => _session.Configuration.FixVersion;

        // EDITABLE: Host
        public string Host
        {
            get => _session.Configuration.Host;
            set
            {
                if (_session.Configuration.Host != value)
                {
                    _session.UpdateConfiguration(_session.Configuration.WithHost(value));
                    OnPropertyChanged();
                }
            }
        }

        // EDITABLE: Port
        public int Port
        {
            get => _session.Configuration.Port;
            set
            {
                if (_session.Configuration.Port != value)
                {
                    _session.UpdateConfiguration(_session.Configuration.WithPort(value));
                    OnPropertyChanged();
                }
            }
        }

        // Timing Properties (readonly)
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
        public string LastError => _session.LastError ?? "None";

        public string LastLogonFormatted => _session.LastLogonTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";
        public string LastLogoutFormatted => _session.LastLogoutTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";
        public string LastHeartbeatFormatted => _session.LastHeartbeatTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";
        public string LastHeartbeat => _session.LastHeartbeatTime?.ToString("HH:mm:ss") ?? "Never";

        // EDITABLE: IsEnabled
        public bool IsEnabled
        {
            get => _session.Configuration.IsEnabled;
            set
            {
                if (_session.Configuration.IsEnabled != value)
                {
                    _session.UpdateConfiguration(_session.Configuration.WithEnabled(value));
                    OnPropertyChanged();
                    System.Diagnostics.Debug.WriteLine($"[SessionViewModel] IsEnabled changed to {value}");
                }
            }
        }

        // Timeout Properties (readonly)
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

        // Access to underlying entity
        public FixSession Session => _session;

        public void Dispose()
        {
            _disposed = true;
            _fixEngine.StatusChanged -= OnFixEngineStatusChanged;
            _fixEngine.HeartbeatReceived -= OnFixEngineHeartbeatReceived;
            _fixEngine.ErrorOccurred -= OnFixEngineErrorOccurred;
        }
    }
}
