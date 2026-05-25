using FxFixGateway.Domain.Entities;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.Services
{
    /// <summary>
    /// Hanterar inkommande SecurityList (35=y) från Volbroker/TFSICAP.
    /// Tar emot färdigparsad SecurityInstrumentDto från Infrastructure.
    /// Filtrerar mot prenumerationskonfiguration och persisterar matchande
    /// instrument i fxvol.market_instruments.
    /// </summary>
    public class SecurityListService : ISecurityListService
    {
        private readonly IMarketSubscriptionRepository _subscriptionRepo;
        private readonly IMarketInstrumentRepository _instrumentRepo;
        private readonly IMarketDataOrchestrator _orchestrator;
        private readonly ILogger<SecurityListService> _logger;

        // TODO: Flytta till konfiguration/DB om fler sessioner tillkommer
        private static readonly HashSet<string> MarketDataSessions = new(StringComparer.OrdinalIgnoreCase)
        {
            "VOLB_FIXHUB_PROD"
        };

        private readonly Dictionary<string, List<string>> _pendingSecurityIds = new();
        private readonly object _lock = new();

        public SecurityListService(
            IMarketSubscriptionRepository subscriptionRepo,
            IMarketInstrumentRepository instrumentRepo,
            IMarketDataOrchestrator orchestrator,
            ILogger<SecurityListService> logger)
        {
            _subscriptionRepo = subscriptionRepo ?? throw new ArgumentNullException(nameof(subscriptionRepo));
            _instrumentRepo = instrumentRepo ?? throw new ArgumentNullException(nameof(instrumentRepo));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleSecurityListAsync(string sessionKey, IReadOnlyList<SecurityInstrumentDto> instruments)
        {
            if (!MarketDataSessions.Contains(sessionKey))
                return;

            if (instruments.Count == 0) return;

            // Kontrollera felresultat (gäller hela meddelandet)
            var first = instruments[0];
            if (first.SecurityRequestResult.HasValue && first.SecurityRequestResult.Value != 0)
            {
                _logger.LogWarning(
                    "[{Session}] SecurityList error: SecurityRequestResult={Result}",
                    sessionKey, first.SecurityRequestResult.Value);
                return;
            }

            var filters = await _subscriptionRepo.GetEnabledSubscriptionsAsync(sessionKey);
            if (filters.Count == 0)
            {
                _logger.LogWarning("[{Session}] No enabled subscriptions — skipping SecurityList", sessionKey);
                return;
            }

            // Hämta tillåtna tenors från vol_tenor_def en gång per batch
            var allowedTenors = await _instrumentRepo.GetAllowedTenorCodesAsync();

            var matchedIds = new List<string>();

            foreach (var dto in instruments)
            {
                if (string.IsNullOrEmpty(dto.SecurityId))
                    continue;

                // Filtrera bort instrument vars tenor inte finns i vol_tenor_def
                // Tenor-filter gäller bara standard vol (product=19).
                // Specific Interest (product=20) är options med expiry-datum, inte tenor-baserade.
                if (dto.Product != 20)
                {
                    if (string.IsNullOrEmpty(dto.Tenor) || !allowedTenors.Contains(dto.Tenor))
                    {
                        _logger.LogDebug(
                            "[{Session}] Tenor filtered out: SecurityId={SecId} Tenor={Tenor}",
                            sessionKey, dto.SecurityId, dto.Tenor);
                        continue;
                    }
                }

                var matchingFilter = filters.FirstOrDefault(f =>
                    f.Product == dto.Product &&
                    string.Equals(f.CurrencyPair, dto.CurrencyPair, StringComparison.OrdinalIgnoreCase));

                if (matchingFilter == null)
                {
                    _logger.LogDebug(
                        "[{Session}] No filter match: SecurityId={SecId} Pair={Pair} Product={Prod}",
                        sessionKey, dto.SecurityId, dto.CurrencyPair, dto.Product);
                    continue;
                }

                var instrument = new MarketInstrument
                {
                    SessionKey    = sessionKey,
                    SecurityId    = dto.SecurityId,
                    Symbol        = dto.Symbol,
                    CurrencyPair  = dto.CurrencyPair,
                    Product       = dto.Product,
                    SecurityReqId = dto.SecurityReqId,
                    IsSubscribed  = false,
                    DiscoveredUtc = DateTime.UtcNow,
                    UpdatedUtc    = DateTime.UtcNow,
                    Tenor         = dto.Tenor,
                    ExpiryDate    = ParseDate(dto.ExpiryDate),
                    Cut           = dto.Cut,
                    Strike        = dto.Strike,
                    PremiumCcy    = dto.PremiumCcy,
                    Strategy      = dto.Strategy,
                    Delta         = dto.Delta,
                    AmountCcy     = dto.AmountCcy,
                    QuoteStyle    = dto.QuoteStyle,
                    DeltaStyle    = dto.DeltaStyle
                };

                await _instrumentRepo.UpsertAsync(instrument);
                matchedIds.Add(dto.SecurityId);

                _logger.LogInformation(
                    "[{Session}] Instrument matched: SecurityId={SecId} Pair={Pair} Tenor={Tenor}",
                    sessionKey, dto.SecurityId, dto.CurrencyPair, dto.Tenor);
            }

            // Samla matchade IDs tills SecurityList är komplett
            bool isComplete;
            lock (_lock)
            {
                if (!_pendingSecurityIds.TryGetValue(sessionKey, out var pending))
                {
                    pending = new List<string>();
                    _pendingSecurityIds[sessionKey] = pending;
                }
                pending.AddRange(matchedIds);

                // LastFragment=Y → sista meddelandet. Null = ett instrument per msg (utan fragmentering).
                isComplete = first.LastFragment == "Y" || first.LastFragment == null;
            }

            if (isComplete)
            {
                List<string> allIds;
                lock (_lock)
                {
                    _pendingSecurityIds.TryGetValue(sessionKey, out var pending);
                    allIds = pending?.ToList() ?? matchedIds;
                    _pendingSecurityIds.Remove(sessionKey);
                }

                if (allIds.Count > 0)
                {
                    _logger.LogInformation(
                        "[{Session}] SecurityList complete — {Count} matching instrument(s), notifying orchestrator",
                        sessionKey, allIds.Count);
                    await _orchestrator.OnSecurityListReadyAsync(sessionKey, allIds);
                }
                else
                {
                    _logger.LogWarning("[{Session}] SecurityList complete but no matching instruments found", sessionKey);
                }
            }
        }

        private static DateTime? ParseDate(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            return DateTime.TryParseExact(raw, "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d) ? d : null;
        }
    }
}