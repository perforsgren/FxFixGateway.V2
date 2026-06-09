using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using FxFixGateway.Domain.Constants;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using MySql.Data.MySqlClient;

namespace FxFixGateway.Infrastructure.Persistence
{
    public sealed class PostMarkerRepository : IPostMarkerRepository
    {
        private readonly string _connectionString;

        public PostMarkerRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
            _connectionString = connectionString;
        }

        public async Task<IEnumerable<PendingAccept>> GetPendingAcceptsAsync(int maxCount = 100)
        {
            var result = new List<PendingAccept>();

            var sql = @"
                SELECT
                    tsl.StpTradeId         AS TradeId,
                    CAST(mi.SourceMessageKey AS UNSIGNED) AS SeqNo,
                    tsl.CreatedUtc
                FROM tradesystemlink tsl
                INNER JOIN trade t       ON t.StpTradeId   = tsl.StpTradeId
                INNER JOIN messagein mi  ON mi.MessageInId = t.MessageInId
                WHERE tsl.SystemCode = 'POSTMARKER_ACCEPT'
                  AND tsl.Status     = @Status
                ORDER BY tsl.CreatedUtc ASC
                LIMIT @MaxCount;";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Status", DbPostMarkerStatus.ReadyToAccept);
                command.Parameters.AddWithValue("@MaxCount", maxCount);

                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

                while (await reader.ReadAsync())
                {
                    result.Add(new PendingAccept(
                        tradeId: reader.GetInt64("TradeId"),
                        sequenceNo: reader.GetInt32("SeqNo"),
                        createdUtc: reader.IsDBNull(reader.GetOrdinal("CreatedUtc"))
                            ? DateTime.UtcNow : reader.GetDateTime("CreatedUtc")
                    ));
                }
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PostMarkerRepository] GetPendingAcceptsAsync error: {ex.Number} - {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PostMarkerRepository] Unexpected error: {ex.Message}");
            }

            return result;
        }

        public async Task UpdateAcceptStatusAsync(long tradeId, string status, DateTime updatedUtc)
        {
            var sql = @"
                UPDATE tradesystemlink
                SET    Status        = @Status,
                       LastStatusUtc = @UpdatedUtc
                WHERE  StpTradeId  = @TradeId
                  AND  SystemCode  = 'POSTMARKER_ACCEPT';";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Status", status);
                command.Parameters.AddWithValue("@UpdatedUtc", updatedUtc);
                command.Parameters.AddWithValue("@TradeId", tradeId);

                await command.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PostMarkerRepository] UpdateAcceptStatusAsync error: {ex.Number} - {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PostMarkerRepository] Unexpected error: {ex.Message}");
            }
        }

        public async Task InsertWorkflowEventAsync(long tradeId, string systemCode, string eventType, string details)
        {
            var sql = @"
                INSERT INTO tradeworkflowevent
                    (StpTradeId, TimestampUtc, EventType, SystemCode, UserId, Details)
                VALUES
                    (@StpTradeId, @TimestampUtc, @EventType, @SystemCode, @UserId, @Details);";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@StpTradeId", tradeId);
                command.Parameters.AddWithValue("@TimestampUtc", DateTime.UtcNow);
                command.Parameters.AddWithValue("@EventType", eventType);
                command.Parameters.AddWithValue("@SystemCode", systemCode);
                command.Parameters.AddWithValue("@UserId", "POSTMARKER");
                command.Parameters.AddWithValue("@Details", details ?? (object)DBNull.Value);

                await command.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PostMarkerRepository] InsertWorkflowEventAsync error: {ex.Number} - {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PostMarkerRepository] Unexpected error: {ex.Message}");
            }
        }
    }
}