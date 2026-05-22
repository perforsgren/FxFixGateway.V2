using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Hanterar inkommande QuoteRequest (35=R).
    /// Sparar requesten och aktiverar prenumerationen för SecurityId
    /// så att efterföljande 35=W-meddelanden inte filtreras bort.
    /// </summary>
    public interface IQuoteRequestService
    {
        /// <summary>
        /// Anropas från QuickFixApplication.FromApp när MsgType=R tas emot.
        /// </summary>
        Task HandleQuoteRequestAsync(string sessionKey, QuoteRequestDto dto);
    }
}