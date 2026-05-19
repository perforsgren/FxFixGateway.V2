using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Hanterar inkommande MarketDataSnapshot (35=W) från Volbroker/TFSICAP.
    /// Filtrerar på prenumererade instrument och persisterar i fxvol.
    /// </summary>
    public interface IMarketDataService
    {
        /// <summary>
        /// Anropas från QuickFixApplication.FromApp när MsgType=W tas emot.
        /// </summary>
        Task HandleMarketDataSnapshotAsync(string sessionKey, MarketDataSnapshotDto dto);
    }
}