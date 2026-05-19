namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Orchestrerar market data-flödet för en session:
    ///   OnLogon → SecurityListRequest → SecurityList klar → MarketDataRequest
    /// </summary>
    public interface IMarketDataOrchestrator
    {
        /// <summary>Anropas av QuickFixApplication.OnLogon för market data-sessioner.</summary>
        Task OnSessionLoggedOnAsync(string sessionKey);

        /// <summary>
        /// Anropas av SecurityListService när alla initiala instrument är mottagna
        /// och filtrerade. Triggar SendMarketDataRequest (35=V).
        /// </summary>
        Task OnSecurityListReadyAsync(string sessionKey, IReadOnlyList<string> securityIds);
    }
}