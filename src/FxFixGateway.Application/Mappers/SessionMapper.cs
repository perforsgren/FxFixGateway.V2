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
                SessionKey = session.SessionKey,
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
                HeartbeatIntervalSeconds = (int)config.HeartbeatInterval.TotalSeconds,

                UseDataDictionary = config.UseDataDictionary,
                DataDictionaryFile = config.DataDictionaryFile,

                StartTime = config.StartTime,
                EndTime = config.EndTime,
                ReconnectIntervalSeconds = (int)config.ReconnectInterval.TotalSeconds,

                LogonUsername = config.LogonUsername,
                Password = config.Password,

                RequiresAck = config.RequiresAck,
                AckMode = config.AckMode,

                // Runtime state
                Status = session.Status,
                LastLogonUtc = session.LastLogonUtc,
                LastLogoutUtc = session.LastLogoutUtc,
                LastHeartbeatUtc = session.LastHeartbeatUtc,
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
                sessionKey: dto.SessionKey,
                venueCode: dto.VenueCode,
                connectionType: dto.ConnectionType,
                host: dto.Host,
                port: dto.Port,
                fixVersion: dto.FixVersion,
                senderCompId: dto.SenderCompId,
                targetCompId: dto.TargetCompId,
                heartbeatInterval: TimeSpan.FromSeconds(dto.HeartbeatIntervalSeconds),
                startTime: dto.StartTime,
                endTime: dto.EndTime,
                reconnectInterval: TimeSpan.FromSeconds(dto.ReconnectIntervalSeconds),
                updatedBy: dto.UpdatedBy,
                useSsl: dto.UseSsl,
                sslServerName: dto.SslServerName,
                description: dto.Description,
                useDataDictionary: dto.UseDataDictionary,
                dataDictionaryFile: dto.DataDictionaryFile,
                logonUsername: dto.LogonUsername,
                password: dto.Password,
                isEnabled: dto.IsEnabled,
                requiresAck: dto.RequiresAck,
                ackMode: dto.AckMode,
                createdUtc: dto.CreatedUtc,
                updatedUtc: dto.UpdatedUtc,
                connectionId: dto.ConnectionId
            );
        }
    }
}
