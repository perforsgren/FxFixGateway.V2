using System;
using FxFixGateway.Application.DTOs;
using FxFixGateway.Domain.Entities;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Application.Mappers
{
    /// <summary>
    /// Mappar mellan FixSession (domain) och SessionDto (UI).
    /// </summary>
    public static class SessionMapper
    {

        /// <summary>
        /// Konverterar FixSession till SessionDto.
        /// </summary>
        public static SessionDto ToDto(FixSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            var config = session.Configuration;

            return new SessionDto
            {
                // Identitet
                SessionKey = session.Configuration.SessionKey,  // ✅ FIX: session.SessionKey → session.Configuration.SessionKey
                ConnectionId = config.ConnectionId,

                // Konfiguration
                VenueCode = config.VenueCode,
                ConnectionType = config.ConnectionType,
                Description = config.Description,
                IsEnabled = config.IsEnabled,

                Host = config.Host,
                Port = config.Port,
                UseSsl = config.UseSsl,
                SslServerName = config.SslServerName,

                FixVersion = config.FixVersion,
                SenderCompId = config.SenderCompId,
                TargetCompId = config.TargetCompId,
                HeartbeatIntervalSeconds = config.HeartBtIntSec,  // ✅ FIX: (int)config.HeartbeatInterval.TotalSeconds → config.HeartBtIntSec

                UseDataDictionary = config.UseDataDictionary,
                DataDictionaryFile = config.DataDictionaryFile,

                StartTime = config.StartTime,
                EndTime = config.EndTime,
                ReconnectIntervalSeconds = config.ReconnectIntervalSeconds,  // ✅ FIX: (int)config.ReconnectInterval.TotalSeconds → config.ReconnectIntervalSeconds

                LogonUsername = config.LogonUsername,
                Password = config.Password,

                RequiresAck = config.AckSupported,  // ✅ FIX: config.RequiresAck → config.AckSupported
                AckMode = config.AckMode,

                // Runtime state
                Status = session.Status,
                LastLogonUtc = session.LastLogonTime,      // ✅ FIX: LastLogonUtc → LastLogonTime
                LastLogoutUtc = session.LastLogoutTime,    // ✅ FIX: LastLogoutUtc → LastLogoutTime
                LastHeartbeatUtc = session.LastHeartbeatTime,  // ✅ FIX: LastHeartbeatUtc → LastHeartbeatTime
                LastError = session.LastError,

                // Audit
                CreatedUtc = config.CreatedUtc,
                UpdatedUtc = config.UpdatedUtc,
                UpdatedBy = config.UpdatedBy
            };
        }

        /// <summary>
        /// Konverterar SessionDto tillbaka till SessionConfiguration.
        /// Används när användaren uppdaterar config i UI.
        /// </summary>
        public static SessionConfiguration ToConfiguration(SessionDto dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            return new SessionConfiguration(
                connectionId: (int)dto.ConnectionId, // <-- FIX: Explicit cast from long to int
                sessionKey: dto.SessionKey,
                venueCode: dto.VenueCode,
                connectionType: dto.ConnectionType,
                description: dto.Description,
                fixVersion: dto.FixVersion,
                host: dto.Host,
                port: dto.Port,
                senderCompId: dto.SenderCompId,
                targetCompId: dto.TargetCompId,
                heartBtIntSec: dto.HeartbeatIntervalSeconds,
                useSsl: dto.UseSsl,
                sslServerName: dto.SslServerName,
                logonUsername: dto.LogonUsername,
                password: dto.Password,
                ackSupported: dto.RequiresAck,
                ackMode: dto.AckMode,
                reconnectIntervalSeconds: dto.ReconnectIntervalSeconds,
                startTime: dto.StartTime,
                endTime: dto.EndTime,
                useDataDictionary: dto.UseDataDictionary,
                dataDictionaryFile: dto.DataDictionaryFile,
                isEnabled: dto.IsEnabled,
                createdUtc: dto.CreatedUtc,
                updatedUtc: dto.UpdatedUtc,
                updatedBy: dto.UpdatedBy,
                notes: string.Empty
            );
        }

    }
}
