using System;
using FxFixGateway.Domain.Enums;

namespace FxFixGateway.Domain.ValueObjects
{
    /// <summary>
    /// Immutable representation av ett loggat FIX-meddelande.
    /// </summary>
    public sealed class MessageLogEntry : IEquatable<MessageLogEntry>
    {
        public DateTime Timestamp { get; }
        public MessageDirection Direction { get; }
        public string MsgType { get; }
        public string Summary { get; }
        public string RawText { get; }

        public MessageLogEntry(
            DateTime timestamp,
            MessageDirection direction,
            string msgType,
            string summary,
            string rawText)
        {
            if (string.IsNullOrWhiteSpace(msgType))
                throw new ArgumentException("MsgType cannot be null or empty.", nameof(msgType));

            if (string.IsNullOrWhiteSpace(summary))
                throw new ArgumentException("Summary cannot be null or empty.", nameof(summary));

            Timestamp = timestamp;
            Direction = direction;
            MsgType = msgType;
            Summary = summary;
            RawText = rawText ?? string.Empty;
        }

        public bool Equals(MessageLogEntry? other)
        {
            if (other is null) return false;
            return Timestamp == other.Timestamp &&
                   Direction == other.Direction &&
                   MsgType == other.MsgType &&
                   RawText == other.RawText;
        }

        public override bool Equals(object? obj) => Equals(obj as MessageLogEntry);

        public override int GetHashCode() => HashCode.Combine(Timestamp, Direction, MsgType);

        public override string ToString()
            => $"[{Timestamp:HH:mm:ss}] {Direction} {MsgType} - {Summary}";
    }
}
