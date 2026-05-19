using FxFixGateway.Domain.Interfaces;

namespace FxFixGateway.Infrastructure.QuickFix
{
    /// <summary>
    /// Proxy för IMarketDataSubscriber som kan registreras i DI vid startup
    /// och sedan kopplas till den verkliga QuickFixSender när QuickFixEngine
    /// har initialiserat sessionIdMap i InitializeAsync.
    /// </summary>
    public class QuickFixSenderProxy : IMarketDataSubscriber
    {
        private IMarketDataSubscriber? _inner;

        /// <summary>
        /// Anropas av QuickFixEngine.InitializeAsync när sessionIdMap är klar.
        /// </summary>
        public void SetInner(IMarketDataSubscriber inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void SendSecurityListRequest(string sessionKey)
        {
            if (_inner == null)
                throw new InvalidOperationException("QuickFixSenderProxy not initialized — SetInner() has not been called yet.");
            _inner.SendSecurityListRequest(sessionKey);
        }

        public void SendMarketDataRequest(string sessionKey, IEnumerable<string> securityIds)
        {
            if (_inner == null)
                throw new InvalidOperationException("QuickFixSenderProxy not initialized — SetInner() has not been called yet.");
            _inner.SendMarketDataRequest(sessionKey, securityIds);
        }
    }
}