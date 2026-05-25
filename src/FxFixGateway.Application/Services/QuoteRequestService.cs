using FxFixGateway.Domain.Entities;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.Services
{
    public class QuoteRequestService : IQuoteRequestService
    {
        private readonly IQuoteRequestRepository      _quoteRequestRepo;
        private readonly IMarketInstrumentRepository  _instrumentRepo;
        private readonly IMarketSubscriptionRepository _subscriptionRepo;
        private readonly ILogger<QuoteRequestService>  _logger;

        private static readonly HashSet<string> MarketDataSessions = new(StringComparer.OrdinalIgnoreCase)
        {
            "VOLB_FIXHUB_PROD"
        };

        // Cache av tillåtna valutapar per session — laddas en gång, rensas ej (statisk konfiguration)
        private readonly Dictionary<string, HashSet<string>> _allowedPairsCache = new();
        private readonly SemaphoreSlim _cacheLock = new(1, 1);

        public QuoteRequestService(
            IQuoteRequestRepository quoteRequestRepo,
            IMarketInstrumentRepository instrumentRepo,
            IMarketSubscriptionRepository subscriptionRepo,
            ILogger<QuoteRequestService> logger)
        {
            _quoteRequestRepo  = quoteRequestRepo  ?? throw new ArgumentNullException(nameof(quoteRequestRepo));
            _instrumentRepo    = instrumentRepo    ?? throw new ArgumentNullException(nameof(instrumentRepo));
            _subscriptionRepo  = subscriptionRepo  ?? throw new ArgumentNullException(nameof(subscriptionRepo));
            _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleQuoteRequestAsync(string sessionKey, QuoteRequestDto dto)
        {
            _logger.LogInformation(
                "[{Session}] HandleQuoteRequestAsync anropad — SecurityId={SecId} Legs={LegCount}",
                sessionKey, dto.SecurityId, dto.Legs.Count);

            if (!MarketDataSessions.Contains(sessionKey))
            {
                _logger.LogWarning(
                    "[{Session}] 35=R ignoreras — session ej i MarketDataSessions. Tillåtna: {Allowed}",
                    sessionKey, string.Join(", ", MarketDataSessions));
                return;
            }

            if (string.IsNullOrEmpty(dto.SecurityId))
            {
                _logger.LogDebug("[{Session}] 35=R missing SecurityId (tag 48) — skipping", sessionKey);
                return;
            }

            // TODO: TEMPORÄRT — valutaparsfiltret är inaktiverat för product=20 (Specific Interest).
            //       Alla inkommande legs sparas oavsett currency_pair.
            //       Aktivera filtret igen när market_subscriptions är komplett konfigurerad.
            //       Sök efter "TODO: TEMPORÄRT" för att hitta detta ställe igen.
            var filteredLegs = dto.Legs.ToList();

            // ── AKTIVERA DETTA NÄR FILTRET SKA SLÅ PÅ IGEN ──────────────────
            // var allowedPairs = await GetAllowedPairsAsync(sessionKey);
            // var filteredLegs = dto.Legs
            //     .Where(l => l.CurrencyPair != null && allowedPairs.Contains(l.CurrencyPair))
            //     .ToList();
            //
            // if (filteredLegs.Count == 0)
            // {
            //     _logger.LogDebug(
            //         "[{Session}] 35=R SecurityId={SecId} — inga legs matchar prenumererade valutapar ({Pairs}) — skipping",
            //         sessionKey, dto.SecurityId, string.Join(", ", allowedPairs));
            //     return;
            // }
            // ─────────────────────────────────────────────────────────────────

            // Upserta + aktivera subscription per leg
            foreach (var leg in filteredLegs)
                await UpsertAndSubscribeAsync(sessionKey, dto.SecurityId, leg);

            // Spara en rad per leg
            foreach (var leg in filteredLegs)
            {
                var entity = MapToEntity(sessionKey, dto, leg);
                try
                {
                    var id = await _quoteRequestRepo.InsertAsync(entity);
                    _logger.LogDebug(
                        "[{Session}] 35=R saved: Id={Id} SecurityId={SecId} Pair={Pair} Tenor={Tenor} Strike={Strike} PutOrCall={PoC}",
                        sessionKey, id, dto.SecurityId, leg.CurrencyPair, leg.Tenor, leg.StrikePrice, leg.PutOrCall);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[{Session}] Failed to save 35=R for SecurityId={SecId}",
                        sessionKey, dto.SecurityId);
                }
            }
        }

        /// <summary>
        /// Hämtar tillåtna valutapar från market_subscriptions (produkt 20 = Specific Interest).
        /// Cachar resultatet i minnet per session.
        /// </summary>
        private async Task<HashSet<string>> GetAllowedPairsAsync(string sessionKey)
        {
            await _cacheLock.WaitAsync();
            try
            {
                if (_allowedPairsCache.TryGetValue(sessionKey, out var cached))
                    return cached;

                var subs = await _subscriptionRepo.GetEnabledSubscriptionsAsync(sessionKey);
                var pairs = new HashSet<string>(
                    subs.Where(s => s.Product == 20).Select(s => s.CurrencyPair),
                    StringComparer.OrdinalIgnoreCase);

                _allowedPairsCache[sessionKey] = pairs;
                _logger.LogInformation(
                    "[{Session}] Tillåtna valutapar för Specific Interest: {Pairs}",
                    sessionKey, string.Join(", ", pairs));
                return pairs;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private async Task UpsertAndSubscribeAsync(string sessionKey, string securityId, QuoteRequestLegDto? leg)
        {
            try
            {
                DateTime? expiryDate = null;
                if (!string.IsNullOrEmpty(leg?.ExpiryDate) &&
                    DateTime.TryParseExact(leg.ExpiryDate, "yyyyMMdd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var parsed))
                    expiryDate = parsed;

                var instrument = new MarketInstrument
                {
                    SessionKey    = sessionKey,
                    SecurityId    = securityId,
                    CurrencyPair  = leg?.CurrencyPair,
                    Product       = 20,
                    Tenor         = leg?.Tenor,
                    ExpiryDate    = expiryDate,
                    Cut           = leg?.Cut,
                    Strike        = leg?.StrikePrice?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    QuoteStyle    = leg?.QuoteStyle,
                    DeltaStyle    = leg?.DeltaStyle,
                    PremiumCcy    = leg?.PremiumCurrency,
                    AmountCcy     = leg?.NotionalCurrency,
                    DiscoveredUtc = DateTime.UtcNow
                };

                await _instrumentRepo.UpsertAsync(instrument);
                await _instrumentRepo.MarkAsSubscribedAsync(sessionKey, new[] { securityId });

                _logger.LogInformation(
                    "[{Session}] 35=R: is_subscribed=true för SecurityId={SecId} Pair={Pair} Tenor={Tenor}",
                    sessionKey, securityId, leg?.CurrencyPair, leg?.Tenor);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[{Session}] Failed to upsert/subscribe SecurityId={SecId}", sessionKey, securityId);
            }
        }

        private static QuoteRequest MapToEntity(string sessionKey, QuoteRequestDto dto, QuoteRequestLegDto? leg) =>
            new()
            {
                SessionKey       = sessionKey,
                QuoteReqId       = dto.QuoteReqId,
                SecurityId       = dto.SecurityId,
                Symbol           = dto.Symbol,
                Product          = dto.Product,
                QuoteStyle       = dto.QuoteStyle,
                DeltaStyle       = dto.DeltaStyle,
                StrategyType     = dto.StrategyType,
                CurrencyPair     = leg?.CurrencyPair,
                PutOrCall        = leg?.PutOrCall,
                Tenor            = leg?.Tenor,           // ← OD/ON/1M/3M etc. sparas alltid
                ExpiryDate       = leg?.ExpiryDate,
                Cut              = leg?.Cut,
                StrikePrice      = leg?.StrikePrice,
                LegOrderQty      = leg?.LegOrderQty,
                NotionalCurrency = leg?.NotionalCurrency,
                LegSide          = leg?.LegSide,
                PremiumCurrency  = leg?.PremiumCurrency,
                RawPayload       = dto.RawPayload,
                ReceivedUtc      = DateTime.UtcNow
            };
    }
}