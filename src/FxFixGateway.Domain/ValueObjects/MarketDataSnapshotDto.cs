using FxFixGateway.Domain.Entities;

namespace FxFixGateway.Domain.ValueObjects
{
    /// <summary>
    /// Färdigparsad data ur ett MarketDataSnapshot (35=W)-meddelande.
    /// Byggs i Infrastructure (QuickFixApplication) där QF.Message + dictionary finns.
    /// </summary>
    public sealed class MarketDataSnapshotDto
    {
        public string? SecurityId { get; init; }      // tag 48
        public string? MdReqId { get; init; }         // tag 262
        public string RawPayload { get; init; } = string.Empty;
        public IReadOnlyList<MarketDataEntryDto> Entries { get; init; } = Array.Empty<MarketDataEntryDto>();
    }

    public sealed class MarketDataEntryDto
    {
        public string? MdEntryType { get; init; }     // tag 269
        public decimal? Price { get; init; }           // tag 270
        public decimal? Size { get; init; }            // tag 271
        public string? QuoteCondition { get; init; }  // tag 276
        public string? TradeCondition { get; init; }  // tag 277
        public int? PositionNo { get; init; }          // tag 290
        public string? Originator { get; init; }       // tag 282
        public string? TraderId { get; init; }         // tag 9536
        public string? ExecInst { get; init; }         // tag 18
        public string? Scope { get; init; }            // tag 546
        public string? EntryDate { get; init; }        // tag 272 (YYYYMMDD)
        public string? EntryTime { get; init; }        // tag 273 (HH:MM:SS.sss)
    }
}