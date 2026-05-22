using FxFixGateway.Domain.Entities;

namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Persisterar inkommande QuoteRequest (35=R) i fxvol.quote_requests.
    /// </summary>
    public interface IQuoteRequestRepository
    {
        /// <summary>Spara ett QuoteRequest och returnera det genererade ID:t.</summary>
        Task<long> InsertAsync(QuoteRequest request);
    }
}