using System;
using FxFixGateway.Application.DTOs;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Application.Mappers
{
    /// <summary>
    /// Mappar mellan PendingAck (domain) och PendingAckDto (UI).
    /// </summary>
    public static class AckMapper
    {
        public static PendingAckDto ToDto(PendingAck ack, AckStatus status, DateTime? sentUtc = null)
        {
            if (ack == null)
                throw new ArgumentNullException(nameof(ack));

            return new PendingAckDto
            {
                TradeId = ack.TradeId,
                SessionKey = ack.SessionKey,
                TradeReportId = ack.TradeReportId,
                InternTradeId = ack.InternTradeId,
                Status = status,
                CreatedUtc = ack.CreatedUtc,
                SentUtc = sentUtc
            };
        }
    }
}
