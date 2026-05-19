using FxFixGateway.Domain.Entities;

namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Lagrar och hämtar instrument-katalogen från market_data.market_instruments.
    /// Populeras från inkommande 35=y (SecurityList).
    /// </summary>
    public interface IMarketInstrumentRepository
    {
        /// <summary>Upsert: uppdatera om SecurityId redan finns, annars insert.</summary>
        Task UpsertAsync(MarketInstrument instrument);

        /// <summary>Hämta instrument matchande givet filter (valutapar + produkttyp).</summary>
        Task<IReadOnlyList<MarketInstrument>> GetByFilterAsync(string sessionKey, string currencyPair, int product);

        /// <summary>Markera instrument som prenumererade (is_subscribed = true).</summary>
        Task MarkAsSubscribedAsync(string sessionKey, IEnumerable<string> securityIds);

        /// <summary>Hämta ett specifikt instrument via SecurityID — används för meta-lookup i MarketDataService.</summary>
        Task<MarketInstrument?> GetBySecurityIdAsync(string sessionKey, string securityId);

        /// <summary>
        /// Hämtar tillåtna tenor-koder från fxvol.vol_tenor_def.
        /// Används för att filtrera bort instrument med okänd tenor vid SecurityList-hantering.
        /// </summary>
        Task<IReadOnlySet<string>> GetAllowedTenorCodesAsync();
    }
}