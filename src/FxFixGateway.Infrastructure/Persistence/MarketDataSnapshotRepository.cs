using FxFixGateway.Domain.Entities;
using FxFixGateway.Domain.Interfaces;
using MySql.Data.MySqlClient;
using System.Data;

namespace FxFixGateway.Infrastructure.Persistence
{
    /// <summary>
    /// Persisterar MarketDataSnapshot (35=W) i fxvol.market_data_snapshots
    /// och tillhörande entries i fxvol.market_data_entries.
    /// Använder transaction för att säkerställa atomicitet.
    /// </summary>
    public class MarketDataSnapshotRepository : IMarketDataSnapshotRepository
    {
        private readonly string _connectionString;

        public MarketDataSnapshotRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

            _connectionString = connectionString;
        }

        public async Task<bool> IsSubscribedAsync(string sessionKey, string securityId)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM fxvol.market_instruments
                WHERE session_key  = @SessionKey
                  AND security_id  = @SecurityId
                  AND is_subscribed = TRUE;";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey", sessionKey);
                command.Parameters.AddWithValue("@SecurityId", securityId);

                var count = Convert.ToInt64(await command.ExecuteScalarAsync());
                return count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketDataSnapshotRepository] IsSubscribedAsync error: {ex.Message}");
                return false;
            }
        }

        public async Task<long> InsertSnapshotAsync(MarketDataSnapshot snapshot)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Insert snapshot
                const string snapshotSql = @"
                    INSERT INTO fxvol.market_data_snapshots
                        (session_key, security_id, md_req_id, currency_pair, product, raw_payload, received_utc)
                    VALUES
                        (@SessionKey, @SecurityId, @MdReqId, @CurrencyPair, @Product, @RawPayload, @ReceivedUtc);
                    SELECT LAST_INSERT_ID();";

                await using var snapshotCmd = new MySqlCommand(snapshotSql, connection, transaction);
                snapshotCmd.Parameters.AddWithValue("@SessionKey",  snapshot.SessionKey);
                snapshotCmd.Parameters.AddWithValue("@SecurityId",  snapshot.SecurityId);
                snapshotCmd.Parameters.AddWithValue("@MdReqId",     (object?)snapshot.MdReqId    ?? DBNull.Value);
                snapshotCmd.Parameters.AddWithValue("@CurrencyPair",(object?)snapshot.CurrencyPair?? DBNull.Value);
                snapshotCmd.Parameters.AddWithValue("@Product",     (object?)snapshot.Product    ?? DBNull.Value);
                snapshotCmd.Parameters.AddWithValue("@RawPayload",  snapshot.RawPayload);
                snapshotCmd.Parameters.AddWithValue("@ReceivedUtc", snapshot.ReceivedUtc);

                var snapshotId = Convert.ToInt64(await snapshotCmd.ExecuteScalarAsync());

                // Insert entries
                if (snapshot.Entries.Count > 0)
                {
                    const string entrySql = @"
                        INSERT INTO fxvol.market_data_entries
                            (snapshot_id, security_id, md_entry_type, price, size,
                             quote_condition, trade_condition, position_no,
                             originator, trader_id, exec_inst, scope, entry_date, entry_time)
                        VALUES
                            (@SnapshotId, @SecurityId, @MdEntryType, @Price, @Size,
                             @QuoteCondition, @TradeCondition, @PositionNo,
                             @Originator, @TraderId, @ExecInst, @Scope, @EntryDate, @EntryTime);";

                    foreach (var entry in snapshot.Entries)
                    {
                        await using var entryCmd = new MySqlCommand(entrySql, connection, transaction);
                        entryCmd.Parameters.AddWithValue("@SnapshotId",     snapshotId);
                        entryCmd.Parameters.AddWithValue("@SecurityId",     snapshot.SecurityId);
                        entryCmd.Parameters.AddWithValue("@MdEntryType",    entry.MdEntryType);
                        entryCmd.Parameters.AddWithValue("@Price",          (object?)entry.Price          ?? DBNull.Value);
                        entryCmd.Parameters.AddWithValue("@Size",           (object?)entry.Size           ?? DBNull.Value);
                        entryCmd.Parameters.AddWithValue("@QuoteCondition", (object?)entry.QuoteCondition ?? DBNull.Value);
                        entryCmd.Parameters.AddWithValue("@TradeCondition", (object?)entry.TradeCondition ?? DBNull.Value);
                        entryCmd.Parameters.AddWithValue("@PositionNo",     (object?)entry.PositionNo     ?? DBNull.Value);
                        entryCmd.Parameters.AddWithValue("@Originator",     (object?)entry.Originator     ?? DBNull.Value);
                        entryCmd.Parameters.AddWithValue("@TraderId",       (object?)entry.TraderId       ?? DBNull.Value);
                        entryCmd.Parameters.AddWithValue("@ExecInst",       (object?)entry.ExecInst       ?? DBNull.Value);
                        entryCmd.Parameters.AddWithValue("@Scope",          (object?)entry.Scope          ?? DBNull.Value);
                        entryCmd.Parameters.AddWithValue("@EntryDate",      (object?)entry.EntryDate      ?? DBNull.Value);
                        entryCmd.Parameters.AddWithValue("@EntryTime",      entry.EntryTime.HasValue
                            ? (object)entry.EntryTime.Value.ToString(@"hh\:mm\:ss\.fff")
                            : DBNull.Value);

                        await entryCmd.ExecuteNonQueryAsync();
                    }
                }

                await transaction.CommitAsync();
                return snapshotId;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketDataSnapshotRepository] InsertSnapshotAsync error: {ex.Message}");
                throw;
            }
        }
    }
}