using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using MySql.Data.MySqlClient;

namespace FxFixGateway.Infrastructure.Persistence
{
    public class AckQueueRepository : IAckQueueRepository
    {
        private readonly string _connectionString;

        public AckQueueRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

            _connectionString = connectionString;
        }

        public async Task<IEnumerable<PendingAck>> GetPendingAcksAsync(int maxCount = 100)
        {
            var result = new List<PendingAck>();

            // Ändra från fix_config_dev.Trades till trade_stp.tradesystemlink + joins
            var sql = @"
        SELECT
            tsl.StpTradeId AS TradeId,
            m.SessionKey,
            m.SourceMessageKey AS TradeReportId,
            tsl.AckInternalTradeId AS InternTradeId,
            tsl.CreatedTime AS CreatedUtc
        FROM tradesystemlink tsl
        INNER JOIN trade t ON t.StpTradeId = tsl.StpTradeId
        INNER JOIN messagein m ON m.MessageInId = t.MessageInId
        WHERE tsl.SystemCode = 'FIX_ACK'
          AND tsl.Status = 'READY_TO_ACK'
        ORDER BY tsl.CreatedTime ASC
        LIMIT @MaxCount;";

            try
            {
                // Byt connectionString till STP-databasen
                var stpConnectionString = _connectionString.Replace("fix_config_dev", "trade_stp");

                await using var connection = new MySqlConnection(stpConnectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@MaxCount", maxCount);

                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

                while (await reader.ReadAsync())
                {
                    var ack = new PendingAck(
                        tradeId: reader.GetInt64("StpTradeId"),
                        sessionKey: reader.GetString("SessionKey"),
                        tradeReportId: reader.IsDBNull(reader.GetOrdinal("TradeReportId"))
                            ? string.Empty
                            : reader.GetString("TradeReportId"),
                        internTradeId: reader.GetString("AckInternalTradeId"),
                        createdUtc: DateTime.UtcNow // Vi har inte CreatedUtc i tradesystemlink, kan lägga till senare
                    );
                    result.Add(ack);
                }
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetPendingAcksAsync error: {ex.Number} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"SQL: {sql}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] Unexpected error: {ex.Message}");
            }

            return result;
        }

