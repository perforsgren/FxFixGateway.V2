using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using FxFixGateway.Domain.Constants;
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
                    tsl.StpTradeId         AS TradeId,
                    m.SessionKey,
                    m.SourceMessageKey     AS TradeReportId,
                    m.ExternalTradeKey,
                    tsl.AckInternalTradeId AS InternTradeId,
                    tsl.CreatedUtc
                FROM tradesystemlink tsl
                INNER JOIN trade t ON t.StpTradeId = tsl.StpTradeId
                INNER JOIN messagein m ON m.MessageInId = t.MessageInId
                WHERE tsl.SystemCode = 'FIX_ACK'
                  AND tsl.Status = @StatusReady
                  AND m.SessionKey IS NOT NULL AND m.SessionKey <> ''
                  AND m.SourceMessageKey IS NOT NULL AND m.SourceMessageKey <> ''
                ORDER BY tsl.CreatedUtc ASC
                LIMIT @MaxCount;";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@MaxCount", maxCount);
                command.Parameters.AddWithValue("@StatusReady", DbAckStatus.ReadyToAck);

                using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

                while (await reader.ReadAsync())
                {
                    result.Add(new PendingAck(
                        tradeId: reader.GetInt64("TradeId"),
                        sessionKey: reader.GetString("SessionKey"),
                        tradeReportId: reader.GetString("TradeReportId"),
                        externalTradeKey: reader.IsDBNull(reader.GetOrdinal("ExternalTradeKey"))
                            ? string.Empty : reader.GetString("ExternalTradeKey"),
                        internTradeId: reader.IsDBNull(reader.GetOrdinal("InternTradeId"))
                            ? string.Empty : reader.GetString("InternTradeId"),
                        createdUtc: reader.IsDBNull(reader.GetOrdinal("CreatedUtc"))
                            ? DateTime.UtcNow : reader.GetDateTime("CreatedUtc")
                    ));
                }
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetPendingAcksAsync error: {ex.Number} - {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] Unexpected error: {ex.Message}");
            }

            return result;
        }

        public async Task<IEnumerable<PendingAck>> GetRejectedAcksAsync(int maxCount = 100)
        {
            var result = new List<PendingAck>();

            var sql = @"
                SELECT
                    tsl.StpTradeId     AS TradeId,
                    m.SessionKey,
                    m.SourceMessageKey AS TradeReportId,
                    m.ExternalTradeKey,
                    tsl.LastError,
                    tsl.CreatedUtc
                FROM tradesystemlink tsl
                INNER JOIN trade t ON t.StpTradeId = tsl.StpTradeId
                INNER JOIN messagein m ON m.MessageInId = t.MessageInId
                WHERE tsl.SystemCode = 'FIX_ACK'
                  AND tsl.Status = @StatusRejected
                  AND m.SessionKey IS NOT NULL AND m.SessionKey <> ''
                  AND m.SourceMessageKey IS NOT NULL AND m.SourceMessageKey <> ''
                ORDER BY tsl.CreatedUtc ASC
                LIMIT @MaxCount;";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@MaxCount", maxCount);
                command.Parameters.AddWithValue("@StatusRejected", DbAckStatus.AckRejected);

                using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

                while (await reader.ReadAsync())
                {
                    result.Add(new PendingAck(
                        tradeId: reader.GetInt64("TradeId"),
                        sessionKey: reader.GetString("SessionKey"),
                        tradeReportId: reader.GetString("TradeReportId"),
                        externalTradeKey: reader.IsDBNull(reader.GetOrdinal("ExternalTradeKey"))
                            ? string.Empty : reader.GetString("ExternalTradeKey"),
                        internTradeId: string.Empty,
                        createdUtc: reader.IsDBNull(reader.GetOrdinal("CreatedUtc"))
                            ? DateTime.UtcNow : reader.GetDateTime("CreatedUtc"),
                        isReject: true,
                        rejectReason: reader.IsDBNull(reader.GetOrdinal("LastError"))
                            ? null : reader.GetString("LastError")
                    ));
                }
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetRejectedAcksAsync error: {ex.Number} - {ex.Message}");
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
                  AND tsl.SystemCode = 'FIX_ACK'
                  AND tsl.Status IS NOT NULL AND tsl.Status <> ''";

            if (statusFilter.HasValue)
            {
                sql += statusFilter.Value switch
                {
                    AckStatus.Pending => " AND tsl.Status IN (@StatusNew, @StatusReady)",
                    AckStatus.Sent => " AND tsl.Status = @StatusSent",
                    AckStatus.Failed => " AND tsl.Status = @StatusError",
                    AckStatus.Rejected => " AND tsl.Status = @StatusRejectSent",
                    _ => ""
                };
            }

            sql += " ORDER BY tsl.LastStatusUtc DESC LIMIT @MaxCount;";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey", sessionKey);
                command.Parameters.AddWithValue("@MaxCount", maxCount);
                command.Parameters.AddWithValue("@StatusNew", DbAckStatus.New);
                command.Parameters.AddWithValue("@StatusReady", DbAckStatus.ReadyToAck);
                command.Parameters.AddWithValue("@StatusSent", DbAckStatus.AckSent);
                command.Parameters.AddWithValue("@StatusError", DbAckStatus.AckError);
                command.Parameters.AddWithValue("@StatusRejectSent", DbAckStatus.AckRejectSent);

                using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

                while (await reader.ReadAsync())
                {
                    var statusStr = reader.GetString("Status");

                    var status = statusStr switch
                    {
                        DbAckStatus.New => AckStatus.Pending,
                        DbAckStatus.ReadyToAck => AckStatus.Pending,
                        DbAckStatus.AckSent => AckStatus.Sent,
                        DbAckStatus.AckError => AckStatus.Failed,
                        DbAckStatus.AckRejected => AckStatus.Pending,
                        DbAckStatus.AckRejectSent => AckStatus.Rejected,
                        _ => AckStatus.Pending
                    };

                    DateTime? sentUtc = null;
                    if ((status == AckStatus.Sent || status == AckStatus.Rejected)
                        && !reader.IsDBNull(reader.GetOrdinal("LastStatusUtc")))
                    {
                        sentUtc = reader.GetDateTime("LastStatusUtc");
                    }

                    result.Add(new AckEntry(
                        tradeId: reader.GetInt64("StpTradeId"),
                        sessionKey: reader.GetString("SessionKey"),
                        tradeReportId: reader.IsDBNull(reader.GetOrdinal("TradeReportId"))
                            ? string.Empty : reader.GetString("TradeReportId"),
                        internTradeId: reader.IsDBNull(reader.GetOrdinal("AckInternalTradeId"))
                            ? string.Empty : reader.GetString("AckInternalTradeId"),
                        status: status,
                        createdUtc: reader.IsDBNull(reader.GetOrdinal("CreatedUtc"))
                            ? DateTime.UtcNow : reader.GetDateTime("CreatedUtc"),
                        sentUtc: sentUtc
                    ));
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
                AckStatus.Pending => DbAckStatus.ReadyToAck,
                AckStatus.Sent => DbAckStatus.AckSent,
                AckStatus.Failed => DbAckStatus.AckError,
                AckStatus.Rejected => DbAckStatus.AckRejectSent,
                _ => DbAckStatus.ReadyToAck
            };

            var sql = @"
                UPDATE tradesystemlink
                SET Status = @Status, LastStatusUtc = @LastStatusUtc
                WHERE StpTradeId = @TradeId
                AND SystemCode = 'FIX_ACK';";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
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

        public async Task<AckStatistics> GetStatisticsAsync(string sessionKey)
        {
            var sql = @"
                SELECT 
                    SUM(CASE WHEN tsl.Status IN (@StatusNew, @StatusReady, @StatusAckRejected) THEN 1 ELSE 0 END) AS PendingCount,
                    SUM(CASE WHEN tsl.Status = @StatusSent AND DATE(tsl.LastStatusUtc) = CURDATE() THEN 1 ELSE 0 END) AS SentTodayCount,
                    SUM(CASE WHEN tsl.Status = @StatusError THEN 1 ELSE 0 END) AS FailedCount
                FROM tradesystemlink tsl
                JOIN trade t ON t.StpTradeId = tsl.StpTradeId
                JOIN messagein m ON m.MessageInId = t.MessageInId
                WHERE m.SessionKey = @SessionKey
                AND tsl.SystemCode = 'FIX_ACK';";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey", sessionKey);
                command.Parameters.AddWithValue("@StatusNew", DbAckStatus.New);
                command.Parameters.AddWithValue("@StatusReady", DbAckStatus.ReadyToAck);
                command.Parameters.AddWithValue("@StatusAckRejected", DbAckStatus.AckRejected);
                command.Parameters.AddWithValue("@StatusSent", DbAckStatus.AckSent);
                command.Parameters.AddWithValue("@StatusError", DbAckStatus.AckError);

                using var reader = await command.ExecuteReaderAsync();

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
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@StpTradeId", stpTradeId);
                command.Parameters.AddWithValue("@TimestampUtc", DateTime.UtcNow);
                command.Parameters.AddWithValue("@EventType", eventType);
                command.Parameters.AddWithValue("@SystemCode", systemCode);
                command.Parameters.AddWithValue("@UserId", "FIX_GATEWAY");
                command.Parameters.AddWithValue("@Details", details ?? (object)DBNull.Value);

                await command.ExecuteNonQueryAsync();
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