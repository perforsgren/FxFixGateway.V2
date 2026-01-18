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
    /// ADO.NET implementation för ACK-kö.
    /// Läser från Trades-tabell där AckStatus='Pending'.
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

            var sql = @"
                SELECT
                    TradeId,
                    SessionKey,
                    TradeReportId,
                    InternTradeId,
                    CreatedUtc
                FROM Trades
                WHERE AckStatus = 'Pending'
                ORDER BY CreatedUtc
                LIMIT @MaxCount;";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@MaxCount", maxCount);

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

            while (await reader.ReadAsync())
            {
                var tradeId = reader.GetInt64(0);
                var sessionKey = reader.GetString(1);
                var tradeReportId = reader.GetString(2);
                var internTradeId = reader.GetString(3);
                var createdUtc = reader.GetDateTime(4);

                var ack = new PendingAck(tradeId, sessionKey, tradeReportId, internTradeId, createdUtc);
                result.Add(ack);
            }

            return result;
        }

        public async Task UpdateAckStatusAsync(long tradeId, AckStatus status, DateTime? sentUtc)
        {
            var sql = @"
                UPDATE Trades
                SET
                    AckStatus = @Status,
                    AckSentUtc = @SentUtc
                WHERE TradeId = @TradeId;";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TradeId", tradeId);
            command.Parameters.AddWithValue("@Status", status.ToString());
            command.Parameters.AddWithValue("@SentUtc", (object?)sentUtc ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<int> GetPendingCountAsync(string sessionKey)
        {
            var sql = @"
                SELECT COUNT(*)
                FROM Trades
                WHERE SessionKey = @SessionKey
                  AND AckStatus = 'Pending';";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SessionKey", sessionKey);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
    }
}
