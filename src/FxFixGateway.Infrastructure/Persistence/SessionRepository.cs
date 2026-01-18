using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Infrastructure.Persistence
{
    public class SessionRepository : ISessionRepository
    {
        private readonly string _connectionString;

        public SessionRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task<IEnumerable<SessionConfiguration>> GetAllAsync()
        {
            var configurations = new List<SessionConfiguration>();

            const string sql = @"
                SELECT 
                    ConnectionId,
                    SessionKey,
                    VenueCode,
                    ConnectionType,
                    Description,
                    FixVersion,
                    Host,
                    Port,
                    SenderCompId,
                    TargetCompId,
                    HeartBtIntSec,
                    UseSsl,
                    SslServerName,
                    LogonUsername,
                    Password,
                    AckSupported,
                    AckMode,
                    ReconnectIntervalSeconds,
                    StartTime,
                    EndTime,
                    UseDataDictionary,
                    DataDictionaryFile,
                    IsEnabled,
                    CreatedUtc,
                    UpdatedUtc,
                    UpdatedBy,
                    Notes
                FROM fix_connection_config
                ORDER BY SessionKey";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new MySqlCommand(sql, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var config = MapReaderToConfiguration(reader);
                        configurations.Add(config);
                    }
                }
            }

            return configurations;
        }

        public async Task<SessionConfiguration> GetBySessionKeyAsync(string sessionKey)
        {
            const string sql = @"
                SELECT 
                    ConnectionId,
                    SessionKey,
                    VenueCode,
                    ConnectionType,
                    Description,
                    FixVersion,
                    Host,
                    Port,
                    SenderCompId,
                    TargetCompId,
                    HeartBtIntSec,
                    UseSsl,
                    SslServerName,
                    LogonUsername,
                    Password,
                    AckSupported,
                    AckMode,
                    ReconnectIntervalSeconds,
                    StartTime,
                    EndTime,
                    UseDataDictionary,
                    DataDictionaryFile,
                    IsEnabled,
                    CreatedUtc,
                    UpdatedUtc,
                    UpdatedBy,
                    Notes
                FROM fix_connection_config
                WHERE SessionKey = @SessionKey";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new MySqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@SessionKey", sessionKey);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return MapReaderToConfiguration(reader);
                        }
                    }
                }
            }

            return null;
        }

        public async Task<SessionConfiguration> SaveAsync(SessionConfiguration configuration)
        {
            const string sql = @"
        INSERT INTO fix_connection_config (
            SessionKey, VenueCode, ConnectionType, Description, FixVersion,
            Host, Port, SenderCompId, TargetCompId, HeartBtIntSec,
            UseSsl, SslServerName, LogonUsername, Password,
            AckSupported, AckMode, ReconnectIntervalSeconds,
            StartTime, EndTime, UseDataDictionary, DataDictionaryFile,
            IsEnabled, CreatedUtc, UpdatedUtc, UpdatedBy, Notes
        ) VALUES (
            @SessionKey, @VenueCode, @ConnectionType, @Description, @FixVersion,
            @Host, @Port, @SenderCompId, @TargetCompId, @HeartBtIntSec,
            @UseSsl, @SslServerName, @LogonUsername, @Password,
            @AckSupported, @AckMode, @ReconnectIntervalSeconds,
            @StartTime, @EndTime, @UseDataDictionary, @DataDictionaryFile,
            @IsEnabled, @CreatedUtc, @UpdatedUtc, @UpdatedBy, @Notes
        )
        ON DUPLICATE KEY UPDATE
            VenueCode = @VenueCode,
            ConnectionType = @ConnectionType,
            Description = @Description,
            FixVersion = @FixVersion,
            Host = @Host,
            Port = @Port,
            SenderCompId = @SenderCompId,
            TargetCompId = @TargetCompId,
            HeartBtIntSec = @HeartBtIntSec,
            UseSsl = @UseSsl,
            SslServerName = @SslServerName,
            LogonUsername = @LogonUsername,
            Password = @Password,
            AckSupported = @AckSupported,
            AckMode = @AckMode,
            ReconnectIntervalSeconds = @ReconnectIntervalSeconds,
            StartTime = @StartTime,
            EndTime = @EndTime,
            UseDataDictionary = @UseDataDictionary,
            DataDictionaryFile = @DataDictionaryFile,
            IsEnabled = @IsEnabled,
            UpdatedUtc = @UpdatedUtc,
            UpdatedBy = @UpdatedBy,
            Notes = @Notes";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new MySqlCommand(sql, connection))
                {
                    AddConfigurationParameters(command, configuration);
                    await command.ExecuteNonQueryAsync();
                }
            }

            // Return the saved configuration
            return configuration;
        }


        public async Task DeleteAsync(string sessionKey)
        {
            const string sql = "DELETE FROM fix_connection_config WHERE SessionKey = @SessionKey";

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new MySqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@SessionKey", sessionKey);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private SessionConfiguration MapReaderToConfiguration(System.Data.Common.DbDataReader reader)
        {
            return new SessionConfiguration(
                connectionId: reader.GetInt32(reader.GetOrdinal("ConnectionId")),
                sessionKey: reader.GetString(reader.GetOrdinal("SessionKey")),
                venueCode: reader.IsDBNull(reader.GetOrdinal("VenueCode")) ? string.Empty : reader.GetString(reader.GetOrdinal("VenueCode")),
                connectionType: reader.IsDBNull(reader.GetOrdinal("ConnectionType")) ? string.Empty : reader.GetString(reader.GetOrdinal("ConnectionType")),
                description: reader.IsDBNull(reader.GetOrdinal("Description")) ? string.Empty : reader.GetString(reader.GetOrdinal("Description")),
                fixVersion: reader.GetString(reader.GetOrdinal("FixVersion")),
                host: reader.GetString(reader.GetOrdinal("Host")),
                port: reader.GetInt32(reader.GetOrdinal("Port")),
                senderCompId: reader.GetString(reader.GetOrdinal("SenderCompId")),
                targetCompId: reader.GetString(reader.GetOrdinal("TargetCompId")),
                heartBtIntSec: reader.GetInt32(reader.GetOrdinal("HeartBtIntSec")),
                useSsl: reader.GetBoolean(reader.GetOrdinal("UseSsl")),
                sslServerName: reader.IsDBNull(reader.GetOrdinal("SslServerName")) ? string.Empty : reader.GetString(reader.GetOrdinal("SslServerName")),
                logonUsername: reader.IsDBNull(reader.GetOrdinal("LogonUsername")) ? string.Empty : reader.GetString(reader.GetOrdinal("LogonUsername")),
                password: reader.IsDBNull(reader.GetOrdinal("Password")) ? string.Empty : reader.GetString(reader.GetOrdinal("Password")),
                ackSupported: reader.GetBoolean(reader.GetOrdinal("AckSupported")),
                ackMode: reader.IsDBNull(reader.GetOrdinal("AckMode")) ? string.Empty : reader.GetString(reader.GetOrdinal("AckMode")),
                reconnectIntervalSeconds: reader.GetInt32(reader.GetOrdinal("ReconnectIntervalSeconds")),
                startTime: (TimeSpan)reader.GetValue(reader.GetOrdinal("StartTime")),
                endTime: (TimeSpan)reader.GetValue(reader.GetOrdinal("EndTime")),
                useDataDictionary: reader.GetBoolean(reader.GetOrdinal("UseDataDictionary")),
                dataDictionaryFile: reader.IsDBNull(reader.GetOrdinal("DataDictionaryFile")) ? string.Empty : reader.GetString(reader.GetOrdinal("DataDictionaryFile")),
                isEnabled: reader.GetBoolean(reader.GetOrdinal("IsEnabled")),
                createdUtc: reader.GetDateTime(reader.GetOrdinal("CreatedUtc")),
                updatedUtc: reader.GetDateTime(reader.GetOrdinal("UpdatedUtc")),
                updatedBy: reader.IsDBNull(reader.GetOrdinal("UpdatedBy")) ? string.Empty : reader.GetString(reader.GetOrdinal("UpdatedBy")),
                notes: reader.IsDBNull(reader.GetOrdinal("Notes")) ? string.Empty : reader.GetString(reader.GetOrdinal("Notes"))
            );
        }


        private void AddConfigurationParameters(MySqlCommand command, SessionConfiguration config)
        {
            command.Parameters.AddWithValue("@SessionKey", config.SessionKey);
            command.Parameters.AddWithValue("@VenueCode", config.VenueCode);
            command.Parameters.AddWithValue("@ConnectionType", config.ConnectionType);
            command.Parameters.AddWithValue("@Description", config.Description);
            command.Parameters.AddWithValue("@FixVersion", config.FixVersion);
            command.Parameters.AddWithValue("@Host", config.Host);
            command.Parameters.AddWithValue("@Port", config.Port);
            command.Parameters.AddWithValue("@SenderCompId", config.SenderCompId);
            command.Parameters.AddWithValue("@TargetCompId", config.TargetCompId);
            command.Parameters.AddWithValue("@HeartBtIntSec", config.HeartBtIntSec);
            command.Parameters.AddWithValue("@UseSsl", config.UseSsl);
            command.Parameters.AddWithValue("@SslServerName", config.SslServerName);
            command.Parameters.AddWithValue("@LogonUsername", config.LogonUsername);
            command.Parameters.AddWithValue("@Password", config.Password);
            command.Parameters.AddWithValue("@AckSupported", config.AckSupported);
            command.Parameters.AddWithValue("@AckMode", config.AckMode);
            command.Parameters.AddWithValue("@ReconnectIntervalSeconds", config.ReconnectIntervalSeconds);
            command.Parameters.AddWithValue("@StartTime", config.StartTime);
            command.Parameters.AddWithValue("@EndTime", config.EndTime);
            command.Parameters.AddWithValue("@UseDataDictionary", config.UseDataDictionary);
            command.Parameters.AddWithValue("@DataDictionaryFile", config.DataDictionaryFile);
            command.Parameters.AddWithValue("@IsEnabled", config.IsEnabled);
            command.Parameters.AddWithValue("@CreatedUtc", config.CreatedUtc);
            command.Parameters.AddWithValue("@UpdatedUtc", DateTime.UtcNow);
            command.Parameters.AddWithValue("@UpdatedBy", config.UpdatedBy);
            command.Parameters.AddWithValue("@Notes", config.Notes);
        }
    }
}
