using System;

namespace FxFixGateway.Domain.Interfaces
{
    public interface IPostMarkerSession : IDisposable
    {
        void RegisterPayloadHandler(IPostMarkerPayloadHandler handler);
        void Connect(string username, string password, int reconnectSeconds, bool autoReconnect);
        void StartSubscription();
        void Acknowledge(int sequenceNo);
        void Accept(int sequenceNo, object? metadata);
        void Disconnect();
    }
}