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
    /// <summary>
    /// ADO.NET implementation för att hantera ACK-kön.
    /// </summary>
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

            // TODO: Anpassa query till din faktiska Trades-tabell
            var sql = @"
                SELECT 
                    TradeId,
                    SessionKey,
                    TradeReportId,
                    InternTradeId,
                    CreatedUtc
                FROM Trades
                WHERE AckStatus = 'Pending'
                ORDER BY CreatedUtc ASC
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
                    var ack = new PendingAck(
                        tradeId: reader.GetInt64("TradeId"),
                        sessionKey: reader.GetString("SessionKey"),
                        tradeReportId: reader.GetString("TradeReportId"),
                        internTradeId: reader.GetString("InternTradeId"),
                        createdUtc: reader.GetDateTime("CreatedUtc")
                    );
                    result.Add(ack);
                }
            }
            catch (MySqlException)
            {
                // Tabellen kanske inte finns ännu - returnera tom lista
            }

            return result;
        }

        public async Task<IEnumerable<AckEntry>> GetAcksBySessionAsync(string sessionKey, AckStatus? statusFilter = null, int maxCount = 100)
        {
            var result = new List<AckEntry>();

            var sql = @"
                SELECT 
                    TradeId,
                    SessionKey,
                    TradeReportId,
                    InternTradeId,
                    AckStatus,
                    CreatedUtc,
                    SentUtc
                FROM Trades
                WHERE SessionKey = @SessionKey";

            if (statusFilter.HasValue)
            {
                sql += " AND AckStatus = @Status";
            }

            sql += " ORDER BY CreatedUtc DESC LIMIT @MaxCount;";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
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
                    var statusStr = reader.GetString("AckStatus");
                    var status = Enum.TryParse<AckStatus>(statusStr, out var parsed) ? parsed : AckStatus.Pending;

                    var entry = new AckEntry(
                        tradeId: reader.GetInt64("TradeId"),
                        sessionKey: reader.GetString("SessionKey"),
                        tradeReportId: reader.GetString("TradeReportId"),
                        internTradeId: reader.GetString("InternTradeId"),
                        status: status,
                        createdUtc: reader.GetDateTime("CreatedUtc"),
                        sentUtc: reader.IsDBNull("SentUtc") ? null : reader.GetDateTime("SentUtc")
                    );
                    result.Add(entry);
                }
            }
            catch (MySqlException)
            {
                // Tabellen kanske inte finns ännu
            }

            return result;
        }

        public async Task UpdateAckStatusAsync(long tradeId, AckStatus status, DateTime? sentUtc)
        {
            var sql = @"
                UPDATE Trades
                SET AckStatus = @Status, SentUtc = @SentUtc
                WHERE TradeId = @TradeId;";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TradeId", tradeId);
            command.Parameters.AddWithValue("@Status", status.ToString());
            command.Parameters.AddWithValue("@SentUtc", sentUtc.HasValue ? sentUtc.Value : DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<int> GetPendingCountAsync(string sessionKey)
        {
            var sql = @"
                SELECT COUNT(*) 
                FROM Trades 
                WHERE SessionKey = @SessionKey AND AckStatus = 'Pending';";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey", sessionKey);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (MySqlException)
            {
                return 0;
            }
        }

        public async Task<AckStatistics> GetStatisticsAsync(string sessionKey)
        {
            var sql = @"
                SELECT 
                    SUM(CASE WHEN AckStatus = 'Pending' THEN 1 ELSE 0 END) AS PendingCount,
                    SUM(CASE WHEN AckStatus = 'Sent' AND DATE(SentUtc) = CURDATE() THEN 1 ELSE 0 END) AS SentTodayCount,
                    SUM(CASE WHEN AckStatus = 'Failed' THEN 1 ELSE 0 END) AS FailedCount
                FROM Trades
                WHERE SessionKey = @SessionKey;";

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
            catch (MySqlException)
            {
                // Tabellen kanske inte finns ännu
            }

            return AckStatistics.Empty;
        }
    }
}
