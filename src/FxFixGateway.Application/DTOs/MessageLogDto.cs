using System;
using FxFixGateway.Domain.Enums;

namespace FxFixGateway.Application.DTOs
{
    /// <summary>
    /// DTO för att visa loggade FIX-meddelanden i UI.
    /// </summary>
    public sealed class MessageLogDto
    {
        public DateTime Timestamp { get; set; }
        public MessageDirection Direction { get; set; }
        public string MsgType { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string RawText { get; set; } = string.Empty;

        // Computed properties
        public string DirectionDisplay => Direction == MessageDirection.Incoming ? "IN" : "OUT";
        public string TimestampDisplay => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    }
}
