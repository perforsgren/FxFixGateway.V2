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
    // TODO: ACK-FLÖDET ÄR INTE IMPLEMENTERAT ÄNNU
    // ============================================================================
    // 
    // För att aktivera ACK-funktionaliteten behöver följande göras:
    //
    // 1. SKAPA TRADES-TABELLEN I DATABASEN:
    //    ---------------------------------
    //    CREATE TABLE IF NOT EXISTS Trades (
    //        TradeId BIGINT AUTO_INCREMENT PRIMARY KEY,
    //        SessionKey VARCHAR(50) NOT NULL,
    //        TradeReportId VARCHAR(100) NOT NULL,
    //        InternTradeId VARCHAR(100) NOT NULL,
    //        AckStatus VARCHAR(20) NOT NULL DEFAULT 'Pending',
    //        CreatedUtc DATETIME NOT NULL,
    //        SentUtc DATETIME NULL,
    //        INDEX idx_session_status (SessionKey, AckStatus),
    //        INDEX idx_created (CreatedUtc)
    //    );
    //
    // 2. AKTIVERA ACK POLLING SERVICE:
    //    ------------------------------
    //    I AckPollingService.cs, sätt ENABLE_ACK_POLLING = true
    //
    // 3. INTEGRERA MED FxTradeHub:
    //    -------------------------
    //    I MessageProcessingService.cs, implementera ProcessTradeCaptureReportAsync()
    //    för att normalisera AE-meddelanden och spara till Trades-tabellen
    //
    // 4. TESTA FLÖDET:
    //    -------------
    //    - AE kommer in → sparas som Pending i Trades
    //    - AckPollingService pollar → skickar AR → uppdaterar till Sent
    //    - UI visar ACKs i "ACKs"-tabben
    //
    // ============================================================================

    public sealed class AckQueueRepository : IAckQueueRepository
    {
        private readonly string _connectionString;
        private static bool _tableExistsChecked = false;
        private static bool _tableExists = false;

        public AckQueueRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

            _connectionString = connectionString;
        }

        public async Task<IEnumerable<PendingAck>> GetPendingAcksAsync(int maxCount = 100)
        {
            if (!await CheckTableExistsAsync()) return Array.Empty<PendingAck>();

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
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetPendingAcksAsync error: {ex.Number} - {ex.Message}");
            }

            return result;
        }

        public async Task<IEnumerable<AckEntry>> GetAcksBySessionAsync(string sessionKey, AckStatus? statusFilter = null, int maxCount = 100)
        {
            if (!await CheckTableExistsAsync()) return Array.Empty<AckEntry>();

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
            if (!await CheckTableExistsAsync()) return;

            var sql = @"
                UPDATE Trades
                SET AckStatus = @Status, SentUtc = @SentUtc
                WHERE TradeId = @TradeId;";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TradeId", tradeId);
                command.Parameters.AddWithValue("@Status", status.ToString());
                command.Parameters.AddWithValue("@SentUtc", sentUtc.HasValue ? sentUtc.Value : DBNull.Value);

                await command.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] UpdateAckStatusAsync error: {ex.Number} - {ex.Message}");
            }
        }

        public async Task<int> GetPendingCountAsync(string sessionKey)
        {
            if (!await CheckTableExistsAsync()) return 0;

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
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetPendingCountAsync error: {ex.Number} - {ex.Message}");
                return 0;
            }
        }

        public async Task<AckStatistics> GetStatisticsAsync(string sessionKey)
        {
            if (!await CheckTableExistsAsync()) return AckStatistics.Empty;

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
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] GetStatisticsAsync error: {ex.Number} - {ex.Message}");
            }

            return AckStatistics.Empty;
        }

        /// <summary>
        /// Kontrollerar om Trades-tabellen finns. Cachar resultatet.
        /// </summary>
        private async Task<bool> CheckTableExistsAsync()
        {
            if (_tableExistsChecked) return _tableExists;

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(
                    "SELECT 1 FROM information_schema.tables WHERE table_name = 'Trades' LIMIT 1;", 
                    connection);

                var result = await command.ExecuteScalarAsync();
                _tableExists = result != null;
                _tableExistsChecked = true;

                if (!_tableExists)
                {
                    System.Diagnostics.Debug.WriteLine("[AckQueueRepository] Trades table does not exist - ACK features disabled. See TODO in this file.");
                }
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueRepository] CheckTableExistsAsync error: {ex.Number} - {ex.Message}");
                _tableExistsChecked = true;
                _tableExists = false;
            }

            return _tableExists;
        }
    }
}
