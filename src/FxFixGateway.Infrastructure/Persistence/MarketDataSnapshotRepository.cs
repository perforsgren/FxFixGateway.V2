using FxFixGateway.Domain.Entities;
using FxFixGateway.Domain.Interfaces;
using MySql.Data.MySqlClient;
using System.Data;

namespace FxFixGateway.Infrastructure.Persistence
{
    /// <summary>
    /// Persisterar MarketDataSnapshot (35=W) i fxvol.market_data_snapshots
    /// och tillhörande entries i fxvol.market_data_entries.
    /// Upsertar även prisdjupet i fxvol.active_market_book.
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
                WHERE session_key   = @SessionKey
                  AND security_id   = @SecurityId
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
                const string snapshotSql = @"
                    INSERT INTO fxvol.market_data_snapshots
                        (session_key, security_id, md_req_id, currency_pair, product, raw_payload, received_utc, entry_count)
                    VALUES
                        (@SessionKey, @SecurityId, @MdReqId, @CurrencyPair, @Product, @RawPayload, @ReceivedUtc, @EntryCount);
                    SELECT LAST_INSERT_ID();";

                await using var snapshotCmd = new MySqlCommand(snapshotSql, connection, transaction);
                snapshotCmd.Parameters.AddWithValue("@SessionKey",   snapshot.SessionKey);
                snapshotCmd.Parameters.AddWithValue("@SecurityId",   snapshot.SecurityId);
                snapshotCmd.Parameters.AddWithValue("@MdReqId",      (object?)snapshot.MdReqId      ?? DBNull.Value);
                snapshotCmd.Parameters.AddWithValue("@CurrencyPair", (object?)snapshot.CurrencyPair ?? DBNull.Value);
                snapshotCmd.Parameters.AddWithValue("@Product",      (object?)snapshot.Product      ?? DBNull.Value);
                snapshotCmd.Parameters.AddWithValue("@RawPayload",   snapshot.RawPayload);
                snapshotCmd.Parameters.AddWithValue("@ReceivedUtc",  snapshot.ReceivedUtc);
                snapshotCmd.Parameters.AddWithValue("@EntryCount",   snapshot.Entries.Count);

                var snapshotId = Convert.ToInt64(await snapshotCmd.ExecuteScalarAsync());

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
                        entryCmd.Parameters.AddWithValue("@QuoteCondition", (object?)entry.QuoteCondition?.Truncate(50) ?? DBNull.Value);
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

