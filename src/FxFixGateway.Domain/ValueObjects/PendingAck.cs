using System;

namespace FxFixGateway.Domain.ValueObjects
{
    /// <summary>
    /// Immutable representation av en trade som väntar på ACK.
    /// </summary>
    public sealed class PendingAck : IEquatable<PendingAck>
    {
        public long TradeId { get; }
        public string SessionKey { get; }
        public string TradeReportId { get; }
        public string InternTradeId { get; }
        public DateTime CreatedUtc { get; }

        public PendingAck(
            long tradeId,
            string sessionKey,
            string tradeReportId,
            string internTradeId,
            DateTime createdUtc)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
                throw new ArgumentException("SessionKey cannot be null or empty.", nameof(sessionKey));

            if (string.IsNullOrWhiteSpace(tradeReportId))
                throw new ArgumentException("TradeReportId cannot be null or empty.", nameof(tradeReportId));

            if (string.IsNullOrWhiteSpace(internTradeId))
                throw new ArgumentException("InternTradeId cannot be null or empty.", nameof(internTradeId));

            TradeId = tradeId;
            SessionKey = sessionKey;
            TradeReportId = tradeReportId;
            InternTradeId = internTradeId;
            CreatedUtc = createdUtc;
        }

        public bool Equals(PendingAck? other)
        {
            if (other is null) return false;
            return TradeId == other.TradeId;
        }

        public override bool Equals(object? obj) => Equals(obj as PendingAck);

        public override int GetHashCode() => TradeId.GetHashCode();

        public override string ToString()
            => $"ACK for Trade {TradeId}: {TradeReportId} → {InternTradeId}";
    }
}
