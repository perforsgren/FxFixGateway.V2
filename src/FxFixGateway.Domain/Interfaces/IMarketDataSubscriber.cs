namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Skickar FIX-meddelanden för market data-prenumeration.
    /// Implementeras i Infrastructure (QuickFixSender).
    /// </summary>
    public interface IMarketDataSubscriber
    {
        /// <summary>Skickar SecurityListRequest (35=x) till venues.</summary>
        void SendSecurityListRequest(string sessionKey);

        /// <summary>Skickar MarketDataRequest (35=V) för angivna SecurityIDs.</summary>
        void SendMarketDataRequest(string sessionKey, IEnumerable<string> securityIds);
    }
}