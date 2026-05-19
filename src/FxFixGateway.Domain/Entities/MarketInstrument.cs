namespace FxFixGateway.Domain.Entities
{
    /// <summary>
    /// Instrument upptäckt via SecurityList (35=y).
    /// Sparas i market_data.market_instruments.
    /// SecurityId (tag 48) är Volbrokers unika nyckel och används vid MarketDataRequest.
    /// </summary>
    public class MarketInstrument
    {
        public long Id { get; set; }
        public string SessionKey { get; set; } = string.Empty;
        public string SecurityId { get; set; } = string.Empty;
        public string? Symbol { get; set; }
        public string? CurrencyPair { get; set; }
        public int? Product { get; set; }
        public string? SecurityReqId { get; set; }
        public bool IsSubscribed { get; set; }
        public DateTime DiscoveredUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }

        // Instrument-specifika attribut från SecurityList (35=y)
        public string? Tenor { get; set; }        // tag 620
        public DateTime? ExpiryDate { get; set; } // tag 611
        public string? Cut { get; set; }           // tag 598 (NY/TK/LN...)
        public string? Strategy { get; set; }      // tag 310
        public string? Delta { get; set; }         // tag 763
        public string? Strike { get; set; }        // tag 612 (ATMf/DN/25...)
        public string? QuoteStyle { get; set; }    // tag 691 (VOL/PCT)
        public string? DeltaStyle { get; set; }    // tag 876 (FWD/SPOT)
        public string? PremiumCcy { get; set; }    // tag 556
        public string? AmountCcy { get; set; }     // tag 318
    }
}