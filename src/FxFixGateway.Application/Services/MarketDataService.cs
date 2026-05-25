using System.Threading.Channels;
using FxFixGateway.Domain.Entities;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Application.Services
{
    public class MarketDataService : IMarketDataService, IAsyncDisposable
    {
        private readonly IMarketDataSnapshotRepository              _snapshotRepo;
        private readonly IMarketInstrumentRepository                _instrumentRepo;
        private readonly ILogger<MarketDataService>                 _logger;

        private readonly Channel<(string SessionKey, MarketDataSnapshotDto Dto)> _channel;
        private readonly Task                                                     _consumerTask;
        private readonly CancellationTokenSource                                  _cts = new();

        // Cache: sessionKey → securityId → (CurrencyPair, Product, Tenor, Cut, Strategy, Delta)
        private readonly Dictionary<string, Dictionary<string, (string? CurrencyPair, int? Product, string? Tenor, string? Cut, string? Strategy, string? Delta)>> _instrumentCache = new();
        private readonly object _cacheLock = new();

        private static readonly HashSet<string> MarketDataSessions = new(StringComparer.OrdinalIgnoreCase)
        {
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

            _channel = Channel.CreateBounded<(string, MarketDataSnapshotDto)>(
                new BoundedChannelOptions(1000)
                {
                    FullMode     = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false
                });

            _consumerTask = Task.Run(() => ConsumeAsync(_cts.Token));
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

            if (!_channel.Writer.TryWrite((sessionKey, dto)))
            {
                _logger.LogWarning(
                    "[{Session}] Market data channel full — dropping snapshot for SecurityId={SecId}",
                    sessionKey, dto.SecurityId);
            }
        }

        private async Task ConsumeAsync(CancellationToken ct)
        {
            await foreach (var (sessionKey, dto) in _channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await ProcessSnapshotAsync(sessionKey, dto);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[{Session}] Consumer failed for SecurityId={SecId}",
                        sessionKey, dto.SecurityId);
                }
            }
        }

        private async Task ProcessSnapshotAsync(string sessionKey, MarketDataSnapshotDto dto)
        {
            if (dto.Entries.Count == 0)
            {
                _logger.LogInformation(
                    "[{Session}] 35=W 268=0 for SecurityId={SecId} — clearing active_market_book",
                    sessionKey, dto.SecurityId);

                await _snapshotRepo.DeleteBookEntriesAsync(sessionKey, dto.SecurityId);
                return;
            }

            // GetInstrumentMetaAsync returnerar nu även Tenor, Cut, Strategy, Delta
            var (currencyPair, product, tenor, cut, strategy, delta) =
                await GetInstrumentMetaAsync(sessionKey, dto.SecurityId);

            var snapshot = new MarketDataSnapshot
            {
                SessionKey   = sessionKey,
                SecurityId   = dto.SecurityId,
                MdReqId      = dto.MdReqId,
                CurrencyPair = currencyPair,
                Product      = product,
                Tenor        = tenor,
                Cut          = cut,
                Strategy     = strategy,
                Delta        = delta,
                RawPayload   = dto.RawPayload,
                ReceivedUtc  = DateTime.UtcNow,
                Entries      = dto.Entries.Select(e => MapEntry(e, dto.SecurityId)).ToList(),
                EntryCount   = dto.Entries.Count
            };

            var snapshotId = await _snapshotRepo.InsertSnapshotAsync(snapshot);

            _logger.LogDebug(
                "[{Session}] 35=W saved: SnapshotId={Id} SecurityId={SecId} Pair={Pair} Tenor={Tenor} Entries={Count}",
                sessionKey, snapshotId, dto.SecurityId, currencyPair, tenor, snapshot.Entries.Count);

            var bookEntries = BuildBookEntries(snapshot, snapshotId);
            if (bookEntries.Count > 0)
                await _snapshotRepo.UpsertBookEntriesAsync(bookEntries);

            var trades = BuildTrades(snapshot, snapshotId);
            if (trades.Count > 0)
                await _snapshotRepo.InsertTradesAsync(trades);
        }

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

        private static List<MarketTrade> BuildTrades(MarketDataSnapshot snapshot, long snapshotId)
        {
            var now = DateTime.UtcNow;

            return snapshot.Entries
                .Where(e => e.MdEntryType == "2")
                .Select(e => new MarketTrade
                {
                    SecurityId     = snapshot.SecurityId,
                    SessionKey     = snapshot.SessionKey,
                    CurrencyPair   = snapshot.CurrencyPair,
                    Tenor          = snapshot.Tenor,      // ← hämtas nu från snapshot
                    Cut            = snapshot.Cut,        // ← hämtas nu från snapshot
                    Strategy       = snapshot.Strategy,   // ← hämtas nu från snapshot
                    Delta          = snapshot.Delta,      // ← hämtas nu från snapshot
                    Price          = e.Price,
                    Size           = e.Size,
                    TradeDate      = e.EntryDate.HasValue
                        ? DateOnly.FromDateTime(e.EntryDate.Value) : null,
                    TradeTime      = e.EntryTime.HasValue
                        ? TimeOnly.FromTimeSpan(e.EntryTime.Value) : null,
                    TradeCondition = e.TradeCondition,
                    SnapshotId     = snapshotId,
                    ReceivedUtc    = now
                })
                .ToList();
        }

        private async Task<(string? CurrencyPair, int? Product, string? Tenor, string? Cut, string? Strategy, string? Delta)> GetInstrumentMetaAsync(
            string sessionKey, string securityId)
        {
            lock (_cacheLock)
            {
                if (_instrumentCache.TryGetValue(sessionKey, out var sessionCache) &&
                    sessionCache.TryGetValue(securityId, out var cached))
                    return cached;
            }

            var instrument = await _instrumentRepo.GetBySecurityIdAsync(sessionKey, securityId);
            var meta       = (instrument?.CurrencyPair, instrument?.Product,
                              instrument?.Tenor, instrument?.Cut,
                              instrument?.Strategy, instrument?.Delta);

            lock (_cacheLock)
            {
                if (!_instrumentCache.ContainsKey(sessionKey))
                    _instrumentCache[sessionKey] = new();

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

        public async ValueTask DisposeAsync()
        {
            _channel.Writer.Complete();
            await _cts.CancelAsync();
            await _consumerTask.ConfigureAwait(false);
            _cts.Dispose();
        }
    }
}