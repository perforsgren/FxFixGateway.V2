using FxFixGateway.Domain.Interfaces;
using MySql.Data.MySqlClient;

namespace FxFixGateway.Infrastructure.Persistence
{
    public class GatewayHeartbeatRepository : IGatewayHeartbeatRepository
    {
        private readonly string _connectionString;
        private readonly string _nonPooledConnectionString;

        public GatewayHeartbeatRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

            _connectionString = connectionString;

            // Non-pooled variant used for critical SetOfflineAsync so it bypasses
            // an exhausted pool and always gets a fresh physical connection.
            var builder = new MySqlConnectionStringBuilder(connectionString) { Pooling = false };
            _nonPooledConnectionString = builder.ConnectionString;
        }

        public async Task UpdateBeatAsync(string sessionKey)
        {
            const string sql = @"
                INSERT INTO fxvol.session_heartbeat (session_key, status, beat_utc)
                VALUES (@SessionKey, 'ONLINE', UTC_TIMESTAMP(3))
                ON DUPLICATE KEY UPDATE
                    status   = 'ONLINE',
                    beat_utc = VALUES(beat_utc);";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@SessionKey", sessionKey);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetOfflineAsync(string sessionKey)
        {
            const string sql = @"
                INSERT INTO fxvol.session_heartbeat (session_key, status, beat_utc)
                VALUES (@SessionKey, 'OFFLINE', UTC_TIMESTAMP(3))
                ON DUPLICATE KEY UPDATE
                    status   = 'OFFLINE',
                    beat_utc = VALUES(beat_utc);";

            // Non-pooled + up to 5 retry attempts with back-off.
            // Ensures OFFLINE is written even during pool pressure at shutdown.
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    await using var connection = new MySqlConnection(_nonPooledConnectionString);
                    await connection.OpenAsync();
                    await using var cmd = new MySqlCommand(sql, connection);
                    cmd.Parameters.AddWithValue("@SessionKey", sessionKey);
                    await cmd.ExecuteNonQueryAsync();
                    return; // success
                }
                catch (MySqlException) when (attempt < 5)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt)); // 1s, 2s, 3s, 4s
                }
            }
        }
    }
}