using FxFixGateway.Domain.Entities;
using FxFixGateway.Domain.Interfaces;
using MySql.Data.MySqlClient;
using System.Data;

namespace FxFixGateway.Infrastructure.Persistence
{
    /// <summary>
    /// Lagrar instrument-katalogen i fxvol.market_instruments.
    /// Upsert-baserad: SecurityId är unik nyckel per session.
    /// </summary>
    public class MarketInstrumentRepository : IMarketInstrumentRepository
    {
        private readonly string _connectionString;

        public MarketInstrumentRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

            _connectionString = connectionString;
        }

        public async Task UpsertAsync(MarketInstrument instrument)
        {
            const string sql = @"
                INSERT INTO fxvol.market_instruments
                    (session_key, security_id, symbol, currency_pair, product, security_req_id,
                     tenor, expiry_date, cut, strategy, delta, strike,
                     quote_style, delta_style, premium_ccy, amount_ccy,
                     is_subscribed, discovered_utc)
                VALUES
                    (@SessionKey, @SecurityId, @Symbol, @CurrencyPair, @Product, @SecurityReqId,
                     @Tenor, @ExpiryDate, @Cut, @Strategy, @Delta, @Strike,
                     @QuoteStyle, @DeltaStyle, @PremiumCcy, @AmountCcy,
                     FALSE, @DiscoveredUtc)
                ON DUPLICATE KEY UPDATE
                    symbol       = VALUES(symbol),
                    currency_pair= VALUES(currency_pair),
                    product      = VALUES(product),
                    tenor        = VALUES(tenor),
                    expiry_date  = VALUES(expiry_date),
                    cut          = VALUES(cut),
                    strategy     = VALUES(strategy),
                    delta        = VALUES(delta),
                    strike       = VALUES(strike),
                    quote_style  = VALUES(quote_style),
                    delta_style  = VALUES(delta_style),
                    premium_ccy  = VALUES(premium_ccy),
                    amount_ccy   = VALUES(amount_ccy),
                    updated_utc  = CURRENT_TIMESTAMP(3);";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey",   instrument.SessionKey);
                command.Parameters.AddWithValue("@SecurityId",   instrument.SecurityId);
                command.Parameters.AddWithValue("@Symbol",       (object?)instrument.Symbol       ?? DBNull.Value);
                command.Parameters.AddWithValue("@CurrencyPair", (object?)instrument.CurrencyPair ?? DBNull.Value);
                command.Parameters.AddWithValue("@Product",      (object?)instrument.Product      ?? DBNull.Value);
                command.Parameters.AddWithValue("@SecurityReqId",(object?)instrument.SecurityReqId?? DBNull.Value);
                command.Parameters.AddWithValue("@Tenor",        (object?)instrument.Tenor        ?? DBNull.Value);
                command.Parameters.AddWithValue("@ExpiryDate",   (object?)instrument.ExpiryDate   ?? DBNull.Value);
                command.Parameters.AddWithValue("@Cut",          (object?)instrument.Cut          ?? DBNull.Value);
                command.Parameters.AddWithValue("@Strategy",     (object?)instrument.Strategy     ?? DBNull.Value);
                command.Parameters.AddWithValue("@Delta",        (object?)instrument.Delta        ?? DBNull.Value);
                command.Parameters.AddWithValue("@Strike",       (object?)instrument.Strike       ?? DBNull.Value);
                command.Parameters.AddWithValue("@QuoteStyle",   (object?)instrument.QuoteStyle   ?? DBNull.Value);
                command.Parameters.AddWithValue("@DeltaStyle",   (object?)instrument.DeltaStyle   ?? DBNull.Value);
                command.Parameters.AddWithValue("@PremiumCcy",   (object?)instrument.PremiumCcy   ?? DBNull.Value);
                command.Parameters.AddWithValue("@AmountCcy",    (object?)instrument.AmountCcy    ?? DBNull.Value);
                command.Parameters.AddWithValue("@DiscoveredUtc", instrument.DiscoveredUtc);

