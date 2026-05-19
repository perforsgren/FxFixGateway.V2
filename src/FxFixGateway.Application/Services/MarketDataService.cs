using FxFixGateway.Domain.Entities;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.Services
{
    /// <summary>
    /// Hanterar inkommande MarketDataSnapshot (35=W) från Volbroker/TFSICAP.
    ///
    /// Flöde:
    ///   1. Ta emot MarketDataSnapshotDto (parsad i Infrastructure med dictionary)
    ///   2. Filtrera: kontrollera att SecurityID är prenumererat (is_subscribed=true)
    ///   3. Berika med CurrencyPair/Product från market_instruments
    ///   4. Persistera snapshot + entries i fxvol
    ///
    /// TODO: Vid hög volym — flytta DB-skrivning till en bakgrundskö (Channel<T>)
    ///       för att undvika att blockera FIX-tråden.
    /// </summary>
    public class MarketDataService : IMarketDataService
    {
        private readonly IMarketDataSnapshotRepository _snapshotRepo;
        private readonly IMarketInstrumentRepository _instrumentRepo;
        private readonly ILogger<MarketDataService> _logger;

        // Cache av prenumererade SecurityIDs per session för att undvika DB-query per meddelande
        // Populeras vid första träff och rensas vid reconnect (session-scoped)
        private readonly Dictionary<string, HashSet<string>> _subscribedCache = new();
        private readonly Dictionary<string, Dictionary<string, (string? CurrencyPair, int? Product)>> _instrumentCache = new();
        private readonly object _cacheLock = new();

        // TODO: Flytta till konfiguration
        private static readonly HashSet<string> MarketDataSessions = new(StringComparer.OrdinalIgnoreCase)
        {
            "VOLB_FIXHUB_DEV",
            "VOLB_FIXHUB_PROD"
        };

        public MarketDataService(
            IMarketDataSnapshotRepository snapshotRepo,
            IMarketInstrumentRepository instrumentRepo,
            ILogger<MarketDataService> logger)
        {
            _snapshotRepo   = snapshotRepo   ?? throw new ArgumentNullException(nameof(snapshotRepo));
            _instrumentRepo = instrumentRepo ?? throw new ArgumentNullException(nameof(instrumentRepo));
            _logger         = logger         ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleMarketDataSnapshotAsync(string sessionKey, MarketDataSnapshotDto dto)
        {
            if (!MarketDataSessions.Contains(sessionKey))
                return;

            if (string.IsNullOrEmpty(dto.SecurityId))
            {
                _logger.LogDebug("[{Session}] 35=W missing SecurityId (tag 48) — skipping", sessionKey);
                return;
            }

            // Filtrera: är detta instrument prenumererat?
            var isSubscribed = await _snapshotRepo.IsSubscribedAsync(sessionKey, dto.SecurityId);
            if (!isSubscribed)
            {
                _logger.LogDebug(
                    "[{Session}] 35=W SecurityId={SecId} not subscribed — skipping",
                    sessionKey, dto.SecurityId);
                return;
            }

            // Hämta CurrencyPair/Product från instrument-cachen (undvik DB per snapshot)
            var (currencyPair, product) = await GetInstrumentMetaAsync(sessionKey, dto.SecurityId);

            // Bygg snapshot-entity
            var snapshot = new MarketDataSnapshot
            {
                SessionKey   = sessionKey,
                SecurityId   = dto.SecurityId,
                MdReqId      = dto.MdReqId,
                CurrencyPair = currencyPair,
                Product      = product,
                RawPayload   = dto.RawPayload,
                ReceivedUtc  = DateTime.UtcNow,
                Entries      = dto.Entries.Select(e => MapEntry(e, dto.SecurityId)).ToList()
            };

            try
            {
                var snapshotId = await _snapshotRepo.InsertSnapshotAsync(snapshot);

                _logger.LogDebug(
                    "[{Session}] 35=W saved: SnapshotId={Id} SecurityId={SecId} Pair={Pair} Entries={Count}",
                    sessionKey, snapshotId, dto.SecurityId, currencyPair, snapshot.Entries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[{Session}] Failed to save 35=W snapshot for SecurityId={SecId}",
                    sessionKey, dto.SecurityId);
            }
        }

        /// <summary>
        /// Hämtar CurrencyPair och Product för ett SecurityID.
        /// Cachar resultatet i minnet per session för att minimera DB-queries.
        /// </summary>
        private async Task<(string? CurrencyPair, int? Product)> GetInstrumentMetaAsync(
            string sessionKey, string securityId)
        {
            lock (_cacheLock)
            {
                if (_instrumentCache.TryGetValue(sessionKey, out var sessionCache) &&
                    sessionCache.TryGetValue(securityId, out var cached))
                    return cached;
            }

            var instrument = await _instrumentRepo.GetBySecurityIdAsync(sessionKey, securityId);
            var meta = (instrument?.CurrencyPair, instrument?.Product);

            lock (_cacheLock)
            {
                if (!_instrumentCache.ContainsKey(sessionKey))
                    _instrumentCache[sessionKey] = new Dictionary<string, (string?, int?)>();
                _instrumentCache[sessionKey][securityId] = meta;
            }

            return meta;
        }

        private static MarketDataEntry MapEntry(MarketDataEntryDto dto, string securityId)
        {
            DateTime? entryDate = null;
            if (!string.IsNullOrEmpty(dto.EntryDate) &&
                DateTime.TryParseExact(dto.EntryDate, "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d))
                entryDate = d;

            TimeSpan? entryTime = null;
            if (!string.IsNullOrEmpty(dto.EntryTime) &&
                TimeSpan.TryParseExact(dto.EntryTime, @"hh\:mm\:ss\.fff",
                    System.Globalization.CultureInfo.InvariantCulture, out var t))
                entryTime = t;

            return new MarketDataEntry
            {
                SecurityId     = securityId,
                MdEntryType    = dto.MdEntryType ?? string.Empty,
                Price          = dto.Price,
                Size           = dto.Size,
                QuoteCondition = dto.QuoteCondition,
                TradeCondition = dto.TradeCondition,
                PositionNo     = dto.PositionNo,
                Originator     = dto.Originator,
                TraderId       = dto.TraderId,
                ExecInst       = dto.ExecInst,
                Scope          = dto.Scope,
                EntryDate      = entryDate,
                EntryTime      = entryTime
            };
        }
    }
}