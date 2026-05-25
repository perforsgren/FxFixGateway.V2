namespace FxFixGateway.Domain.Entities
{
    /// <summary>
    /// Representerar en inkommande MarketDataSnapshot (35=W).
    /// Sparas i fxvol.market_data_snapshots med tillhörande entries.
    /// </summary>
    public class MarketDataSnapshot
    {
        public long Id { get; set; }
        public string SessionKey { get; set; } = string.Empty;
        public string SecurityId { get; set; } = string.Empty;   // tag 48
        public string? MdReqId { get; set; }                      // tag 262
        public string? CurrencyPair { get; set; }                 // denormaliserat från market_instruments
        public int? Product { get; set; }                         // denormaliserat från market_instruments
        public string? Tenor { get; set; }                        // från market_instruments (tag 620)
        public string? Cut { get; set; }                          // från market_instruments (tag 598)
        public string? Strategy { get; set; }                     // från market_instruments (tag 310)
        public string? Delta { get; set; }                        // från market_instruments (tag 763)
        public string RawPayload { get; set; } = string.Empty;
        public DateTime ReceivedUtc { get; set; }

        public List<MarketDataEntry> Entries { get; set; } = new();

        public int EntryCount { get; set; }
    }
}