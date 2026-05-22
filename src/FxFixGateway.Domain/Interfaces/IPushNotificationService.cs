using FxFixGateway.Domain.Enums;
using System.Threading.Tasks;

namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Sends push notifications (e.g. via Pushover) on significant gateway events.
    /// </summary>
    public interface IPushNotificationService
    {
        Task SendDisconnectAsync(string sessionKey, DisconnectReason reason);
        Task SendAsync(string title, string message);
    }
}