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
    // ============================================================================
    // ACK-FLÖDET via trade_stp.tradesystemlink
    // ============================================================================
    //
    // Detta repository pollar trade_stp.tradesystemlink för READY_TO_ACK trades.
    //
    // Flöde:
    // 1. AE kommer in → QuickFixApplication sparar till messagein
    // 2. FxTradeHub parser AE → skapar trade + tradesystemlink (Status='NEW')
    // 3. Blotter bokar i MX3 → uppdaterar tradesystemlink.Status='READY_TO_ACK'
    // 4. AckPollingService pollar → skickar AR → uppdaterar till 'ACK_SENT'
    //
    // ============================================================================

    public sealed class AckQueueRepository : IAckQueueRepository
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
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@MaxCount", maxCount);

                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

                while (await reader.ReadAsync())
                {
                    var tradeId = reader.GetInt64("TradeId");
                    var sessionKey = reader.IsDBNull(reader.GetOrdinal("SessionKey"))
                        ? string.Empty
                        : reader.GetString("SessionKey");
                    var tradeReportId = reader.IsDBNull(reader.GetOrdinal("TradeReportId"))
                        ? string.Empty
                        : reader.GetString("TradeReportId");
                    var internTradeId = reader.IsDBNull(reader.GetOrdinal("InternTradeId"))
                        ? string.Empty
                        : reader.GetString("InternTradeId");
                    var createdUtc = reader.GetDateTime("CreatedUtc");

                    // Skip if required fields are missing
                    if (string.IsNullOrWhiteSpace(sessionKey) ||
                        string.IsNullOrWhiteSpace(tradeReportId) ||
                        string.IsNullOrWhiteSpace(internTradeId))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] Skipping trade {tradeId} - missing required fields");
                        continue;
                    }

                    var ack = new PendingAck(
                        tradeId: tradeId,
                        sessionKey: sessionKey,
                        tradeReportId: tradeReportId,
                        internTradeId: internTradeId,
                        createdUtc: createdUtc
                    );
                    result.Add(ack);
                }
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetPendingAcksAsync error: {ex.Number} - {ex.Message}");
            }

            return result;
        }

        public async Task<IEnumerable<AckEntry>> GetAcksBySessionAsync(string sessionKey, AckStatus? statusFilter = null, int maxCount = 100)
        {
            var result = new List<AckEntry>();

            var sql = @"
                SELECT
                    tsl.StpTradeId AS TradeId,
                    m.SessionKey,
                    m.SourceMessageKey AS TradeReportId,
                    tsl.AckInternalTradeId AS InternTradeId,
                    tsl.Status,
                    tsl.CreatedTime AS CreatedUtc,
                    tsl.UpdatedTime AS SentUtc
                FROM tradesystemlink tsl
                INNER JOIN trade t ON t.StpTradeId = tsl.StpTradeId
                INNER JOIN messagein m ON m.MessageInId = t.MessageInId
                WHERE m.SessionKey = @SessionKey
                  AND tsl.SystemCode = 'FIX_ACK'";

            if (statusFilter.HasValue)
            {
                var systemLinkStatus = statusFilter.Value switch
                {
                    AckStatus.Pending => "READY_TO_ACK",
                    AckStatus.Sent => "ACK_SENT",
                    AckStatus.Failed => "ACK_ERROR",
                    _ => "READY_TO_ACK"
                };
                sql += " AND tsl.Status = @Status";
            }

            sql += " ORDER BY tsl.CreatedTime DESC LIMIT @MaxCount;";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey", sessionKey);
                command.Parameters.AddWithValue("@MaxCount", maxCount);

                if (statusFilter.HasValue)
                {
                    var systemLinkStatus = statusFilter.Value switch
                    {
                        AckStatus.Pending => "READY_TO_ACK",
                        AckStatus.Sent => "ACK_SENT",
                        AckStatus.Failed => "ACK_ERROR",
                        _ => "READY_TO_ACK"
                    };
                    command.Parameters.AddWithValue("@Status", systemLinkStatus);
                }

                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

                while (await reader.ReadAsync())
                {
                    var statusStr = reader.GetString("Status");
                    var status = statusStr switch
                    {
                        "ACK_SENT" => AckStatus.Sent,
                        "ACK_ERROR" => AckStatus.Failed,
                        _ => AckStatus.Pending
                    };

                    var entry = new AckEntry(
                        tradeId: reader.GetInt64("TradeId"),
                        sessionKey: reader.GetString("SessionKey"),
                        tradeReportId: reader.GetString("TradeReportId"),
                        internTradeId: reader.GetString("InternTradeId"),
                        status: status,
                        createdUtc: reader.GetDateTime("CreatedUtc"),
                        sentUtc: reader.IsDBNull(reader.GetOrdinal("SentUtc")) ? null : reader.GetDateTime("SentUtc")
                    );
                    result.Add(entry);
                }
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetAcksBySessionAsync error: {ex.Number} - {ex.Message}");
            }

            return result;
        }

        public async Task UpdateAckStatusAsync(long tradeId, AckStatus status, DateTime? sentUtc)
        {
            // Map AckStatus enum to tradesystemlink.Status values
            string systemLinkStatus = status switch
            {
                AckStatus.Sent => "ACK_SENT",
                AckStatus.Failed => "ACK_ERROR",
                _ => "READY_TO_ACK"
            };

            var sql = @"
                UPDATE tradesystemlink
                SET Status = @Status, UpdatedTime = @SentUtc
                WHERE StpTradeId = @TradeId
                  AND SystemCode = 'FIX_ACK';";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TradeId", tradeId);
                command.Parameters.AddWithValue("@Status", systemLinkStatus);
                command.Parameters.AddWithValue("@SentUtc", sentUtc ?? DateTime.UtcNow);

                await command.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] UpdateAckStatusAsync error: {ex.Number} - {ex.Message}");
            }
        }

        public async Task<int> GetPendingCountAsync(string sessionKey)
        {
            var sql = @"
                SELECT COUNT(*)
                FROM tradesystemlink tsl
                INNER JOIN trade t ON t.StpTradeId = tsl.StpTradeId
                INNER JOIN messagein m ON m.MessageInId = t.MessageInId
                WHERE m.SessionKey = @SessionKey
                  AND tsl.SystemCode = 'FIX_ACK'
                  AND tsl.Status = 'READY_TO_ACK';";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey", sessionKey);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetPendingCountAsync error: {ex.Number} - {ex.Message}");
                return 0;
            }
        }

        public async Task<AckStatistics> GetStatisticsAsync(string sessionKey)
        {
            var sql = @"
                SELECT
                    SUM(CASE WHEN tsl.Status = 'READY_TO_ACK' THEN 1 ELSE 0 END) AS PendingCount,
                    SUM(CASE WHEN tsl.Status = 'ACK_SENT' AND DATE(tsl.UpdatedTime) = CURDATE() THEN 1 ELSE 0 END) AS SentTodayCount,
                    SUM(CASE WHEN tsl.Status = 'ACK_ERROR' THEN 1 ELSE 0 END) AS FailedCount
                FROM tradesystemlink tsl
                INNER JOIN trade t ON t.StpTradeId = tsl.StpTradeId
                INNER JOIN messagein m ON m.MessageInId = t.MessageInId
                WHERE m.SessionKey = @SessionKey
                  AND tsl.SystemCode = 'FIX_ACK';";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
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
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetStatisticsAsync error: {ex.Number} - {ex.Message}");
            }

            return AckStatistics.Empty;
        }

    }
}
