using System;
using FxFixGateway.Application.DTOs;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Application.Mappers
{
    /// <summary>
    /// Mappar mellan MessageLogEntry (domain) och MessageLogDto (UI).
    /// </summary>
    public static class MessageLogMapper
    {
        public static MessageLogDto ToDto(MessageLogEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            return new MessageLogDto
            {
                Timestamp = entry.Timestamp,
                Direction = entry.Direction,
                MsgType = entry.MsgType,
                Summary = entry.Summary,
                RawText = entry.RawText
            };
        }
    }
}
