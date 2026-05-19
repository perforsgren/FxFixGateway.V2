namespace FxFixGateway.Domain.Entities
{
    /// <summary>
    /// En parsad MDEntry ur en MarketDataSnapshot.
    /// Sparas i fxvol.market_data_entries.
    /// 
    /// MDEntryType (tag 269):
    ///   0 = Bid
    ///   1 = Offer  
    ///   2 = Trade
    /// </summary>
    public class MarketDataEntry
    {
        public long Id { get; set; }
        public long SnapshotId { get; set; }
        public string SecurityId { get; set; } = string.Empty;
        public string MdEntryType { get; set; } = string.Empty;  // tag 269
        public decimal? Price { get; set; }                       // tag 270 (volatilitet)
        public decimal? Size { get; set; }                        // tag 271 (miljoner)
        public string? QuoteCondition { get; set; }               // tag 276 (A/I/G/C)
        public string? TradeCondition { get; set; }               // tag 277 (G/P)
        public int? PositionNo { get; set; }                      // tag 290
        public string? Originator { get; set; }                   // tag 282
        public string? TraderId { get; set; }                     // tag 9536
        public string? ExecInst { get; set; }                     // tag 18 (0/1)
        public string? Scope { get; set; }                        // tag 546 (0/1/2)
        public DateTime? EntryDate { get; set; }                  // tag 272 (trades)
        public TimeSpan? EntryTime { get; set; }                  // tag 273 (trades)
    }
}