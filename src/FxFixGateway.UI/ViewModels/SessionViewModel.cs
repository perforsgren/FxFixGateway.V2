using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxFixGateway.Domain.Entities;
using FxFixGateway.Domain.Enums;

namespace FxFixGateway.UI.ViewModels
{
    public partial class SessionViewModel : ObservableObject
    {
        private readonly FixSession _session;

        public SessionViewModel(FixSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
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

        // Timeout Properties (not in domain, using default values)
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
    }
}
