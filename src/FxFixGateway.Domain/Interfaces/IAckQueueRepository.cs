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
        /// Hämtar alla pending ACKs från databasen.
        /// </summary>
        Task<IEnumerable<PendingAck>> GetPendingAcksAsync(int maxCount = 100);

        /// <summary>
        /// Uppdaterar status för en ACK efter att den skickats.
        /// </summary>
        Task UpdateAckStatusAsync(long tradeId, AckStatus status, DateTime? sentUtc);

        /// <summary>
        /// Räknar antal pending ACKs för en specifik session.
        /// </summary>
        Task<int> GetPendingCountAsync(string sessionKey);
    }
}