                await command.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketInstrumentRepository] UpsertAsync MySqlException {ex.Number}: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketInstrumentRepository] UpsertAsync error: {ex.Message}");
            }
        }

        public async Task<IReadOnlyList<MarketInstrument>> GetByFilterAsync(
            string sessionKey, string currencyPair, int product)
        {
            var result = new List<MarketInstrument>();

            const string sql = @"
                SELECT id, session_key, security_id, symbol, currency_pair,
                       product, security_req_id, is_subscribed, discovered_utc, updated_utc,
                       tenor, expiry_date, cut, strategy, delta, strike,
                       quote_style, delta_style, premium_ccy, amount_ccy
                FROM fxvol.market_instruments
                WHERE session_key   = @SessionKey
                  AND currency_pair = @CurrencyPair
                  AND product       = @Product;";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey",   sessionKey);
                command.Parameters.AddWithValue("@CurrencyPair", currencyPair);
                command.Parameters.AddWithValue("@Product",      product);

                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
                while (await reader.ReadAsync())
                    result.Add(MapRow(reader));
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketInstrumentRepository] GetByFilterAsync MySqlException {ex.Number}: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketInstrumentRepository] GetByFilterAsync Unexpected error: {ex.Message}");
            }

            return result;
        }

        public async Task MarkAsSubscribedAsync(string sessionKey, IEnumerable<string> securityIds)
        {
            var ids = securityIds.ToList();
            if (ids.Count == 0) return;

            var paramNames = ids.Select((_, i) => $"@Id{i}").ToList();
            var sql = $@"
                UPDATE fxvol.market_instruments
                SET    is_subscribed = TRUE
                WHERE  session_key   = @SessionKey
                  AND  security_id   IN ({string.Join(",", paramNames)});";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey", sessionKey);
                for (int i = 0; i < ids.Count; i++)
                    command.Parameters.AddWithValue(paramNames[i], ids[i]);

                await command.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketInstrumentRepository] MarkAsSubscribedAsync MySqlException {ex.Number}: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketInstrumentRepository] MarkAsSubscribedAsync Unexpected error: {ex.Message}");
            }
        }

        public async Task<MarketInstrument?> GetBySecurityIdAsync(string sessionKey, string securityId)
        {
            const string sql = @"
                SELECT id, session_key, security_id, symbol, currency_pair,
                       product, security_req_id, is_subscribed, discovered_utc, updated_utc,
                       tenor, expiry_date, cut, strategy, delta, strike,
                       quote_style, delta_style, premium_ccy, amount_ccy
                FROM fxvol.market_instruments
                WHERE session_key = @SessionKey
                  AND security_id = @SecurityId
                LIMIT 1;";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey", sessionKey);
                command.Parameters.AddWithValue("@SecurityId", securityId);

                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
                if (await reader.ReadAsync())
                    return MapRow(reader);

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketInstrumentRepository] GetBySecurityIdAsync error: {ex.Message}");
                return null;
            }
        }

        public async Task<ISet<string>> GetAllowedTenorCodesAsync()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            const string sql = "SELECT tenor_code FROM fxvol.vol_tenor_def;";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var code = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(code))
                        result.Add(code);
                }
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketInstrumentRepository] GetAllowedTenorCodesAsync MySqlException {ex.Number}: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketInstrumentRepository] GetAllowedTenorCodesAsync error: {ex.Message}");
            }

            return result;
        }

        private static MarketInstrument MapRow(MySqlDataReader reader) => new()
        {
            Id            = reader.GetInt64("id"),
            SessionKey    = reader.GetString("session_key"),
            SecurityId    = reader.GetString("security_id"),
            Symbol        = reader.IsDBNull(reader.GetOrdinal("symbol"))         ? null : reader.GetString("symbol"),
            CurrencyPair  = reader.IsDBNull(reader.GetOrdinal("currency_pair"))  ? null : reader.GetString("currency_pair"),
            Product       = reader.IsDBNull(reader.GetOrdinal("product"))        ? null : reader.GetInt32("product"),
            SecurityReqId = reader.IsDBNull(reader.GetOrdinal("security_req_id"))? null : reader.GetString("security_req_id"),
            IsSubscribed  = reader.GetBoolean("is_subscribed"),
            DiscoveredUtc = reader.GetDateTime("discovered_utc"),
            UpdatedUtc    = reader.GetDateTime("updated_utc"),
            Tenor         = reader.IsDBNull(reader.GetOrdinal("tenor"))           ? null : reader.GetString("tenor"),
            ExpiryDate    = reader.IsDBNull(reader.GetOrdinal("expiry_date"))     ? null : reader.GetDateTime("expiry_date"),
            Cut           = reader.IsDBNull(reader.GetOrdinal("cut"))             ? null : reader.GetString("cut"),
            Strategy      = reader.IsDBNull(reader.GetOrdinal("strategy"))        ? null : reader.GetString("strategy"),
            Delta         = reader.IsDBNull(reader.GetOrdinal("delta"))           ? null : reader.GetString("delta"),
            Strike        = reader.IsDBNull(reader.GetOrdinal("strike"))          ? null : reader.GetString("strike"),
            QuoteStyle    = reader.IsDBNull(reader.GetOrdinal("quote_style"))     ? null : reader.GetString("quote_style"),
            DeltaStyle    = reader.IsDBNull(reader.GetOrdinal("delta_style"))     ? null : reader.GetString("delta_style"),
            PremiumCcy    = reader.IsDBNull(reader.GetOrdinal("premium_ccy"))     ? null : reader.GetString("premium_ccy"),
            AmountCcy     = reader.IsDBNull(reader.GetOrdinal("amount_ccy"))      ? null : reader.GetString("amount_ccy"),
        };
    }
}