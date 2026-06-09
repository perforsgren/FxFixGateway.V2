using System;
using FxFixGateway.Domain.Interfaces;
using TTL.PostMarker.Client;

namespace FxFixGateway.Infrastructure.PostMarker
{
    public sealed class PostMarkerSession : IPostMarkerSession
    {
        private readonly Session _inner;
        private bool _disposed;

        public PostMarkerSession()
        {
            _inner = new Session();
        }

        public void RegisterPayloadHandler(IPostMarkerPayloadHandler handler)
        {
            _inner.AddPayloadListener(new SdkListenerAdapter(handler));
        }

        public void Connect(string username, string password, int reconnectSeconds, bool autoReconnect)
        {
            _inner.Connect(username, password, reconnectSeconds, autoReconnect);
        }

        public void StartSubscription()
        {
            _inner.StartSubscription();
        }

        public void Acknowledge(int sequenceNo)
        {
            _inner.Acknowledge(sequenceNo);
        }

        public void Accept(int sequenceNo, object metadata)
        {
            _inner.Accept(sequenceNo, metadata?.ToString() ?? string.Empty);
        }

        public void Disconnect()
        {
            _inner.Disconnect();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _inner.Disconnect(); } catch { }
        }

        private sealed class SdkListenerAdapter : IPayloadListener
        {
            private readonly IPostMarkerPayloadHandler _handler;

            public SdkListenerAdapter(IPostMarkerPayloadHandler handler)
            {
                _handler = handler;
            }

            public void OnReceivePayload(Session session, Payload payload)
            {
                _handler.OnPayloadReceived(payload.SequenceNumber, payload.Status, payload.XML);
            }
        }
    }
}