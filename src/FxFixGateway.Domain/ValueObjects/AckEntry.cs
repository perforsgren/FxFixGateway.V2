using System;
using FxFixGateway.Domain.Enums;

namespace FxFixGateway.Domain.ValueObjects
{
    /// <summary>
    /// Representation av en ACK-post med full information inklusive status.
    /// </summary>
    public sealed class AckEntry : IEquatable<AckEntry>
    {
        public long TradeId { get; }
        public string SessionKey { get; }
        public string TradeReportId { get; }
        public string InternTradeId { get; }
        public AckStatus Status { get; }
        public DateTime CreatedUtc { get; }
        public DateTime? SentUtc { get; }

        public AckEntry(
            long tradeId,
            string sessionKey,
            string tradeReportId,
            string internTradeId,
            AckStatus status,
            DateTime createdUtc,
            DateTime? sentUtc)
        {
            TradeId = tradeId;
            SessionKey = sessionKey ?? string.Empty;
            TradeReportId = tradeReportId ?? string.Empty;
            InternTradeId = internTradeId ?? string.Empty;
            Status = status;
            CreatedUtc = createdUtc;
            SentUtc = sentUtc;
        }

        public bool Equals(AckEntry? other)
        {
            if (other is null) return false;
            return TradeId == other.TradeId;
        }

        public override bool Equals(object? obj) => Equals(obj as AckEntry);
        public override int GetHashCode() => TradeId.GetHashCode();
    }
}