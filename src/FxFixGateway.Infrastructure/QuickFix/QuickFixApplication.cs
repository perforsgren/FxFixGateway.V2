using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Events;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;
using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Parsing;
using FxTradeHub.Domain.Services;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using QF = global::QuickFix;

namespace FxFixGateway.Infrastructure.QuickFix
{
    public class QuickFixApplication : QF.IApplication
    {
        private readonly Dictionary<QF.SessionID, string> _sessionKeyMap;
        private readonly Dictionary<string, (string Username, string Password)> _sessionCredentials;
        private readonly HashSet<string> _disabledSessions;
        private readonly IMessageInService? _messageInService;
        private readonly IMessageInParserOrchestrator? _orchestrator;
        private readonly ISecurityListService? _securityListService;
        private readonly IMarketDataOrchestrator? _mdOrchestrator;
        private readonly IMarketDataSubscriber? _mdSubscriber;
        private readonly IMarketDataService? _marketDataService;     // ← NY

        public event EventHandler<SessionStatusChangedEvent>? StatusChanged;
        public event EventHandler<MessageReceivedEvent>? MessageReceived;
        public event EventHandler<MessageSentEvent>? MessageSent;
        public event EventHandler<HeartbeatReceivedEvent>? HeartbeatReceived;
        public event EventHandler<ErrorOccurredEvent>? ErrorOccurred;

        public QuickFixApplication(
            Dictionary<QF.SessionID, string> sessionKeyMap,
            Dictionary<string, (string Username, string Password)> sessionCredentials,
            IMessageInService? messageInService,
            IMessageInParserOrchestrator? orchestrator,
            IEnumerable<string>? disabledSessionKeys = null,
            ISecurityListService? securityListService = null,
            IMarketDataOrchestrator? mdOrchestrator = null,
            IMarketDataSubscriber? mdSubscriber = null,
            IMarketDataService? marketDataService = null)       // ← NY
        {
            _sessionKeyMap = sessionKeyMap ?? throw new ArgumentNullException(nameof(sessionKeyMap));
            _sessionCredentials = sessionCredentials ?? new Dictionary<string, (string, string)>();
            _disabledSessions = disabledSessionKeys != null
                ? new HashSet<string>(disabledSessionKeys, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();
            _messageInService = messageInService;
            _orchestrator = orchestrator;
            _securityListService = securityListService;
            _mdOrchestrator = mdOrchestrator;
            _mdSubscriber = mdSubscriber;
            _marketDataService = marketDataService;             // ← NY
        }

        /// <summary>
        /// Called when a new session is created.
        /// </summary>
        public void OnCreate(QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            // Log session creation as an info message
            MessageReceived?.Invoke(this, new MessageReceivedEvent(
                sessionKey,
                "CREATE",
                $"Session created: {sessionId}",
                DateTime.UtcNow));
        }

        /// <summary>
        /// Called when logon is confirmed (session is fully established).
        /// </summary>
        public void OnLogon(QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            if (_disabledSessions.Contains(sessionKey))
            {
                QF.Session.LookupSession(sessionId)?.Logout("Session is disabled");
                return;
            }

            MessageReceived?.Invoke(this, new MessageReceivedEvent(
                sessionKey, "LOGON", "Logon confirmed - session established", DateTime.UtcNow));

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey, SessionStatus.Connecting, SessionStatus.LoggedOn));

            // Starta market data-flödet för market data-sessioner (VOLB_FIXHUB_*)
            if (_mdOrchestrator != null)
                _ = Task.Run(() => _mdOrchestrator.OnSessionLoggedOnAsync(sessionKey));
        }

        /// <summary>
        /// Called when logout is received (session is disconnecting).
        /// </summary>
        public void OnLogout(QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            // Log "Logout received" event
            MessageReceived?.Invoke(this, new MessageReceivedEvent(
                sessionKey,
                "LOGOUT",
                "Logout received - session disconnected",
                DateTime.UtcNow));

            StatusChanged?.Invoke(this, new SessionStatusChangedEvent(
                sessionKey,
                SessionStatus.LoggedOn,
                SessionStatus.Stopped));
        }

