using FxFixGateway.Domain.Enums;

namespace FxFixGateway.Domain.ValueObjects
{
    /// <summary>
    /// Färdigparsad data ur ett QuoteRequest (35=R)-meddelande.
    /// Stödjer multi-leg (555=N) — varje leg är en separat QuoteRequestLegDto.
    /// </summary>
    public sealed class QuoteRequestDto
    {
        // ── Top-level ────────────────────────────────────────────────────────
        public string? QuoteReqId  { get; init; }   // tag 131
        public string? SecurityId  { get; init; }   // tag 48
        public string? Symbol      { get; init; }   // tag 55
        public int?    Product     { get; init; }   // tag 460 — 20=Specific Interest
        public string? QuoteStyle  { get; init; }   // tag 691 — VOL / PCT
        public string? DeltaStyle  { get; init; }   // tag 876 — SPOT / FWD

        // ── NoUnderlyings (711) ──────────────────────────────────────────────
        // tag 310: Single Leg, Straddle, Strangle, Risk Reversal, Butterfly, Generic Spread
        public string? StrategyType { get; init; }

        /// <summary>Legs ur repeating group 555.</summary>
        public IReadOnlyList<QuoteRequestLegDto> Legs { get; init; } = Array.Empty<QuoteRequestLegDto>();

        public string RawPayload { get; init; } = string.Empty;
    }

    /// <summary>En leg ur repeating group tag 555 (NoLegs) i ett 35=R.</summary>
    public sealed class QuoteRequestLegDto
    {
        public string?  CurrencyPair     { get; init; }   // tag 600
        public string?  PutOrCall        { get; init; }   // tag 764 — C / P
        public string?  Tenor            { get; init; }   // tag 620
        public string?  ExpiryDate       { get; init; }   // tag 611 — YYYYMMDD
        public string?  Cut              { get; init; }   // tag 598 — NY, TOK, LON
        public decimal? StrikePrice      { get; init; }   // tag 612
        public decimal? LegOrderQty      { get; init; }   // tag 623
        public string?  NotionalCurrency { get; init; }   // tag 622
        public string?  LegSide          { get; init; }   // tag 624 — B / S
        public string?  PremiumCurrency  { get; init; }   // tag 556
        public string?  QuoteStyle       { get; init; }   // ärvt från top-level
        public string?  DeltaStyle       { get; init; }   // ärvt från top-level
    }
}