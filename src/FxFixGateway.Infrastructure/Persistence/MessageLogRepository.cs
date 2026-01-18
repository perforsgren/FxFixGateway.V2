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
    /// ADO.NET implementation för att logga FIX-meddelanden till MessageIn-tabell.
    /// </summary>
    public sealed class MessageLogRepository : IMessageLogger
    {
        private readonly string _connectionString;

        public MessageLogRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

            _connectionString = connectionString;
        }

        public async Task LogIncomingAsync(string sessionKey, string msgType, string rawMessage)
        {
            await LogMessageAsync(sessionKey, msgType, rawMessage, MessageDirection.Incoming);
        }

        public async Task LogOutgoingAsync(string sessionKey, string msgType, string rawMessage)
        {
            await LogMessageAsync(sessionKey, msgType, rawMessage, MessageDirection.Outgoing);
        }

        public async Task<IEnumerable<MessageLogEntry>> GetRecentAsync(string sessionKey, int maxCount = 100)
        {
            var result = new List<MessageLogEntry>();

            var sql = @"
                SELECT
                    ReceivedUtc,
                    Direction,
                    MsgType,
                    RawMessage
                FROM MessageIn
                WHERE SessionKey = @SessionKey
                ORDER BY ReceivedUtc DESC
                LIMIT @MaxCount;";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SessionKey", sessionKey);
            command.Parameters.AddWithValue("@MaxCount", maxCount);

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

            while (await reader.ReadAsync())
            {
                var timestamp = reader.GetDateTime(0);
                var direction = Enum.Parse<MessageDirection>(reader.GetString(1));
                var msgType = reader.GetString(2);
                var rawMessage = reader.GetString(3);

                // Summary genereras från msgType (kan göras smartare senare)
                var summary = GetMessageSummary(msgType);

                var entry = new MessageLogEntry(timestamp, direction, msgType, summary, rawMessage);
                result.Add(entry);
            }

            return result;
        }

        private async Task LogMessageAsync(string sessionKey, string msgType, string rawMessage, MessageDirection direction)
        {
            var sql = @"
                INSERT INTO MessageIn
                (SessionKey, MsgType, RawMessage, ReceivedUtc, Direction)
                VALUES
                (@SessionKey, @MsgType, @RawMessage, @ReceivedUtc, @Direction);";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SessionKey", sessionKey);
            command.Parameters.AddWithValue("@MsgType", msgType);
            command.Parameters.AddWithValue("@RawMessage", rawMessage);
            command.Parameters.AddWithValue("@ReceivedUtc", DateTime.UtcNow);
            command.Parameters.AddWithValue("@Direction", direction.ToString());

            await command.ExecuteNonQueryAsync();
        }

        private string GetMessageSummary(string msgType)
        {
            return msgType switch
            {
                "0" => "Heartbeat",
                "A" => "Logon",
                "5" => "Logout",
                "AE" => "TradeCaptureReport",
                "AR" => "TradeCaptureReportAck",
                "8" => "ExecutionReport",
                _ => $"Message Type {msgType}"
            };
        }
    }
}
