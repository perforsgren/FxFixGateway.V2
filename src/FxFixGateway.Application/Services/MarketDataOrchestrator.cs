using FxFixGateway.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.Services
{
    /// <summary>
    /// Orchestrerar market data-flödet för market data-sessioner (VOLB_FIXHUB_*).
    ///
    /// Flöde:
    ///   OnLogon → SendSecurityListRequest (35=x)
    ///   → SecurityListService tar emot 35=y, filtrerar, sparar instrument
    ///   → OnSecurityListReadyAsync → SendMarketDataRequest (35=V)
    ///   → MarketDataService tar emot 35=W (Steg 5)
    /// </summary>
    public class MarketDataOrchestrator : IMarketDataOrchestrator
    {
        private readonly IMarketDataSubscriber _subscriber;
        private readonly IMarketInstrumentRepository _instrumentRepo;
        private readonly ILogger<MarketDataOrchestrator> _logger;

        // Sessioner som hanterar market data-flödet
        // TODO: Flytta till konfiguration/DB om fler sessioner tillkommer
        private static readonly HashSet<string> MarketDataSessions = new(StringComparer.OrdinalIgnoreCase)
        {
            "VOLB_FIXHUB_PROD"
        };

        public MarketDataOrchestrator(
            IMarketDataSubscriber subscriber,
            IMarketInstrumentRepository instrumentRepo,
            ILogger<MarketDataOrchestrator> logger)
        {
            _subscriber     = subscriber     ?? throw new ArgumentNullException(nameof(subscriber));
            _instrumentRepo = instrumentRepo ?? throw new ArgumentNullException(nameof(instrumentRepo));
            _logger         = logger         ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Anropas av QuickFixApplication.OnLogon.
        /// Skickar SecurityListRequest (35=x) för att hämta instrument-katalogen.
        /// </summary>
        public Task OnSessionLoggedOnAsync(string sessionKey)
        {
            if (!MarketDataSessions.Contains(sessionKey))
                return Task.CompletedTask;

            _logger.LogInformation("[{Session}] Logon detected — sending SecurityListRequest (35=x)", sessionKey);
            _subscriber.SendSecurityListRequest(sessionKey);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Anropas av SecurityListService när instrument-katalogen är klar och filtrerad.
        /// Markerar instrument som prenumererade och skickar MarketDataRequest (35=V).
        /// </summary>
        public async Task OnSecurityListReadyAsync(string sessionKey, IReadOnlyList<string> securityIds)
        {
            if (securityIds.Count == 0)
            {
                _logger.LogWarning("[{Session}] SecurityList ready but no matching instruments — skipping MarketDataRequest", sessionKey);
                return;
            }

            _logger.LogInformation(
                "[{Session}] SecurityList ready — {Count} instrument(s) — sending MarketDataRequest (35=V)",
                sessionKey, securityIds.Count);

            // Markera instrument som prenumererade i DB
            await _instrumentRepo.MarkAsSubscribedAsync(sessionKey, securityIds);

            // Skicka MarketDataRequest
            _subscriber.SendMarketDataRequest(sessionKey, securityIds);
        }
    }
}