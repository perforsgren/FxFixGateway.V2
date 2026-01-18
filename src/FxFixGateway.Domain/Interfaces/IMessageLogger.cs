using System.Collections.Generic;
using System.Threading.Tasks;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Kontrakt för att logga FIX-meddelanden till persistent storage.
    /// </summary>
    public interface IMessageLogger
    {
        /// <summary>
        /// Loggar ett inkommande meddelande till databasen.
        /// </summary>
        Task LogIncomingAsync(string sessionKey, string msgType, string rawMessage);

        /// <summary>
        /// Loggar ett utgående meddelande till databasen.
        /// </summary>
        Task LogOutgoingAsync(string sessionKey, string msgType, string rawMessage);

        /// <summary>
        /// Hämtar de senaste N meddelandena för en session.
        /// </summary>
        Task<IEnumerable<MessageLogEntry>> GetRecentAsync(string sessionKey, int maxCount = 100);
    }
}
