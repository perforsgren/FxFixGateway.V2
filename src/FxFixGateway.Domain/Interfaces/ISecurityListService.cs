using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Domain.Interfaces
{
    public interface ISecurityListService
    {
        /// <summary>
        /// Anropas från QuickFixApplication.FromApp när MsgType=y tas emot.
        /// En lista skickas eftersom ett 35=y-meddelande kan innehålla
        /// flera instrument (tag 146=NoRelatedSym).
        /// </summary>
        Task HandleSecurityListAsync(string sessionKey, IReadOnlyList<SecurityInstrumentDto> instruments);
    }
}