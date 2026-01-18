using System;
using FxFixGateway.Domain.Enums;

namespace FxFixGateway.Application.DTOs
{
    /// <summary>
    /// DTO för att visa pending ACKs i UI.
    /// </summary>
    public sealed class PendingAckDto
    {
        public long TradeId { get; set; }
        public string SessionKey { get; set; } = string.Empty;
        public string TradeReportId { get; set; } = string.Empty;
        public string InternTradeId { get; set; } = string.Empty;
        public AckStatus Status { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? SentUtc { get; set; }

        // Computed properties
        public string StatusDisplay => Status.ToString();
        public string CreatedDisplay => CreatedUtc.ToLocalTime().ToString("HH:mm:ss");
        public string SentDisplay => SentUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "-";
        public int WaitingSeconds => (int)(DateTime.UtcNow - CreatedUtc).TotalSeconds;
    }
}