        public async Task<IEnumerable<AckEntry>> GetAcksBySessionAsync(string sessionKey, AckStatus? statusFilter = null, int maxCount = 100)
        {
            var result = new List<AckEntry>();

            var sql = @"
                SELECT 
                    tsl.StpTradeId,
                    tsl.AckInternalTradeId,
                    tsl.Status,
                    tsl.LastStatusUtc AS CreatedUtc,
                    tsl.SentUtc,
                    m.SessionKey,
                    m.SourceMessageKey AS TradeReportId
                FROM tradesystemlink tsl
                JOIN trade t ON t.StpTradeId = tsl.StpTradeId
                JOIN messagein m ON m.MessageInId = t.MessageInId
                WHERE m.SessionKey = @SessionKey
                AND tsl.SystemCode = 'FIX_ACK'";

            if (statusFilter.HasValue)
            {
                sql += " AND tsl.Status = @Status";
            }

            sql += " ORDER BY tsl.LastStatusUtc DESC LIMIT @MaxCount;";

            try
            {
                var stpConnectionString = _connectionString.Replace("fix_config_dev", "trade_stp");

                await using var connection = new MySqlConnection(stpConnectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey", sessionKey);
                command.Parameters.AddWithValue("@MaxCount", maxCount);

                if (statusFilter.HasValue)
                {
                    command.Parameters.AddWithValue("@Status", statusFilter.Value.ToString());
                }

                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

                while (await reader.ReadAsync())
                {
                    var statusStr = reader.GetString("Status");
                    var status = Enum.TryParse<AckStatus>(statusStr, out var parsed) ? parsed : AckStatus.Pending;

                    var entry = new AckEntry(
                        tradeId: reader.GetInt64("StpTradeId"),
                        sessionKey: reader.GetString("SessionKey"),
                        tradeReportId: reader.IsDBNull(reader.GetOrdinal("TradeReportId"))
                            ? string.Empty
                            : reader.GetString("TradeReportId"),
                        internTradeId: reader.IsDBNull(reader.GetOrdinal("AckInternalTradeId"))
                            ? string.Empty
                            : reader.GetString("AckInternalTradeId"),
                        status: status,
                        createdUtc: reader.GetDateTime("CreatedUtc"),
                        sentUtc: reader.IsDBNull(reader.GetOrdinal("SentUtc"))
                            ? null
                            : reader.GetDateTime("SentUtc")
                    );
                    result.Add(entry);
                }
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetAcksBySessionAsync error: {ex.Number} - {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] Unexpected error: {ex.Message}");
            }

            return result;
        }

        public async Task UpdateAckStatusAsync(long tradeId, AckStatus status, DateTime? sentUtc)
        {
            var sql = @"
                UPDATE tradesystemlink
                SET Status = @Status, SentUtc = @SentUtc, LastStatusUtc = @LastStatusUtc
                WHERE StpTradeId = @TradeId
                AND SystemCode = 'FIX_ACK';";

            try
            {
                var stpConnectionString = _connectionString.Replace("fix_config_dev", "trade_stp");

                await using var connection = new MySqlConnection(stpConnectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TradeId", tradeId);
                command.Parameters.AddWithValue("@Status", status.ToString());
                command.Parameters.AddWithValue("@SentUtc", sentUtc.HasValue ? sentUtc.Value : DBNull.Value);
                command.Parameters.AddWithValue("@LastStatusUtc", DateTime.UtcNow);

                await command.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] UpdateAckStatusAsync error: {ex.Number} - {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] Unexpected error: {ex.Message}");
            }
        }

        public async Task<int> GetPendingCountAsync(string sessionKey)
        {
            var sql = @"
                SELECT COUNT(*) 
                FROM tradesystemlink tsl
                JOIN trade t ON t.StpTradeId = tsl.StpTradeId
                JOIN messagein m ON m.MessageInId = t.MessageInId
                WHERE m.SessionKey = @SessionKey 
                AND tsl.SystemCode = 'FIX_ACK'
                AND tsl.Status = 'READY_TO_ACK';";

            try
            {
                var stpConnectionString = _connectionString.Replace("fix_config_dev", "trade_stp");

                await using var connection = new MySqlConnection(stpConnectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey", sessionKey);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetPendingCountAsync error: {ex.Message}");
                return 0;
            }
        }

        public async Task<AckStatistics> GetStatisticsAsync(string sessionKey)
        {
            var sql = @"
                SELECT 
                    SUM(CASE WHEN tsl.Status = 'READY_TO_ACK' THEN 1 ELSE 0 END) AS PendingCount,
                    SUM(CASE WHEN tsl.Status = 'ACK_SENT' AND DATE(tsl.SentUtc) = CURDATE() THEN 1 ELSE 0 END) AS SentTodayCount,
                    SUM(CASE WHEN tsl.Status = 'ACK_ERROR' THEN 1 ELSE 0 END) AS FailedCount
                FROM tradesystemlink tsl
                JOIN trade t ON t.StpTradeId = tsl.StpTradeId
                JOIN messagein m ON m.MessageInId = t.MessageInId
                WHERE m.SessionKey = @SessionKey
                AND tsl.SystemCode = 'FIX_ACK';";

            try
            {
                var stpConnectionString = _connectionString.Replace("fix_config_dev", "trade_stp");

                await using var connection = new MySqlConnection(stpConnectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey", sessionKey);

                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

                if (await reader.ReadAsync())
                {
                    return new AckStatistics(
                        pendingCount: reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                        sentTodayCount: reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                        failedCount: reader.IsDBNull(2) ? 0 : reader.GetInt32(2)
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetStatisticsAsync error: {ex.Message}");
            }

            return AckStatistics.Empty;
        }
    }
}
