using FxFixGateway.Domain.Interfaces;
using MySql.Data.MySqlClient;

namespace FxFixGateway.Infrastructure.Persistence
{
    /// <summary>
    /// Manages gateway heartbeat persistence to fxvol.session_heartbeat table.
    /// Critical: SetOfflineAsync uses non-pooled connections with selective retry
    /// to ensure OFFLINE status is persisted even during connection pool exhaustion.
    /// </summary>
    public class GatewayHeartbeatRepository : IGatewayHeartbeatRepository
    {
        private readonly string _connectionString;
        private readonly string _nonPooledConnectionString;

        // Transient error codes that should trigger retry
        private static readonly HashSet<uint> TransientErrorCodes = new()
        {
            1040,   // Too many connections
            1205,   // Lock wait timeout exceeded
            2002,   // Cannot connect to MySQL server
            2003,   // Cannot connect to MySQL server (connection refused)
            2006,   // MySQL server has gone away
            2013,   // Lost connection to MySQL server
        };

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

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = new MySqlCommand(sql, connection);
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

            // Non-pooled + selective retry with exponential back-off.
            // Ensures OFFLINE is written even during pool pressure at shutdown.
            // Retries ONLY on transient failures (network, overload).
            // Permanent failures (auth, syntax) fail fast without retry.
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    using var connection = new MySqlConnection(_nonPooledConnectionString);
                    await connection.OpenAsync();
                    using var cmd = new MySqlCommand(sql, connection);
                    cmd.Parameters.AddWithValue("@SessionKey", sessionKey);
                    await cmd.ExecuteNonQueryAsync();
                    return; // success
                }
                catch (MySqlException ex) when (attempt < 5 && IsTransientError(ex))
                {
                    // Exponential back-off: 1s, 2s, 3s, 4s
                    // + jitter (0-500ms) to avoid thundering herd if multiple
                    // gateway instances restart simultaneously.
                    var jitter = new Random().Next(0, 500);
                    await Task.Delay(
                        TimeSpan.FromSeconds(attempt)
                        .Add(TimeSpan.FromMilliseconds(jitter)));
                }
                catch (MySqlException)
                {
                    // Permanent failure (auth, syntax, etc.) — fail immediately.
                    throw;
                }
            }
        }

        /// <summary>
        /// Determines if a MySql error is transient (worth retrying) or permanent (fail fast).
        /// </summary>
        private static bool IsTransientError(MySqlException ex) =>
            ex?.Number > 0 && TransientErrorCodes.Contains((uint)ex.Number);
    }
}