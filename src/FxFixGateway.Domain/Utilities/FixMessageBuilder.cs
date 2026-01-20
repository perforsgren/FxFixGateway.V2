namespace FxFixGateway.Domain.Utilities
{
    public static class FixMessageBuilder
    {
        private const char SOH = '\x01';

        /// <summary>
        /// Builds a FIX 4.4 TradeCaptureReportAck (AR) message.
        /// </summary>
        public static string BuildTradeCaptureReportAck(string tradeReportId, string? internTradeId)
        {
            var body = new System.Text.StringBuilder();

            // 35 = MsgType (AR = TradeCaptureReportAck)
            body.Append($"35=AR{SOH}");

            // 571 = TradeReportID (från ursprungliga AE)
            body.Append($"571={tradeReportId}{SOH}");

            // 939 = TrdRptStatus (0 = Accepted)
            body.Append($"939=0{SOH}");

            // 818 = SecondaryTradeReportID (vårt interna trade ID)
            if (!string.IsNullOrEmpty(internTradeId))
            {
                body.Append($"818={internTradeId}{SOH}");
            }

            var bodyStr = body.ToString();
            var bodyLength = bodyStr.Length;

            // Bygg header
            var header = $"8=FIX.4.4{SOH}9={bodyLength}{SOH}";

            // Beräkna checksum
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
            {
                sum += (byte)c;
            }
            return sum % 256;
        }
    }
}