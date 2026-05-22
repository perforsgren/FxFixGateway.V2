namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Notifierar heartbeat-tjänsten om att en session gått online eller offline.
    /// Implementeras av GatewayHeartbeatService i Application-lagret.
    /// Används av Infrastructure (QuickFixApplication) utan cirkulär referens.
    /// </summary>
    public interface ISessionHeartbeatNotifier
    {
        void SessionOnline(string sessionKey);
        void SessionOffline(string sessionKey);
    }
}