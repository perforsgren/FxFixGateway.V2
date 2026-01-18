using System.Collections.Generic;
using System.Threading.Tasks;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Domain.Interfaces
{
    public interface ISessionRepository
    {
        Task<IEnumerable<SessionConfiguration>> GetAllAsync();
        Task<SessionConfiguration> GetBySessionKeyAsync(string sessionKey);
        Task<SessionConfiguration> SaveAsync(SessionConfiguration configuration);
        Task DeleteAsync(string sessionKey);
    }
}
