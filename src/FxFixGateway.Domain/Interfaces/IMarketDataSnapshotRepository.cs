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
        Task<long> InsertSnapshotAsync(MarketDataSnapshot snapshot, IReadOnlyList<MarketTrade> trades);

        /// <summary>
        /// Upsertar prisdjupet för ett instrument baserat på en ny 35=W.
        /// Varje position identifieras unikt av (security_id, session_key, md_entry_type, position_no).
        /// </summary>
        Task UpsertBookEntriesAsync(IReadOnlyList<ActiveMarketBookEntry> entries);

        /// <summary>
        /// Tar bort alla book-entries för ett instrument (268=0 — tom market).
        /// </summary>
        Task DeleteBookEntriesAsync(string sessionKey, string securityId);

        /// <summary>
        /// Sparar trade prints (MdEntryType=2) från 35=W i market_trades.
        /// </summary>
        Task InsertTradesAsync(IReadOnlyList<MarketTrade> trades);
    }
}