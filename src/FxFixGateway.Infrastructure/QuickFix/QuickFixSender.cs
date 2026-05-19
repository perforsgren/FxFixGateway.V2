using FxFixGateway.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using QF = global::QuickFix;

namespace FxFixGateway.Infrastructure.QuickFix
{
    /// <summary>
    /// Skickar FIX-meddelanden för market data-prenumeration via QuickFix.
    /// Implementerar IMarketDataSubscriber (definierad i Domain).
    /// </summary>
    public class QuickFixSender : IMarketDataSubscriber
    {
        private readonly Dictionary<string, QF.SessionID> _sessionIdMap;
        private readonly ILogger<QuickFixSender> _logger;

        public QuickFixSender(
            Dictionary<string, QF.SessionID> sessionIdMap,
            ILogger<QuickFixSender> logger)
        {
            _sessionIdMap = sessionIdMap ?? throw new ArgumentNullException(nameof(sessionIdMap));
            _logger       = logger       ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Skickar SecurityListRequest (35=x).
        /// Volbroker svarar med ett eller flera 35=y meddelanden.
        /// Tag 320 (SecurityReqID) måste vara unikt per request.
        /// </summary>
        public void SendSecurityListRequest(string sessionKey)
        {
            if (!_sessionIdMap.TryGetValue(sessionKey, out var sessionId))
            {
                _logger.LogError("[{Session}] SendSecurityListRequest — session not found in map", sessionKey);
                return;
            }

            try
            {
                var msg = new QF.Message();
                msg.Header.SetField(new QF.Fields.MsgType("x"));
                msg.SetField(new QF.Fields.SecurityReqID($"SECLIST-{sessionKey}-{DateTime.UtcNow:yyyyMMddHHmmss}"));

                QF.Session.SendToTarget(msg, sessionId);

                _logger.LogInformation("[{Session}] SecurityListRequest (35=x) sent", sessionKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Session}] Failed to send SecurityListRequest: {Error}", sessionKey, ex.Message);
            }
        }

        /// <summary>
        /// Skickar MarketDataRequest (35=V).
        /// Enligt Volbroker API-guiden är 35=V mycket tunn — bara MDReqID krävs.
        /// Tag 262 (MDReqID) måste vara unikt per request.
        /// </summary>
        public void SendMarketDataRequest(string sessionKey, IEnumerable<string> securityIds)
        {
            if (!_sessionIdMap.TryGetValue(sessionKey, out var sessionId))
            {
                _logger.LogError("[{Session}] SendMarketDataRequest — session not found in map", sessionKey);
                return;
            }

            var ids = securityIds.ToList();
            if (ids.Count == 0)
            {
                _logger.LogWarning("[{Session}] SendMarketDataRequest called with empty securityIds", sessionKey);
                return;
            }

            try
            {
                var msg = new QF.Message();
                msg.Header.SetField(new QF.Fields.MsgType("V"));
                msg.SetField(new QF.Fields.MDReqID($"MD-{sessionKey}-{DateTime.UtcNow:yyyyMMddHHmmss}"));

                QF.Session.SendToTarget(msg, sessionId);

                _logger.LogInformation(
                    "[{Session}] MarketDataRequest (35=V) sent for {Count} instrument(s): [{Ids}]",
                    sessionKey, ids.Count, string.Join(", ", ids.Take(5)) + (ids.Count > 5 ? "..." : ""));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Session}] Failed to send MarketDataRequest: {Error}", sessionKey, ex.Message);
            }
        }
    }
}