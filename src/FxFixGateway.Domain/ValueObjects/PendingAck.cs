using System;

namespace FxFixGateway.Domain.ValueObjects
{
    /// <summary>
    /// Immutable representation av en trade som väntar på ACK eller reject-ACK.
    /// </summary>
    public sealed class PendingAck : IEquatable<PendingAck>
    {
        public long TradeId { get; }
        public string SessionKey { get; }

        /// <summary>Tag 571 från original AE (TradeReportID). Ekas alltid i AR tag 571.</summary>
        public string TradeReportId { get; }

        /// <summary>
        /// Tag 818 från original AE (SecondaryTradeReportID = Volbrokers trade-ID).
        /// Ekas i AR tag 881 (SecondaryTradeReportRefID) vid reject.
        /// Sätts från messagein.ExternalTradeKey.
        /// </summary>
        public string ExternalTradeKey { get; }

        /// <summary>
        /// Vårt interna trade-ID (från tsl.AckInternalTradeId).
        /// Skickas som tag 17 och 818 vid accept. Tom vid reject.
        /// </summary>
        public string InternTradeId { get; }

        public DateTime CreatedUtc { get; }

        /// <summary>True om detta är en reject-ACK (TrdRptStatus=1).</summary>
        public bool IsReject { get; }

        /// <summary>
        /// Orsak till reject från tsl.LastError.
        /// Skickas som tag 58 (Text) i AR vid reject.
        /// </summary>
        public string? RejectReason { get; }

        public PendingAck(
            long tradeId,
            string sessionKey,
            string tradeReportId,
            string externalTradeKey,
            string internTradeId,
            DateTime createdUtc,
            bool isReject = false,
            string? rejectReason = null)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
                throw new ArgumentException("SessionKey cannot be null or empty.", nameof(sessionKey));

            if (string.IsNullOrWhiteSpace(tradeReportId))
                throw new ArgumentException("TradeReportId cannot be null or empty.", nameof(tradeReportId));

            TradeId = tradeId;
            SessionKey = sessionKey;
            TradeReportId = tradeReportId;
            ExternalTradeKey = externalTradeKey ?? string.Empty;
            InternTradeId = internTradeId ?? string.Empty;
            CreatedUtc = createdUtc;
            IsReject = isReject;
            RejectReason = rejectReason;
        }

        public bool Equals(PendingAck? other)
        {
            if (other is null) return false;
            return TradeId == other.TradeId;
        }

        public override bool Equals(object? obj) => Equals(obj as PendingAck);
        public override int GetHashCode() => TradeId.GetHashCode();

        public override string ToString()
            => IsReject
                ? $"ACK REJECT for Trade {TradeId}: TradeReportId={TradeReportId}, ExternalTradeKey={ExternalTradeKey}, Reason={RejectReason}"
                : $"ACK ACCEPT for Trade {TradeId}: TradeReportId={TradeReportId} → InternTradeId={InternTradeId}";
    }
}