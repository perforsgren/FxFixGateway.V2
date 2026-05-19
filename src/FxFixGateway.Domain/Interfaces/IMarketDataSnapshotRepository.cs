using FxFixGateway.Domain.Entities;

namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Persisterar MarketDataSnapshot (35=W) och dess MDEntries
    /// i fxvol.market_data_snapshots + fxvol.market_data_entries.
    /// </summary>
    public interface IMarketDataSnapshotRepository
    {
        /// <summary>
        /// Kontrollera om ett SecurityID är prenumererat (is_subscribed=true).
        /// Används för att filtrera bort icke-prenumererade 35=W-meddelanden.
        /// </summary>
        Task<bool> IsSubscribedAsync(string sessionKey, string securityId);

        /// <summary>
        /// Spara snapshot + entries i ett transaction-scope.
        /// </summary>
        Task<long> InsertSnapshotAsync(MarketDataSnapshot snapshot);
    }
}