using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Läser prenumerationskonfiguration från fix_config_prod.market_subscriptions.
    /// Definierar vilka valutapar och produkttyper som ska prenumereras per session.
    /// </summary>
    public interface IMarketSubscriptionRepository
    {
        Task<IReadOnlyList<MarketSubscriptionFilter>> GetEnabledSubscriptionsAsync(string sessionKey);
    }
}