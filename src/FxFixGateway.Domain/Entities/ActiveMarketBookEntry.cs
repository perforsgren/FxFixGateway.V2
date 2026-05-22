namespace FxFixGateway.Domain.Entities
{
    /// <summary>
    /// Representerar en aktiv position i prisdjupet för ett instrument.
    /// Speglar alltid nuläget – upsertas vid varje 35=W.
    /// Sparas i fxvol.active_market_book.
    /// </summary>
    public class ActiveMarketBookEntry
    {
        public long     Id             { get; set; }
        public string   SecurityId     { get; set; } = string.Empty;
        public string   SessionKey     { get; set; } = string.Empty;
        public string?  CurrencyPair   { get; set; }
        public string   MdEntryType    { get; set; } = string.Empty;  // 0=Bid, 1=Ask
        public int      PositionNo     { get; set; }                   // tag 290
        public decimal? Price          { get; set; }
        public decimal? Size           { get; set; }
        public string?  Originator     { get; set; }
        public string?  TraderId       { get; set; }
        public string?  QuoteCondition { get; set; }                   // A=aktiv, G=borta, I=indikativ
        public long     SnapshotId     { get; set; }
        public DateTime UpdatedUtc     { get; set; }
    }
}