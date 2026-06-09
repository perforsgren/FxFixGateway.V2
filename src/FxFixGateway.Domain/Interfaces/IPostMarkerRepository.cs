using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Domain.Interfaces
{
    public interface IPostMarkerRepository
    {
        Task<IEnumerable<PendingAccept>> GetPendingAcceptsAsync(int maxCount = 100);
        Task UpdateAcceptStatusAsync(long tradeId, string status, DateTime updatedUtc);
        Task InsertWorkflowEventAsync(long tradeId, string systemCode, string eventType, string details);
    }
}