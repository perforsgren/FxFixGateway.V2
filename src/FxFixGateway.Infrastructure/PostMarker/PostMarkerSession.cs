using System;
using System.Net;
using FxFixGateway.Domain.Interfaces;
using TTL.PostMarker.Client;

namespace FxFixGateway.Infrastructure.PostMarker
{
    public sealed class PostMarkerSession : IPostMarkerSession
    {
        private readonly Session _inner;
        private bool _disposed;

        private const string ProxyAddress = "http://proxyvip.foreningssparbanken.se:8080";

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
            // PostMarker kräver proxy men QuickFIX ska köra direct.
            // App.config har ingen <system.net>-sektion — proxy sätts här precis
            // innan PostMarker kopplar och gäller sedan för hela processen.
            WebRequest.DefaultWebProxy = new WebProxy(ProxyAddress)
            {
                UseDefaultCredentials = true
            };

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