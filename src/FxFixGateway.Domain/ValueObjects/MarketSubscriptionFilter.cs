namespace FxFixGateway.Domain.ValueObjects
{
    /// <summary>
    /// Representerar ett prenumerationsfilter: ett valutapar + en produkttyp per session.
    /// Läses från fix_config_prod.market_subscriptions.
    /// </summary>
    public sealed class MarketSubscriptionFilter
    {
        public string SessionKey { get; }
        public string CurrencyPair { get; }   // ex. "EUR/USD" — matchar tag 600 i SecurityList
        public int Product { get; }           // 20 = Specific Interest, 19 = Standard

        public MarketSubscriptionFilter(string sessionKey, string currencyPair, int product)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
                throw new ArgumentException("SessionKey cannot be empty.", nameof(sessionKey));
            if (string.IsNullOrWhiteSpace(currencyPair))
                throw new ArgumentException("CurrencyPair cannot be empty.", nameof(currencyPair));

            SessionKey = sessionKey;
            CurrencyPair = currencyPair;
            Product = product;
        }

        public override string ToString() => $"{SessionKey} | {CurrencyPair} | Product={Product}";
    }
}