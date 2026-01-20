using System;

namespace FxFixGateway.Domain.ValueObjects
{
    public sealed class SessionConfiguration : IEquatable<SessionConfiguration>
    {
        // Identifiers
        public int ConnectionId { get; }
        public string SessionKey { get; }
        public string VenueCode { get; }
        public string ConnectionType { get; }
        public string Description { get; }

        // FIX Configuration
        public string FixVersion { get; }
        public string Host { get; }
        public int Port { get; }
        public string SenderCompId { get; }
        public string TargetCompId { get; }
        public int HeartBtIntSec { get; }

        // SSL Configuration
        public bool UseSsl { get; }
        public string SslServerName { get; }

        // Authentication
        public string LogonUsername { get; }
        public string Password { get; }

        // ACK Configuration
        public bool AckSupported { get; }
        public string AckMode { get; }

        // Timing
        public int ReconnectIntervalSeconds { get; }
        public TimeSpan StartTime { get; }
        public TimeSpan EndTime { get; }

        // Data Dictionary
        public bool UseDataDictionary { get; }
        public string DataDictionaryFile { get; }

        // Status and Metadata
        public bool IsEnabled { get; }
        public DateTime CreatedUtc { get; }
        public DateTime UpdatedUtc { get; }
        public string UpdatedBy { get; }
        public string Notes { get; }

        // SSL Tunnel Configuration (för venues som kräver SSL tunnel)
        public bool UseSSLTunnel { get; }
        public string SslRemoteHost { get; }
        public int? SslRemotePort { get; }
        public int? SslLocalPort { get; }
        public string SslSniHost { get; }

        public SessionConfiguration(
            int connectionId,
            string sessionKey,
            string venueCode,
            string connectionType,
            string description,
            string fixVersion,
            string host,
            int port,
            string senderCompId,
            string targetCompId,
            int heartBtIntSec,
            bool useSsl,
            string sslServerName,
            string logonUsername,
            string password,
            bool ackSupported,
            string ackMode,
            int reconnectIntervalSeconds,
            TimeSpan startTime,
            TimeSpan endTime,
            bool useDataDictionary,
            string dataDictionaryFile,
            bool isEnabled,
            DateTime createdUtc,
            DateTime updatedUtc,
            string updatedBy,
            string notes,
            bool useSSLTunnel = false,
            string sslRemoteHost = null,
            int? sslRemotePort = null,
            int? sslLocalPort = null,
            string sslSniHost = null)
        {
            // Validation
            if (string.IsNullOrWhiteSpace(sessionKey))
                throw new ArgumentException("SessionKey cannot be empty", nameof(sessionKey));

            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Host cannot be empty", nameof(host));

            if (port < 1 || port > 65535)
                throw new ArgumentException("Port must be between 1 and 65535", nameof(port));

            if (heartBtIntSec < 1)
                throw new ArgumentException("HeartBtIntSec must be greater than 0", nameof(heartBtIntSec));

            ConnectionId = connectionId;
            SessionKey = sessionKey ?? throw new ArgumentNullException(nameof(sessionKey));
            VenueCode = venueCode ?? string.Empty;
            ConnectionType = connectionType ?? string.Empty;
            Description = description ?? string.Empty;
            FixVersion = fixVersion ?? "FIX.4.4";
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Port = port;
            SenderCompId = senderCompId ?? string.Empty;
            TargetCompId = targetCompId ?? string.Empty;
            HeartBtIntSec = heartBtIntSec;
            UseSsl = useSsl;
            SslServerName = sslServerName ?? string.Empty;
            LogonUsername = logonUsername ?? string.Empty;
            Password = password ?? string.Empty;
            AckSupported = ackSupported;
            AckMode = ackMode ?? string.Empty;
            ReconnectIntervalSeconds = reconnectIntervalSeconds;
            StartTime = startTime;
            EndTime = endTime;
            UseDataDictionary = useDataDictionary;
            DataDictionaryFile = dataDictionaryFile ?? string.Empty;
            IsEnabled = isEnabled;
            CreatedUtc = createdUtc;
            UpdatedUtc = updatedUtc;
            UpdatedBy = updatedBy ?? string.Empty;
            Notes = notes ?? string.Empty;
            UseSSLTunnel = useSSLTunnel;
            SslRemoteHost = sslRemoteHost ?? string.Empty;
            SslRemotePort = sslRemotePort;
            SslLocalPort = sslLocalPort;
            SslSniHost = sslSniHost ?? string.Empty;
        }

        // With methods for creating modified copies
        public SessionConfiguration WithHost(string host)
        {
            return new SessionConfiguration(
                ConnectionId, SessionKey, VenueCode, ConnectionType, Description,
                FixVersion, host, Port, SenderCompId, TargetCompId, HeartBtIntSec,
                UseSsl, SslServerName, LogonUsername, Password, AckSupported, AckMode,
                ReconnectIntervalSeconds, StartTime, EndTime, UseDataDictionary,
                DataDictionaryFile, IsEnabled, CreatedUtc, DateTime.UtcNow, UpdatedBy, Notes);
        }

        public SessionConfiguration WithPort(int port)
        {
            return new SessionConfiguration(
                ConnectionId, SessionKey, VenueCode, ConnectionType, Description,
                FixVersion, Host, port, SenderCompId, TargetCompId, HeartBtIntSec,
                UseSsl, SslServerName, LogonUsername, Password, AckSupported, AckMode,
                ReconnectIntervalSeconds, StartTime, EndTime, UseDataDictionary,
                DataDictionaryFile, IsEnabled, CreatedUtc, DateTime.UtcNow, UpdatedBy, Notes);
        }

        public SessionConfiguration WithEnabled(bool isEnabled)
        {
            return new SessionConfiguration(
                ConnectionId, SessionKey, VenueCode, ConnectionType, Description,
                FixVersion, Host, Port, SenderCompId, TargetCompId, HeartBtIntSec,
                UseSsl, SslServerName, LogonUsername, Password, AckSupported, AckMode,
                ReconnectIntervalSeconds, StartTime, EndTime, UseDataDictionary,
                DataDictionaryFile, isEnabled, CreatedUtc, DateTime.UtcNow, UpdatedBy, Notes);
        }

        public bool Equals(SessionConfiguration other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return ConnectionId == other.ConnectionId && SessionKey == other.SessionKey;
        }

        public override bool Equals(object obj)
        {
            return obj is SessionConfiguration other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ConnectionId, SessionKey);
        }

        public static bool operator ==(SessionConfiguration left, SessionConfiguration right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(SessionConfiguration left, SessionConfiguration right)
        {
            return !Equals(left, right);
        }

        public SessionConfiguration WithSSLTunnel(bool useSSLTunnel, string sslRemoteHost, int? sslRemotePort, int? sslLocalPort, string sslSniHost)
        {
            return new SessionConfiguration(
                ConnectionId, SessionKey, VenueCode, ConnectionType, Description,
                FixVersion, Host, Port, SenderCompId, TargetCompId, HeartBtIntSec,
                UseSsl, SslServerName, LogonUsername, Password, AckSupported, AckMode,
                ReconnectIntervalSeconds, StartTime, EndTime, UseDataDictionary,
                DataDictionaryFile, IsEnabled, CreatedUtc, DateTime.UtcNow, UpdatedBy, Notes,
                useSSLTunnel, sslRemoteHost, sslRemotePort, sslLocalPort, sslSniHost);
        }

    }
}
