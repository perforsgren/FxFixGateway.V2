using System;

namespace FxFixGateway.Domain.ValueObjects
{
    /// <summary>
    /// Trade waiting for PostMarker Accept call.
    /// </summary>
    public sealed class PendingAccept : IEquatable<PendingAccept>
    {
        public long TradeId { get; }
        public int SequenceNo { get; }
        public DateTime CreatedUtc { get; }

        public PendingAccept(long tradeId, int sequenceNo, DateTime createdUtc)
        {
            TradeId = tradeId;
            SequenceNo = sequenceNo;
            CreatedUtc = createdUtc;
        }

        public bool Equals(PendingAccept? other)
        {
            if (other is null) return false;
            return TradeId == other.TradeId;
        }

        public override bool Equals(object? obj) => Equals(obj as PendingAccept);
        public override int GetHashCode() => TradeId.GetHashCode();

        public override string ToString()
            => $"PostMarker Accept for Trade {TradeId}, SeqNo={SequenceNo}";
    }
}