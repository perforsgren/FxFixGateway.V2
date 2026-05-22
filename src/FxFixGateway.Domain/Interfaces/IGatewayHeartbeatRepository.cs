namespace FxFixGateway.Domain.Interfaces
{
    public interface IGatewayHeartbeatRepository
    {
        Task UpdateBeatAsync(string sessionKey);
        Task SetOfflineAsync(string sessionKey);
    }
}