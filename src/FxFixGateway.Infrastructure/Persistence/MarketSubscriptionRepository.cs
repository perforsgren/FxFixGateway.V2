using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using MySql.Data.MySqlClient;
using System.Data;

namespace FxFixGateway.Infrastructure.Persistence
{
    /// <summary>
    /// Läser prenumerationskonfiguration från fix_config_dev.market_subscriptions.
    /// </summary>
    public class MarketSubscriptionRepository : IMarketSubscriptionRepository
    {
        private readonly string _connectionString;

        public MarketSubscriptionRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

            _connectionString = connectionString;
        }

        public async Task<IReadOnlyList<MarketSubscriptionFilter>> GetEnabledSubscriptionsAsync(string sessionKey)
        {
            var result = new List<MarketSubscriptionFilter>();

            const string sql = @"
                SELECT session_key, currency_pair, product
                FROM fix_config_dev.market_subscriptions
                WHERE session_key = @SessionKey
                  AND is_enabled  = TRUE
                ORDER BY currency_pair, product;";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionKey", sessionKey);

                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

                while (await reader.ReadAsync())
                {
                    result.Add(new MarketSubscriptionFilter(
                        sessionKey:   reader.GetString("session_key"),
                        currencyPair: reader.GetString("currency_pair"),
                        product:      reader.GetInt32("product")));
                }
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketSubscriptionRepository] MySqlException {ex.Number}: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MarketSubscriptionRepository] Unexpected error: {ex.Message}");
            }

            return result;
        }
    }
}