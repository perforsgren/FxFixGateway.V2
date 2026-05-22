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
    ///   5. Upserta prisdjupet i active_market_book
    ///
    /// TODO: Vid hög volym — flytta DB-skrivning till en bakgrundskö (Channel<T>)
    ///       för att undvika att blockera FIX-tråden.
    /// </summary>
    public class MarketDataService : IMarketDataService
    {
        private readonly IMarketDataSnapshotRepository _snapshotRepo;
        private readonly IMarketInstrumentRepository   _instrumentRepo;
        private readonly ILogger<MarketDataService>    _logger;

        private readonly Dictionary<string, HashSet<string>>                                     _subscribedCache  = new();
        private readonly Dictionary<string, Dictionary<string, (string? CurrencyPair, int? Product)>> _instrumentCache = new();
        private readonly object _cacheLock = new();

        private static readonly HashSet<string> MarketDataSessions = new(StringComparer.OrdinalIgnoreCase)
        {
            "VOLB_FIXHUB_DEV",
            "VOLB_FIXHUB_PROD"
        };

        public MarketDataService(
            IMarketDataSnapshotRepository snapshotRepo,
            IMarketInstrumentRepository   instrumentRepo,
            ILogger<MarketDataService>    logger)
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

            var isSubscribed = await _snapshotRepo.IsSubscribedAsync(sessionKey, dto.SecurityId);
            if (!isSubscribed)
            {
                _logger.LogDebug(
                    "[{Session}] 35=W SecurityId={SecId} not subscribed — skipping",
                    sessionKey, dto.SecurityId);
                return;
            }

            var (currencyPair, product) = await GetInstrumentMetaAsync(sessionKey, dto.SecurityId);

            var snapshot = new MarketDataSnapshot
            {
                SessionKey   = sessionKey,
                SecurityId   = dto.SecurityId,
                MdReqId      = dto.MdReqId,
                CurrencyPair = currencyPair,
                Product      = product,
                RawPayload   = dto.RawPayload,
                ReceivedUtc  = DateTime.UtcNow,
                Entries      = dto.Entries.Select(e => MapEntry(e, dto.SecurityId)).ToList(),
                EntryCount   = dto.Entries.Count
            };

            try
            {
                var snapshotId = await _snapshotRepo.InsertSnapshotAsync(snapshot);

                _logger.LogDebug(
                    "[{Session}] 35=W saved: SnapshotId={Id} SecurityId={SecId} Pair={Pair} Entries={Count}",
                    sessionKey, snapshotId, dto.SecurityId, currencyPair, snapshot.Entries.Count);

                // Upserta prisdjupet – bara entries med PositionNo (Bid/Ask i orderboken)
                var bookEntries = BuildBookEntries(snapshot, snapshotId);
                if (bookEntries.Count > 0)
                {
                    await _snapshotRepo.UpsertBookEntriesAsync(bookEntries);

                    _logger.LogDebug(
                        "[{Session}] active_market_book updated: SecurityId={SecId} Positions={Count}",
                        sessionKey, dto.SecurityId, bookEntries.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[{Session}] Failed to save 35=W snapshot for SecurityId={SecId}",
                    sessionKey, dto.SecurityId);
            }
        }

        /// <summary>
        /// Bygger ActiveMarketBookEntry-lista från snapshot-entries som har PositionNo.
        /// Trades (MdEntryType=2) ingår inte i prisdjupet.
        /// </summary>
        private static List<ActiveMarketBookEntry> BuildBookEntries(MarketDataSnapshot snapshot, long snapshotId)
        {
            var now = DateTime.UtcNow;

            return snapshot.Entries
                .Where(e => e.PositionNo.HasValue && e.MdEntryType != "2")
                .Select(e => new ActiveMarketBookEntry
                {
                    SecurityId     = snapshot.SecurityId,
                    SessionKey     = snapshot.SessionKey,
                    CurrencyPair   = snapshot.CurrencyPair,
                    MdEntryType    = e.MdEntryType,
                    PositionNo     = e.PositionNo!.Value,
                    Price          = e.Price,
                    Size           = e.Size,
                    Originator     = e.Originator,
                    TraderId       = e.TraderId,
                    QuoteCondition = e.QuoteCondition,
                    SnapshotId     = snapshotId,
                    UpdatedUtc     = now
                })
                .ToList();
        }

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
            if (dto.EntryDate is { Length: 8 } d &&
                DateTime.TryParseExact(d, "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                entryDate = parsedDate;

            TimeSpan? entryTime = null;
            if (!string.IsNullOrEmpty(dto.EntryTime) &&
                TimeSpan.TryParse(dto.EntryTime, out var parsedTime))
                entryTime = parsedTime;

            return new MarketDataEntry
            {
                SecurityId     = securityId,
                MdEntryType    = dto.MdEntryType    ?? string.Empty,
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