        public async Task UpsertBookEntriesAsync(IReadOnlyList<ActiveMarketBookEntry> entries)
        {
            if (entries.Count == 0)
                return;

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Steg 1: Soft-delete ALLA befintliga rader per (session_key, security_id).
                // Upserten i steg 2 återaktiverar de som faktiskt finns i snapshoten.
                // Detta hanterar fallet där ett pris försvinner ur en snapshot som
                // fortfarande har andra entries (268 > 0 men priset saknas).
                var groups = entries
                    .GroupBy(e => (e.SessionKey, e.SecurityId))
                    .ToList();

                const string deactivateSql = @"
                    UPDATE fxvol.active_market_book
                    SET    is_active   = 0,
                           updated_utc = @UpdatedUtc
                    WHERE  session_key = @SessionKey
                      AND  security_id = @SecurityId;";

                foreach (var group in groups)
                {
                    await using var deactivateCmd = new MySqlCommand(deactivateSql, connection, transaction);
                    deactivateCmd.Parameters.AddWithValue("@SessionKey",  group.Key.SessionKey);
                    deactivateCmd.Parameters.AddWithValue("@SecurityId",  group.Key.SecurityId);
                    deactivateCmd.Parameters.AddWithValue("@UpdatedUtc",  DateTime.UtcNow);
                    await deactivateCmd.ExecuteNonQueryAsync();
                }

                // Steg 2: Upsert aktiva entries — sätter is_active=1 för de som finns i snapshoten.
                const string upsertSql = @"
                    INSERT INTO fxvol.active_market_book
                        (security_id, session_key, currency_pair, md_entry_type, position_no,
                         price, size, originator, trader_id, quote_condition, is_active, snapshot_id, updated_utc)
                    VALUES
                        (@SecurityId, @SessionKey, @CurrencyPair, @MdEntryType, @PositionNo,
                         @Price, @Size, @Originator, @TraderId, @QuoteCondition, 1, @SnapshotId, @UpdatedUtc)
                    ON DUPLICATE KEY UPDATE
                        price           = VALUES(price),
                        size            = VALUES(size),
                        originator      = VALUES(originator),
                        trader_id       = VALUES(trader_id),
                        quote_condition = VALUES(quote_condition),
                        currency_pair   = VALUES(currency_pair),
                        is_active       = 1,
                        snapshot_id     = VALUES(snapshot_id),
                        updated_utc     = VALUES(updated_utc);";

                foreach (var entry in entries)
                {
                    await using var upsertCmd = new MySqlCommand(upsertSql, connection, transaction);
                    upsertCmd.Parameters.AddWithValue("@SecurityId",     entry.SecurityId);
                    upsertCmd.Parameters.AddWithValue("@SessionKey",     entry.SessionKey);
                    upsertCmd.Parameters.AddWithValue("@CurrencyPair",   (object?)entry.CurrencyPair   ?? DBNull.Value);
                    upsertCmd.Parameters.AddWithValue("@MdEntryType",    entry.MdEntryType);
                    upsertCmd.Parameters.AddWithValue("@PositionNo",     entry.PositionNo);
                    upsertCmd.Parameters.AddWithValue("@Price",          (object?)entry.Price          ?? DBNull.Value);
                    upsertCmd.Parameters.AddWithValue("@Size",           (object?)entry.Size           ?? DBNull.Value);
                    upsertCmd.Parameters.AddWithValue("@Originator",     (object?)entry.Originator     ?? DBNull.Value);
                    upsertCmd.Parameters.AddWithValue("@TraderId",       (object?)entry.TraderId       ?? DBNull.Value);
                    upsertCmd.Parameters.AddWithValue("@QuoteCondition", (object?)entry.QuoteCondition?.Truncate(50) ?? DBNull.Value);
                    upsertCmd.Parameters.AddWithValue("@SnapshotId",     entry.SnapshotId);
                    upsertCmd.Parameters.AddWithValue("@UpdatedUtc",     entry.UpdatedUtc);
                    await upsertCmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task DeleteBookEntriesAsync(string sessionKey, string securityId)
        {
            const string sql = @"
                UPDATE fxvol.active_market_book
                SET    is_active   = 0,
                       updated_utc = @UpdatedUtc
                WHERE  session_key = @SessionKey
                  AND  security_id = @SecurityId;";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@SessionKey",  sessionKey);
            cmd.Parameters.AddWithValue("@SecurityId",  securityId);
            cmd.Parameters.AddWithValue("@UpdatedUtc",  DateTime.UtcNow);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task InsertTradesAsync(IReadOnlyList<MarketTrade> trades)
        {
            if (trades.Count == 0)
                return;

            const string sql = @"
                INSERT INTO fxvol.market_trades
                    (security_id, session_key, currency_pair, tenor, cut, strategy, delta,
                     price, size, trade_date, trade_time, trade_condition, snapshot_id, received_utc)
                VALUES
                    (@SecurityId, @SessionKey, @CurrencyPair, @Tenor, @Cut, @Strategy, @Delta,
                     @Price, @Size, @TradeDate, @TradeTime, @TradeCondition, @SnapshotId, @ReceivedUtc);";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            foreach (var trade in trades)
            {
                await using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@SecurityId",     trade.SecurityId);
                cmd.Parameters.AddWithValue("@SessionKey",     trade.SessionKey);
                cmd.Parameters.AddWithValue("@CurrencyPair",   (object?)trade.CurrencyPair   ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Tenor",          (object?)trade.Tenor          ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Cut",            (object?)trade.Cut            ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Strategy",       (object?)trade.Strategy       ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Delta",          (object?)trade.Delta          ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Price",          (object?)trade.Price          ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Size",           (object?)trade.Size           ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TradeDate",      trade.TradeDate.HasValue
                    ? (object)trade.TradeDate.Value.ToString("yyyy-MM-dd") : DBNull.Value);
                cmd.Parameters.AddWithValue("@TradeTime",      trade.TradeTime.HasValue
                    ? (object)trade.TradeTime.Value.ToString("HH:mm:ss.fff") : DBNull.Value);
                cmd.Parameters.AddWithValue("@TradeCondition", (object?)trade.TradeCondition ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SnapshotId",     trade.SnapshotId);
                cmd.Parameters.AddWithValue("@ReceivedUtc",    trade.ReceivedUtc);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    internal static class StringExtensions
    {
        public static string Truncate(this string value, int maxLength)
            => value.Length <= maxLength ? value : value[..maxLength];
    }
}