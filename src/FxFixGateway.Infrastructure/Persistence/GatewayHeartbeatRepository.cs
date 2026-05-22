using FxFixGateway.Domain.Interfaces;
using MySql.Data.MySqlClient;

namespace FxFixGateway.Infrastructure.Persistence
{
    public class GatewayHeartbeatRepository : IGatewayHeartbeatRepository
    {
        private readonly string _connectionString;

        public GatewayHeartbeatRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

            _connectionString = connectionString;
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

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@SessionKey", sessionKey);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}