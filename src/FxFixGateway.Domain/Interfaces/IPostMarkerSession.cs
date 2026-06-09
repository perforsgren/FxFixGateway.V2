using System;

namespace FxFixGateway.Domain.Interfaces
{
    /// <summary>
    /// Wrapper for TTL.PostMarker.Client.Session.
    /// </summary>
    public interface IPostMarkerSession : IDisposable
    {
        object Inner { get; }
        void Connect(string username, string password, int reconnectSeconds, bool autoReconnect);
        void StartSubscription();
        void Acknowledge(int sequenceNo);
        void Accept(int sequenceNo, object? metadata);
        void Disconnect();
    }
}