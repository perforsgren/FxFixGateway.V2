namespace FxFixGateway.Domain.ValueObjects
{
    /// <summary>
    /// Färdigparsad data ur ett SecurityList (35=y)-meddelande.
    /// Byggs i Infrastructure (QuickFixApplication) där QF.Message + dictionary finns tillgänglig.
    /// Skickas till Application-lagret via ISecurityListService.
    /// </summary>
    public sealed class SecurityInstrumentDto
    {
        /// <summary>Tag 48 — Volbrokers unika SecurityID</summary>
        public string? SecurityId { get; init; }

        /// <summary>Tag 55 — Symbol</summary>
        public string? Symbol { get; init; }

        /// <summary>Tag 460 — Product (20=Specific Interest, 19=Standard)</summary>
        public int? Product { get; init; }

        /// <summary>Tag 320 — SecurityReqID (AUTO om auto-push)</summary>
        public string? SecurityReqId { get; init; }

        /// <summary>Tag 893 — LastFragment (Y/N/null)</summary>
        public string? LastFragment { get; init; }

        /// <summary>Tag 600 (första benet) — LegSymbol, ex. "EUR/USD"</summary>
        public string? CurrencyPair { get; init; }

        /// <summary>Tag 560 — SecurityRequestResult (0=OK)</summary>
        public int? SecurityRequestResult { get; init; }

        // Instrument-attribut
        public string? Tenor { get; init; }
        public string? ExpiryDate { get; init; }  // råsträng YYYYMMDD — parsas i Application
        public string? Cut { get; init; }
        public string? Strategy { get; init; }
        public string? Delta { get; init; }
        public string? Strike { get; init; }
        public string? QuoteStyle { get; init; }
        public string? DeltaStyle { get; init; }
        public string? PremiumCcy { get; init; }
        public string? AmountCcy { get; init; }
    }
}