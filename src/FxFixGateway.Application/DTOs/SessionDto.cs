using System;
using FxFixGateway.Domain.Enums;

namespace FxFixGateway.Application.DTOs
{
    /// <summary>
    /// DTO för att överföra session-data till UI.
    /// Kombinerar både konfiguration och runtime state.
    /// </summary>
    public sealed class SessionDto
    {
        // Identitet
        public string SessionKey { get; set; } = string.Empty;
        public long ConnectionId { get; set; }

        // Konfiguration
        public string VenueCode { get; set; } = string.Empty;
        public string ConnectionType { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsEnabled { get; set; }

        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public bool UseSsl { get; set; }
        public string? SslServerName { get; set; }

        public string FixVersion { get; set; } = string.Empty;
        public string SenderCompId { get; set; } = string.Empty;
        public string TargetCompId { get; set; } = string.Empty;
        public int HeartbeatIntervalSeconds { get; set; }

        public bool UseDataDictionary { get; set; }
        public string? DataDictionaryFile { get; set; }

        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int ReconnectIntervalSeconds { get; set; }

        public string? LogonUsername { get; set; }
        public string? Password { get; set; }

        public bool RequiresAck { get; set; }
        public string? AckMode { get; set; }

        // Runtime state
        public SessionStatus Status { get; set; }
        public DateTime? LastLogonUtc { get; set; }
        public DateTime? LastLogoutUtc { get; set; }
        public DateTime? LastHeartbeatUtc { get; set; }
        public string? LastError { get; set; }

        // Audit
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;

        // Computed properties (för UI convenience)
        public string StatusDisplay => Status.ToString();
        public string LastHeartbeatDisplay => LastHeartbeatUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "-";
        public bool CanStart => Status == SessionStatus.Stopped || Status == SessionStatus.Error;
        public bool CanStop => Status != SessionStatus.Stopped;
        public bool CanEdit => Status == SessionStatus.Stopped;
    }
}
