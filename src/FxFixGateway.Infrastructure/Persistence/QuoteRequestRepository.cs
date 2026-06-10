using FxFixGateway.Domain.Entities;
using FxFixGateway.Domain.Interfaces;
using MySql.Data.MySqlClient;

namespace FxFixGateway.Infrastructure.Persistence
{
    /// <summary>
    /// Persisterar QuoteRequest (35=R) i fxvol.quote_requests.
    /// </summary>
    public class QuoteRequestRepository : IQuoteRequestRepository
    {
        private readonly string _connectionString;

        public QuoteRequestRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

            _connectionString = connectionString;
        }

        public async Task<long> InsertAsync(QuoteRequest request)
        {
            const string sql = @"
                INSERT INTO fxvol.quote_requests
                    (session_key, quote_req_id, security_id, symbol,
                     product, quote_style, delta_style, strategy_type,
                     currency_pair, put_or_call, tenor, expiry_date, cut,
                     strike_price, leg_order_qty, notional_currency, leg_side, premium_currency,
                     raw_payload, received_utc)
                VALUES
                    (@SessionKey, @QuoteReqId, @SecurityId, @Symbol,
                     @Product, @QuoteStyle, @DeltaStyle, @StrategyType,
                     @CurrencyPair, @PutOrCall, @Tenor, @ExpiryDate, @Cut,
                     @StrikePrice, @LegOrderQty, @NotionalCurrency, @LegSide, @PremiumCurrency,
                     @RawPayload, @ReceivedUtc);
                SELECT LAST_INSERT_ID();";

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SessionKey", request.SessionKey);
            command.Parameters.AddWithValue("@QuoteReqId", (object?)request.QuoteReqId ?? DBNull.Value);
            command.Parameters.AddWithValue("@SecurityId", (object?)request.SecurityId ?? DBNull.Value);
            command.Parameters.AddWithValue("@Symbol", (object?)request.Symbol ?? DBNull.Value);
            command.Parameters.AddWithValue("@Product", (object?)request.Product ?? DBNull.Value);
            command.Parameters.AddWithValue("@QuoteStyle", (object?)request.QuoteStyle ?? DBNull.Value);
            command.Parameters.AddWithValue("@DeltaStyle", (object?)request.DeltaStyle ?? DBNull.Value);
            command.Parameters.AddWithValue("@StrategyType", (object?)request.StrategyType ?? DBNull.Value);
            command.Parameters.AddWithValue("@CurrencyPair", (object?)request.CurrencyPair ?? DBNull.Value);
            command.Parameters.AddWithValue("@PutOrCall", (object?)request.PutOrCall ?? DBNull.Value);
            command.Parameters.AddWithValue("@Tenor", (object?)request.Tenor ?? DBNull.Value);
            command.Parameters.AddWithValue("@ExpiryDate", (object?)request.ExpiryDate ?? DBNull.Value);
            command.Parameters.AddWithValue("@Cut", (object?)request.Cut ?? DBNull.Value);
            command.Parameters.AddWithValue("@StrikePrice", (object?)request.StrikePrice ?? DBNull.Value);
            command.Parameters.AddWithValue("@LegOrderQty", (object?)request.LegOrderQty ?? DBNull.Value);
            command.Parameters.AddWithValue("@NotionalCurrency", (object?)request.NotionalCurrency ?? DBNull.Value);
            command.Parameters.AddWithValue("@LegSide", (object?)request.LegSide ?? DBNull.Value);
            command.Parameters.AddWithValue("@PremiumCurrency", (object?)request.PremiumCurrency ?? DBNull.Value);
            command.Parameters.AddWithValue("@RawPayload", request.RawPayload);
            command.Parameters.AddWithValue("@ReceivedUtc", request.ReceivedUtc);

            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
    }
}