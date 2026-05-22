using FxFixGateway.Domain.Enums;

namespace FxFixGateway.Domain.Entities
{
    /// <summary>
    /// Representerar ett inkommande QuoteRequest (35=R).
    /// Sparas i fxvol.quote_requests.
    /// </summary>
    public class QuoteRequest
    {
        public long   Id         { get; set; }
        public string SessionKey { get; set; } = string.Empty;

        // ── Top-level ────────────────────────────────────────────────────────
        public string? QuoteReqId  { get; set; }   // tag 131
        public string? SecurityId  { get; set; }   // tag 48
        public string? Symbol      { get; set; }   // tag 55
        public int?    Product     { get; set; }   // tag 460 — 20=Specific Interest
        public string? QuoteStyle  { get; set; }   // tag 691 — VOL / PCT
        public string? DeltaStyle  { get; set; }   // tag 876 — SPOT / FWD

        // ── NoUnderlyings (711) ──────────────────────────────────────────────
        // tag 310: Single Leg, Straddle, Strangle, Risk Reversal, Butterfly, Generic Spread
        public string? StrategyType { get; set; }

        // ── Leg (555) ────────────────────────────────────────────────────────
        public string?  CurrencyPair     { get; set; }   // tag 600
        public string?  PutOrCall        { get; set; }   // tag 764 — C / P
        public string?  Tenor            { get; set; }   // tag 620
        public string?  ExpiryDate       { get; set; }   // tag 611 — YYYYMMDD
        public string?  Cut              { get; set; }   // tag 598 — NY, TOK, LON
        public decimal? StrikePrice      { get; set; }   // tag 612
        public decimal? LegOrderQty      { get; set; }   // tag 623
        public string?  NotionalCurrency { get; set; }   // tag 622
        public string?  LegSide          { get; set; }   // tag 624 — B / S
        public string?  PremiumCurrency  { get; set; }   // tag 556

        public string   RawPayload  { get; set; } = string.Empty;
        public DateTime ReceivedUtc { get; set; }
    }
}