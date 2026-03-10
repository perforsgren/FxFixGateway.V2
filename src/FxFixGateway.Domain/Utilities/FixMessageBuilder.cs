using System.Text.RegularExpressions;

namespace FxFixGateway.Domain.Utilities
{
    public static class FixMessageBuilder
    {
        private const char SOH = '\x01';

        // Matches "by <anything>" at the end of a reject reason, e.g. "Manually rejected by P901PEF"
        private static readonly Regex UserIdPattern = new(@"\bby\s+\S+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Builds a FIX 4.4 TradeCaptureReportAck (AR) — Accepted (TrdRptStatus=0).
        /// </summary>
        /// <param name="tradeReportId">Tag 571 — echoed from AE tag 571 (TradeReportID).</param>
        /// <param name="internTradeId">Tag 17 + 818 — our internal trade ID (AckInternalTradeId).</param>
        public static string BuildTradeCaptureReportAck(string tradeReportId, string? internTradeId)
        {
            var body = new System.Text.StringBuilder();

            body.Append($"35=AR{SOH}");

            // 17 = ExecID — our internal trade ID
            if (!string.IsNullOrEmpty(internTradeId))
                body.Append($"17={internTradeId}{SOH}");

            // 571 = TradeReportID — echo from AE tag 571
            body.Append($"571={tradeReportId}{SOH}");

            // 818 = SecondaryTradeReportRefID — our internal trade ID
            if (!string.IsNullOrEmpty(internTradeId))
                body.Append($"818={internTradeId}{SOH}");

            // 939 = TrdRptStatus — 0 = Accepted
            body.Append($"939=0{SOH}");

            return BuildMessage(body.ToString());
        }

        /// <summary>
        /// Builds a FIX 4.4 TradeCaptureReportAck (AR) — Rejected (TrdRptStatus=1).
        /// No internal trade ID is included since the trade never reached the internal system.
        /// Any user ID in the reject reason (e.g. "rejected by P901PEF") is anonymised to "rejected by trader".
        /// </summary>
        /// <param name="tradeReportId">Tag 571 — echoed from AE tag 571 (TradeReportID).</param>
        /// <param name="externalTradeKey">Tag 881 — echoed from AE tag 818 (Volbroker trade ID).</param>
        /// <param name="rejectReason">Tag 58 — free-text reject reason (from tsl.LastError).</param>
        public static string BuildTradeCaptureReportAckReject(
            string tradeReportId,
            string? externalTradeKey,
            string? rejectReason)
        {
            var body = new System.Text.StringBuilder();

            body.Append($"35=AR{SOH}");

            // 17 = ExecID — "0" since no internal trade ID was assigned
            body.Append($"17=0{SOH}");

            // 571 = TradeReportID — echo from AE tag 571
            body.Append($"571={tradeReportId}{SOH}");

            // 881 = SecondaryTradeReportRefID — echo from AE tag 818 (Volbroker trade ID)
            if (!string.IsNullOrEmpty(externalTradeKey))
                body.Append($"881={externalTradeKey}{SOH}");

            // 939 = TrdRptStatus — 1 = Rejected
            body.Append($"939=1{SOH}");

            // 751 = TradeReportRejectReason — 99 = Other (required when 939=1)
            body.Append($"751=99{SOH}");

            // 58 = Text — anonymise any internal user ID before sending externally
            if (!string.IsNullOrEmpty(rejectReason))
            {
                var sanitised = UserIdPattern.Replace(rejectReason, "by trader");
                body.Append($"58={sanitised}{SOH}");
            }

            return BuildMessage(body.ToString());
        }

        private static string BuildMessage(string bodyStr)
        {
            var header = $"8=FIX.4.4{SOH}9={bodyStr.Length}{SOH}";
            var messageWithoutChecksum = header + bodyStr;
            var checksum = CalculateChecksum(messageWithoutChecksum);
            return $"{messageWithoutChecksum}10={checksum:D3}{SOH}";
        }

        /// <summary>
        /// Calculates FIX checksum (sum of all ASCII values modulo 256).
        /// </summary>
        public static int CalculateChecksum(string message)
        {
            var sum = 0;
            foreach (var c in message)
                sum += (byte)c;
            return sum % 256;
        }
    }
}