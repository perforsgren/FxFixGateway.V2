using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Kontrakt för att hantera ACK-kön (trades som väntar på acknowledgment).
    /// </summary>
    public interface IAckQueueRepository
    {
        /// <summary>
        /// Hämtar alla pending ACKs från databasen (READY_TO_ACK only).
        /// </summary>
        Task<IEnumerable<PendingAck>> GetPendingAcksAsync(int maxCount = 100);

        /// <summary>
        /// Hämtar ACKs för en specifik session med valfri status-filtrering.
        /// </summary>
        Task<IEnumerable<AckEntry>> GetAcksBySessionAsync(string sessionKey, AckStatus? statusFilter = null, int maxCount = 100);

        /// <summary>
        /// Uppdaterar status för en ACK efter att den skickats.
        /// </summary>
        Task UpdateAckStatusAsync(long tradeId, AckStatus status, DateTime? sentUtc);

        /// <summary>
        /// Hämtar statistik för ACKs (pending, sent today, failed).
        /// </summary>
        Task<AckStatistics> GetStatisticsAsync(string sessionKey);

        /// <summary>
        /// Skriver ett workflow event till tradeworkflowevent-tabellen.
        /// </summary>
        Task InsertWorkflowEventAsync(long stpTradeId, string systemCode, string eventType, string? details = null);
    }
}