        /// <summary>
        /// Called when sending admin messages (Logon, Logout, Heartbeat, TestRequest, etc.)
        /// </summary>
        public void ToAdmin(QF.Message message, QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            var msgType = GetMessageType(message);

            // Lägg till credentials i Logon-meddelandet
            if (msgType == "A") // Logon
            {
                if (_sessionCredentials.TryGetValue(sessionKey, out var credentials))
                {
                    if (!string.IsNullOrEmpty(credentials.Username))
                        message.SetField(new QF.Fields.Username(credentials.Username)); // tag 553

                    if (!string.IsNullOrEmpty(credentials.Password))
                        message.SetField(new QF.Fields.Password(credentials.Password)); // tag 554

                    if (!message.IsSetField(QF.Fields.Tags.ResetSeqNumFlag))
                        message.SetField(new QF.Fields.ResetSeqNumFlag(true)); // tag 141=Y
                }
            }

            var rawMessage = message.ToString();

            // Log ALL outgoing admin messages
            MessageSent?.Invoke(this, new MessageSentEvent(
                sessionKey,
                msgType,
                rawMessage,
                DateTime.UtcNow));

            // Fire heartbeat event for UI updates
            if (msgType == "0")
            {
                HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEvent(
                    sessionKey,
                    DateTime.UtcNow));
            }
        }

        /// <summary>
        /// Called when sending application messages (TradeCaptureReportAck, etc.)
        /// </summary>
        public void ToApp(QF.Message message, QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            var msgType = GetMessageType(message);
            var rawMessage = message.ToString();

            // Kolla om sessionen faktiskt är inloggad
            var session = QF.Session.LookupSession(sessionId);
            var isLoggedOn = session?.IsLoggedOn ?? false;
            var seqNum = message.Header.IsSetField(QF.Fields.Tags.MsgSeqNum) 
                ? message.Header.GetInt(QF.Fields.Tags.MsgSeqNum) 
                : -1;

            // Logga med mer detaljer
            System.Diagnostics.Debug.WriteLine(
                $"[ToApp] {sessionKey} MsgType={msgType} SeqNum={seqNum} IsLoggedOn={isLoggedOn}");

            if (!isLoggedOn)
            {
                // VARNING: Meddelandet kommer köas, inte skickas direkt!
                ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(
                    sessionKey,
                    $"WARNING: Sending {msgType} while session is NOT logged on - message will be queued"));
            }

            MessageSent?.Invoke(this, new MessageSentEvent(
                sessionKey,
                msgType,
                rawMessage,
                DateTime.UtcNow));
        }

        /// <summary>
        /// Called when receiving admin messages (Logon, Logout, Heartbeat, TestRequest, Reject, etc.)
        /// </summary>
        public void FromAdmin(QF.Message message, QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            var msgType = GetMessageType(message);
            var rawMessage = message.ToString();

            // Log ALL incoming admin messages
            MessageReceived?.Invoke(this, new MessageReceivedEvent(
                sessionKey,
                msgType,
                rawMessage,
                DateTime.UtcNow));

            // Handle specific admin message types
            switch (msgType)
            {
                case "0": // Heartbeat
                    HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEvent(
                        sessionKey,
                        DateTime.UtcNow));
                    break;

                case "1": // TestRequest - might indicate connectivity issues
                    // Already logged above, no special handling needed
                    break;

                case "2": // ResendRequest - indicates sequence gap
                    ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(
                        sessionKey,
                        $"ResendRequest received - sequence gap detected"));
                    break;

                case "3": // Reject - session level reject
                    var rejectReason = GetRejectReason(message);
                    
                    // ⭐ LÄGG TILL: Logga vilken MsgType som rejectades
                    var rejectedMsgType = message.IsSetField(QF.Fields.Tags.RefMsgType) 
                        ? message.GetString(QF.Fields.Tags.RefMsgType) 
                        : "?";
                    var refSeqNum = message.IsSetField(QF.Fields.Tags.RefSeqNum) 
                        ? message.GetInt(QF.Fields.Tags.RefSeqNum) 
                        : -1;
                    
                    var detailedReason = $"Session Reject: MsgType={rejectedMsgType}, RefSeqNum={refSeqNum}, Reason={rejectReason}";
                    
                    System.Diagnostics.Debug.WriteLine($"[FromAdmin] {sessionKey} {detailedReason}");
                    
                    ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(
                        sessionKey,
                        detailedReason));
                    break;

                case "4": // SequenceReset
                    // Normal during recovery, just log it (already done above)
                    break;

                case "5": // Logout
                    // Logout message received, OnLogout will be called separately
                    break;
            }
        }

        public void FromApp(QF.Message message, QF.SessionID sessionId)
        {
            var sessionKey = GetSessionKey(sessionId);
            if (sessionKey == null) return;

            var msgType    = GetMessageType(message);
            var rawMessage = message.ToString();

            // Fire event for UI (MessageLog tab)
            MessageReceived?.Invoke(this, new MessageReceivedEvent(
                sessionKey,
                msgType,
                rawMessage,
                DateTime.UtcNow));

            switch (msgType)
            {
                case "AE":
                    HandleTradeCaptureReport(sessionKey, message, rawMessage);
                    break;

                case "y" when _securityListService != null:
                {
                    // Ett 35=y kan innehålla upp till 100 instrument (tag 146=NoRelatedSym).
                    // Parsa alla grupper med dictionary här i Infrastructure.
                    var instruments = ParseSecurityInstrumentDtos(message);
                    if (instruments.Count > 0)
                        _ = Task.Run(() => _securityListService.HandleSecurityListAsync(sessionKey, instruments));
                    break;
                }

                case "W" when _marketDataService != null:
                    {
                        var dto = ParseMarketDataSnapshotDto(message, rawMessage);
                        _ = Task.Run(() => _marketDataService.HandleMarketDataSnapshotAsync(sessionKey, dto));
                        break;
                    }
            }
        }


        private void HandleTradeCaptureReport(string sessionKey, QF.Message message, string rawMessage)
        {
            if (_messageInService != null)
            {
                try
                {
                    var venueCode = GetVenueCode(sessionKey);

                    System.Diagnostics.Debug.WriteLine($"[FromApp] Processing AE for session {sessionKey}, venue {venueCode}");

                    var entity = new MessageIn
                    {
                        SourceType = "FIX",
                        SourceVenueCode = venueCode,
                        SessionKey = sessionKey,
                        RawPayload = rawMessage,
                        RawPayloadHash = ComputeSHA256Hash(rawMessage),
                        FixMsgType = "AE",
                        ReceivedUtc = DateTime.UtcNow
                    };

                    if (venueCode == "VOLBROKER")
                    {
                        EnrichVolbrokerAE(entity, message);
                    }
                    else if (venueCode == "FENICS")
                    {
                        EnrichFenicsAE(entity, message);
                        System.Diagnostics.Debug.WriteLine($"[FromApp] Fenics AE enriched. SourceMessageKey={entity.SourceMessageKey}, ExternalTradeKey={entity.ExternalTradeKey}");
                    }

                    var messageInId = _messageInService.InsertMessage(entity);
                    System.Diagnostics.Debug.WriteLine($"[FromApp] MessageIn inserted with ID {messageInId}");

                    if (_orchestrator != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FromApp] Calling orchestrator.ProcessMessage({messageInId})");
                        _orchestrator.ProcessMessage(messageInId);
                        System.Diagnostics.Debug.WriteLine($"[FromApp] Orchestrator.ProcessMessage completed for {messageInId}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[FromApp] WARNING: _orchestrator is NULL - trade will NOT be processed!");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FromApp] EXCEPTION processing AE for {sessionKey}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[FromApp] Stack trace: {ex.StackTrace}");

                    ErrorOccurred?.Invoke(this, new ErrorOccurredEvent(
                        sessionKey,
                        $"Failed to process MessageIn: {ex.Message}",
                        ex));
                }
            }
            else
            {
                // _messageInService är null — AE kan inte processas
                System.Diagnostics.Debug.WriteLine($"[FromApp] WARNING: Received AE for {sessionKey} but _messageInService is NULL!");
            }
        }


        private string GetVenueCode(string sessionKey)
        {
            return sessionKey switch
            {
                "VOLB_STP_DEV" => "VOLBROKER",
                "VOLB_STP_PROD" => "VOLBROKER",
                "FENICS_STP_STAGE2" => "FENICS",
                _ => sessionKey // fallback to SessionKey
            };
        }


        private string ComputeSHA256Hash(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return string.Empty;

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(payload);
                var hashBytes = sha.ComputeHash(bytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        private void EnrichVolbrokerAE(MessageIn entity, QF.Message message)
        {
            // KRITISKT: SourceMessageKey = 571 (TradeReportID) - detta är vad vi ska eka i AR
            entity.SourceMessageKey = TryGetField(message, 571);
            
            // ExternalTradeKey = 818 (SecondaryTradeReportID) - Volbrokers ID
            entity.ExternalTradeKey = TryGetField(message, 818);

            // Enrichment fields
            entity.InstrumentCode = TryGetField(message, 55); // Symbol
        }

        private void EnrichFenicsAE(MessageIn entity, QF.Message message)
        {
            // KRITISKT för ACK: SourceMessageKey måste fyllas i
            entity.SourceMessageKey = TryGetField(message, 571); // TradeReportID
            entity.ExternalTradeKey = TryGetField(message, 117); // Fenics Trade ID
            
            // Instrument
            entity.InstrumentCode = TryGetField(message, 55); // Symbol (EURUSD)
            
            // ⭐ NYTT: Options-specifika fält (optional - för debugging/logging)
            var cfiCode = TryGetField(message, 461); // HFTAVP = Option
            var strike = TryGetField(message, 612);  // Strike price
            var expiry = TryGetField(message, 611);  // Expiry date
            var premium = TryGetField(message, 6034); // Premium amount
            
            // Logga om det är en option
            if (cfiCode?.StartsWith("HF") == true) // HF = Option i CFI-kod
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EnrichFenicsAE] FX Option: {entity.InstrumentCode}, " +
                    $"Strike={strike}, Expiry={expiry}, Premium={premium}");
            }
            
            // UTI för regulatorisk rapportering (optional)
            var optionUTI = TryGetField(message, 8524); // Leg 1 UTI
            var hedgeUTI = TryGetField(message, 8526);  // Leg 2 UTI
            
            if (!string.IsNullOrEmpty(optionUTI) || !string.IsNullOrEmpty(hedgeUTI))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EnrichFenicsAE] UTI: Option={optionUTI}, Hedge={hedgeUTI}");
            }
        }



        private string? TryGetField(QF.Message message, int tag)
        {
            try
            {
                return message.IsSetField(tag) ? message.GetString(tag) : null;
            }
            catch
            {
                return null;
            }
        }



        private string? GetSessionKey(QF.SessionID sessionId)
        {
            return _sessionKeyMap.TryGetValue(sessionId, out var key) ? key : null;
        }

        private string GetMessageType(QF.Message message)
        {
            try
            {
                var header = message.Header;
                if (header.IsSetField(QF.Fields.Tags.MsgType))
                {
                    return header.GetString(QF.Fields.Tags.MsgType);
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return "?";
        }

        private string GetRejectReason(QF.Message message)
        {
            try
            {
                // Tag 58 = Text (reason for reject)
                if (message.IsSetField(58))
                {
                    return message.GetString(58);
                }
                // Tag 373 = SessionRejectReason
                if (message.IsSetField(373))
                {
                    var reasonCode = message.GetInt(373);
                    return $"RejectReason={reasonCode}";
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return "Unknown reason";
        }

        /// <summary>
        /// Extraherar fält ur SecurityList (35=y) med hjälp av QF.Message + data dictionary.
        /// Dictionary gör att repeating groups (555/600) parsas korrekt.
        /// </summary>
        private SecurityInstrumentDto ParseSecurityInstrumentDto(QF.Message message)
        {
            string? currencyPair = null;

            try
            {
                // Tag 555 = NoLegs. Dictionary gör att GetGroup fungerar här.
                if (message.IsSetField(555))
                {
                    var legGroup = new QF.Group(555, 600);
                    message.GetGroup(1, legGroup);
                    currencyPair = legGroup.IsSetField(600) ? legGroup.GetString(600) : null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParseSecurityInstrumentDto] leg parse failed: {ex.Message}");
            }

            return new SecurityInstrumentDto
            {
                SecurityId = TryGetField(message, 48),
                Symbol = TryGetField(message, 55),
                Product = TryGetIntField(message, 460),
                SecurityReqId = TryGetField(message, 320),
                LastFragment = TryGetField(message, 893),
                SecurityRequestResult = TryGetIntField(message, 560),
                CurrencyPair = currencyPair
            };
        }

        /// <summary>
        /// Extraherar ALLA instrument ur ett SecurityList (35=y)-meddelande.
        /// Tag 146 (NoRelatedSym) anger antal instrument i meddelandet.
        /// Varje instrument ligger i en repeating group med delimiter tag 55.
        /// Dictionary krävs för korrekt grupparsning.
        /// </summary>
        private IReadOnlyList<SecurityInstrumentDto> ParseSecurityInstrumentDtos(QF.Message message)
        {
            var result = new List<SecurityInstrumentDto>();

            // Header-fält som gäller hela meddelandet
            var securityReqId         = TryGetField(message, 320);  // SecurityReqID
            var lastFragment          = TryGetField(message, 893);   // LastFragment
            var securityRequestResult = TryGetIntField(message, 560); // SecurityRequestResult
            var noRelatedSym          = TryGetIntField(message, 146) ?? 0;

            for (int i = 1; i <= noRelatedSym; i++)
            {
                try
                {
                    // NoRelatedSym (146) grupp — delimiter är tag 55 (Symbol)
                    var instrGroup = new QF.Group(146, 55);
                    message.GetGroup(i, instrGroup);

                    var securityId = TryGetField(instrGroup, 48);  // SecurityID
                    var symbol     = TryGetField(instrGroup, 55);   // Symbol
                    var product    = TryGetIntField(instrGroup, 460); // Product

                    // Hämta leg-attribut ur första benet (555/600-gruppen)
                    string? currencyPair = null;
                    string? tenor        = null;
                    string? expiryDate   = null;
                    string? cut          = null;
                    string? strike       = null;
                    string? premiumCcy   = null;

                    try
                    {
                        if (instrGroup.IsSetField(555))
                        {
                            var legGroup = new QF.Group(555, 600);
                            instrGroup.GetGroup(1, legGroup);
                            currencyPair = TryGetField(legGroup, 600);  // LegSymbol
                            tenor        = TryGetField(legGroup, 620);  // LegMaturityMonthYear
                            expiryDate   = TryGetField(legGroup, 611);  // LegMaturityDate
                            cut          = TryGetField(legGroup, 598);  // LegSettlType
                            strike       = TryGetField(legGroup, 612);  // LegStrikePrice
                            premiumCcy   = TryGetField(legGroup, 556);  // LegCurrency
                        }
                    }
                    catch (Exception legEx)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ParseSecurityInstrumentDtos] leg parse failed group {i}: {legEx.Message}");
                    }

                    // Underliggande (711/311-gruppen) — strategy, delta, amountCcy
                    string? strategy  = null;
                    string? delta     = null;
                    string? amountCcy = null;

                    try
                    {
                        if (instrGroup.IsSetField(711))
                        {
                            var ulGroup = new QF.Group(711, 311);
                            instrGroup.GetGroup(1, ulGroup);
                            strategy  = TryGetField(ulGroup, 310);  // UnderlyingPutOrCall → strategy
                            delta     = TryGetField(ulGroup, 763);  // UnderlyingSecurityDesc → delta
                            amountCcy = TryGetField(ulGroup, 318);  // UnderlyingCurrency
                        }
                    }
                    catch (Exception ulEx)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ParseSecurityInstrumentDtos] underlying parse failed group {i}: {ulEx.Message}");
                    }

                    result.Add(new SecurityInstrumentDto
                    {
                        SecurityId            = securityId,
                        Symbol                = symbol,
                        Product               = product,
                        CurrencyPair          = currencyPair,
                        SecurityReqId         = securityReqId,
                        LastFragment          = lastFragment,
                        SecurityRequestResult = securityRequestResult,
                        Tenor = tenor,
                        ExpiryDate = expiryDate,
                        Cut = cut,
                        Strike = strike,
                        PremiumCcy = premiumCcy,
                        Strategy = strategy,
                        Delta = delta,
                        AmountCcy = amountCcy,
                        QuoteStyle = TryGetField(instrGroup, 691),  // Benchmark → VOL/PCT
                        DeltaStyle = TryGetField(instrGroup, 876),  // LegBenchmarkCurveName → FWD/SPOT
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ParseSecurityInstrumentDtos] failed to parse group {i}: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// Extraherar data ur MarketDataSnapshot (35=W) med dictionary-stöd.
        /// Tag 268 (NoMDEntries) innehåller bid/offer/trade-entries.
        /// </summary>
        private MarketDataSnapshotDto ParseMarketDataSnapshotDto(QF.Message message, string rawMessage)
        {
            var securityId = TryGetField(message, 48);   // SecurityID
            var mdReqId    = TryGetField(message, 262);  // MDReqID
            var entries    = new List<MarketDataEntryDto>();

            var noMdEntries = TryGetIntField(message, 268) ?? 0;

            for (int i = 1; i <= noMdEntries; i++)
            {
                try
                {
                    // NoMDEntries (268) grupp — delimiter är tag 269 (MDEntryType)
                    var entryGroup = new QF.Group(268, 269);
                    message.GetGroup(i, entryGroup);

                    decimal? price = null;
                    if (entryGroup.IsSetField(270) &&
                        decimal.TryParse(entryGroup.GetString(270),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var p))
                        price = p;

                    decimal? size = null;
                    if (entryGroup.IsSetField(271) &&
                        decimal.TryParse(entryGroup.GetString(271),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var s))
                        size = s;

                    entries.Add(new MarketDataEntryDto
                    {
                        MdEntryType    = TryGetField(entryGroup, 269),
                        Price          = price,
                        Size           = size,
                        QuoteCondition = TryGetField(entryGroup, 276),
                        TradeCondition = TryGetField(entryGroup, 277),
                        PositionNo     = TryGetIntField(entryGroup, 290),
                        Originator     = TryGetField(entryGroup, 282),
                        TraderId       = TryGetField(entryGroup, 9536),
                        ExecInst       = TryGetField(entryGroup, 18),
                        Scope          = TryGetField(entryGroup, 546),
                        EntryDate      = TryGetField(entryGroup, 272),
                        EntryTime      = TryGetField(entryGroup, 273)
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ParseMarketDataSnapshotDto] failed entry group {i}: {ex.Message}");
                }
            }

            return new MarketDataSnapshotDto
            {
                SecurityId = securityId,
                MdReqId    = mdReqId,
                RawPayload = rawMessage,
                Entries    = entries
            };
        }

        private static int? TryGetIntField(QF.Message message, int tag)
        {
            try { return message.IsSetField(tag) ? message.GetInt(tag) : null; }
            catch { return null; }
        }

        private static int? TryGetIntField(QF.FieldMap fieldMap, int tag)
        {
            try { return fieldMap.IsSetField(tag) ? fieldMap.GetInt(tag) : null; }
            catch { return null; }
        }

        private static string? TryGetField(QF.FieldMap fieldMap, int tag)
        {
            try { return fieldMap.IsSetField(tag) ? fieldMap.GetString(tag) : null; }
            catch { return null; }
        }
    }
}
