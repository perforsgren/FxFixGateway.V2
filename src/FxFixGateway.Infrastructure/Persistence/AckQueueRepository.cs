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

            var sql = @"
        SELECT
            tsl.StpTradeId AS TradeId,
            m.SessionKey,
            m.SourceMessageKey AS TradeReportId,
            tsl.AckInternalTradeId AS InternTradeId,
            tsl.CreatedUtc
        FROM tradesystemlink tsl
        INNER JOIN trade t ON t.StpTradeId = tsl.StpTradeId
        INNER JOIN messagein m ON m.MessageInId = t.MessageInId
        WHERE tsl.SystemCode = 'FIX_ACK'
          AND tsl.Status = 'READY_TO_ACK'
        ORDER BY tsl.CreatedUtc ASC
        LIMIT @MaxCount;";

            try
            {
                var stpConnectionString = _connectionString.Replace("fix_config_dev", "trade_stp");

                await using var connection = new MySqlConnection(stpConnectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@MaxCount", maxCount);

                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

                while (await reader.ReadAsync())
                {
                    var ack = new PendingAck(
                        tradeId: reader.GetInt64("TradeId"),
                        sessionKey: reader.GetString("SessionKey"),
                        tradeReportId: reader.IsDBNull(reader.GetOrdinal("TradeReportId"))
                            ? string.Empty
                            : reader.GetString("TradeReportId"),
                        internTradeId: reader.IsDBNull(reader.GetOrdinal("InternTradeId"))
                            ? string.Empty
                            : reader.GetString("InternTradeId"),
                        createdUtc: reader.GetDateTime("CreatedUtc")
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
                    tsl.CreatedUtc,
                    tsl.LastStatusUtc,
                    m.SessionKey,
                    m.SourceMessageKey AS TradeReportId
                FROM tradesystemlink tsl
                JOIN trade t ON t.StpTradeId = tsl.StpTradeId
                JOIN messagein m ON m.MessageInId = t.MessageInId
                WHERE m.SessionKey = @SessionKey
                AND tsl.SystemCode = 'FIX_ACK'";

            if (statusFilter.HasValue)
            {
                var dbStatus = statusFilter.Value switch
                {
                    AckStatus.Pending => "READY_TO_ACK",
                    AckStatus.Sent => "ACK_SENT",
                    AckStatus.Failed => "ACK_ERROR",
                    _ => "READY_TO_ACK"
                };
                sql += $" AND tsl.Status = '{dbStatus}'";
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

                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

                while (await reader.ReadAsync())
                {
                    var statusStr = reader.GetString("Status");
                    var status = statusStr switch
                    {
                        "READY_TO_ACK" => AckStatus.Pending,
                        "ACK_SENT" => AckStatus.Sent,
                        "ACK_ERROR" => AckStatus.Failed,
                        _ => AckStatus.Pending
                    };

                    // SentUtc = LastStatusUtc om status är ACK_SENT, annars null
                    DateTime? sentUtc = null;
                    if (status == AckStatus.Sent && !reader.IsDBNull(reader.GetOrdinal("LastStatusUtc")))
                    {
                        sentUtc = reader.GetDateTime("LastStatusUtc");
                    }

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
                        sentUtc: sentUtc
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
            var dbStatus = status switch
            {
                AckStatus.Pending => "READY_TO_ACK",
                AckStatus.Sent => "ACK_SENT",
                AckStatus.Failed => "ACK_ERROR",
                _ => "READY_TO_ACK"
            };

            var sql = @"
                UPDATE tradesystemlink
                SET Status = @Status, LastStatusUtc = @LastStatusUtc
                WHERE StpTradeId = @TradeId
                AND SystemCode = 'FIX_ACK';";

            try
            {
                var stpConnectionString = _connectionString.Replace("fix_config_dev", "trade_stp");

                await using var connection = new MySqlConnection(stpConnectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TradeId", tradeId);
                command.Parameters.AddWithValue("@Status", dbStatus);
                command.Parameters.AddWithValue("@LastStatusUtc", sentUtc ?? DateTime.UtcNow);

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
                    SUM(CASE WHEN tsl.Status = 'ACK_SENT' AND DATE(tsl.LastStatusUtc) = CURDATE() THEN 1 ELSE 0 END) AS SentTodayCount,
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

        public async Task InsertWorkflowEventAsync(long stpTradeId, string systemCode, string eventType, string? details = null)
        {
            var sql = @"
                INSERT INTO tradeworkflowevent 
                (StpTradeId, TimestampUtc, EventType, SystemCode, UserId, Details)
                VALUES
                (@StpTradeId, @TimestampUtc, @EventType, @SystemCode, @UserId, @Details);";

            try
            {
                var stpConnectionString = _connectionString.Replace("fix_config_dev", "trade_stp");

                await using var connection = new MySqlConnection(stpConnectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@StpTradeId", stpTradeId);
                command.Parameters.AddWithValue("@TimestampUtc", DateTime.UtcNow);
                command.Parameters.AddWithValue("@EventType", eventType);
                command.Parameters.AddWithValue("@SystemCode", systemCode);
                command.Parameters.AddWithValue("@UserId", "FIX_GATEWAY");  // Identifierar att gateway skrev denna event
                command.Parameters.AddWithValue("@Details", details ?? (object)DBNull.Value);

                await command.ExecuteNonQueryAsync();

                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] Workflow event: {eventType} for trade {stpTradeId}");
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] InsertWorkflowEventAsync error: {ex.Number} - {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] Unexpected error: {ex.Message}");
            }
        }
    }
}
