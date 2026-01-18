🎯 STEG 2: DEFINIERA DOMAIN MODELS
________________________________________
📦 Del 1: Vad ingår i Domain?
Domain-lagret ska innehålla:
1.	Entities - Objekt med identitet och livscykel
2.	Value Objects - Objekt definierade av sina värden (immutable)
3.	Enums - Uppsättningar av tillåtna värden
4.	Domain Events - Notifikationer om vad som hänt
5.	Interfaces - Kontrakt för infrastructure
Ingen implementation av databas, QuickFIX, eller UI!
________________________________________
🏛️ Del 2: ENTITIES
FixSession (Huvudentity)
Vad är det?
En FIX-session är en pågående eller vilande connection till en venue (Volbroker, etc.). Den har ett unikt SessionKey och går igenom olika states under sin livstid.
Identitet:
•	SessionKey (string) - Unik identifierare, t.ex. "VOLBROKER_PRIMARY"
State (mutable, ändras under runtime):
•	Status (SessionStatus enum) - Nuvarande status
•	LastLogonUtc (DateTime?) - När loggade vi senast in
•	LastLogoutUtc (DateTime?) - När loggade vi senast ut
•	LastHeartbeatUtc (DateTime?) - Senaste heartbeat
•	LastError (string?) - Senaste felmeddelande
Configuration (immutable, ändras bara via Save):
•	Configuration (SessionConfiguration value object) - Alla settings
Behavior (metoder):
•	Start() - Försök starta sessionen
•	Stop() - Stoppa sessionen
•	UpdateStatus(SessionStatus newStatus) - Intern metod för state-ändring
•	RecordHeartbeat(DateTime timestamp) - Logga heartbeat
•	RecordError(string errorMessage) - Logga fel
•	UpdateConfiguration(SessionConfiguration newConfig) - Uppdatera config
Events (vad den publicerar):
•	SessionStatusChanged - När status ändras
•	HeartbeatReceived - När heartbeat kommer
•	ErrorOccurred - När fel uppstår
•	ConfigurationUpdated - När config ändras
Business Rules (invariants):
•	Kan bara starta om status är Stopped eller Error
•	Kan bara stoppa om status är Starting, Connecting eller LoggedOn
•	SessionKey kan aldrig vara null eller empty
•	Status-övergångar måste följa regler:
•	Stopped → Starting ✅
•	Starting → Stopped ❌ (måste gå via Error eller Connecting först)
•	LoggedOn → Stopped ❌ (måste gå via Disconnecting)
Varför entity och inte value object?
•	Den har identitet (SessionKey)
•	Den har livscykel (startar, kör, stoppar)
•	Den ändras över tid (state)
•	Två sessions med samma config är INTE samma session
________________________________________
💎 Del 3: VALUE OBJECTS
SessionConfiguration
Vad är det?
All konfiguration för hur en FIX-session ska köra. Immutable - om något ska ändras skapas en ny instans.
Properties (alla get-only):
Identifikation:
•	SessionKey (string) - Unik nyckel
•	VenueCode (string) - T.ex. "VOLBROKER", "FASTMATCH"
•	ConnectionType (string) - "Primary" eller "Secondary"
•	Description (string?) - Fritext beskrivning
Network:
•	Host (string) - IP eller hostname
•	Port (int) - Port-nummer
•	UseSsl (bool) - Kör över SSL?
•	SslServerName (string?) - SNI för SSL
FIX Protocol:
•	FixVersion (string) - T.ex. "FIX.4.4"
•	SenderCompId (string) - Vår SenderCompID
•	TargetCompId (string) - Motpartens CompID
•	HeartbeatInterval (TimeSpan) - Hur ofta heartbeat (sekunder)
•	UseDataDictionary (bool) - Använd data dictionary?
•	DataDictionaryFile (string?) - Sökväg till dictionary
Session Timing:
•	StartTime (TimeSpan) - När session får börja (tid på dygnet)
•	EndTime (TimeSpan) - När session ska sluta
•	ReconnectInterval (TimeSpan) - Hur lång paus mellan reconnect-försök
Authentication:
•	LogonUsername (string?) - Username för logon
•	Password (string?) - Password (kanske encrypted?)
Behavior:
•	IsEnabled (bool) - Ska auto-starta?
•	RequiresAck (bool) - Ska trades ackas? (för Volbroker)
•	AckMode (string?) - T.ex. "Automatic", "Manual"
Audit:
•	CreatedUtc (DateTime) - När skapades denna config
•	UpdatedUtc (DateTime) - När senast ändrad
•	UpdatedBy (string) - Vem ändrade
Metoder:
•	With...() methods - För att skapa nya instanser med ändringar
•	WithHost(string newHost) → returnerar ny SessionConfiguration
•	WithPort(int newPort) → returnerar ny SessionConfiguration
•	etc.
Validation (i konstruktor):
•	SessionKey får inte vara null/empty
•	Port måste vara 1-65535
•	HeartbeatInterval måste vara > 0
•	Host får inte vara null/empty
•	SenderCompId och TargetCompId får inte vara null/empty
Varför value object?
•	Ingen egen identitet (identifieras av SessionKey men den tillhör parent entity)
•	Immutable - config ändras genom att skapa ny instans
•	Två configs med samma värden är likvärdiga
•	Kan delas mellan sessions (teoretiskt, om två sessions hade exakt samma config)
________________________________________
SessionIdentity
Vad är det?
En kombination av SessionKey + VenueCode som unikt identifierar en session.
Properties:
•	SessionKey (string)
•	VenueCode (string)
Varför separat?
•	Type safety (kan inte skicka fel string)
•	Equality comparison inbyggd
•	Kan användas som dictionary key
Equality:
Två SessionIdentities är lika om både SessionKey och VenueCode matchar.
________________________________________
ConnectionEndpoint
Vad är det?
En kombination av Host + Port.
Properties:
•	Host (string)
•	Port (int)
Validation:
•	Host får inte vara null/empty
•	Port måste vara 1-65535
Metoder:
•	ToString() → "hostname:port"
Varför?
•	Type safety
•	Validation samlas på ett ställe
•	Kan återanvändas i olika configs
________________________________________
MessageLogEntry
Vad är det?
Ett loggat FIX-meddelande (inkommande eller utgående).
Properties:
•	Timestamp (DateTime)
•	Direction (MessageDirection enum) - Incoming eller Outgoing
•	MsgType (string) - T.ex. "AE", "AR", "0" (heartbeat)
•	Summary (string) - Kort beskrivning, t.ex. "TradeCaptureReport"
•	RawText (string) - Full FIX-sträng med SOH
Varför value object?
•	Immutable
•	Ingen egen identitet
•	Skapas en gång och ändras aldrig
________________________________________
PendingAck
Vad är det?
Representation av en trade som väntar på ACK.
Properties:
•	TradeId (long) - ID från Trades-tabellen
•	SessionKey (string) - Vilken session ska skicka ACK
•	TradeReportId (string) - TradeReportID från ursprungliga AE
•	InternTradeId (string) - ID från ert interna system
•	CreatedUtc (DateTime) - När blev den pending
Varför value object?
•	Kort livstid (bara under ACK-processning)
•	Immutable
•	DTO-liknande men i domain
________________________________________
🎨 Del 4: ENUMS
SessionStatus
Värden:
•	Stopped - Sessionen kör inte
•	Starting - Vi försöker starta
•	Connecting - Uppkopplad till socket, väntar på Logon
•	LoggedOn - Helt inloggad och aktiv
•	Disconnecting - Håller på att stänga ner
•	Error - Något gick fel
State Transitions (tillåtna övergångar):
Stopped ──Start()──> Starting ──OnConnect──> Connecting ──OnLogon──> LoggedOn
   ↑                    │                         │                      │
   │                    │                         │                      │
   └───OnStop───────────┴─────OnDisconnect────────┴──────Stop()─────────┘
   
Error ←── kan nås från vilken state som helst vid fel
________________________________________
MessageDirection
Värden:
•	Incoming - Meddelande vi tagit emot
•	Outgoing - Meddelande vi skickat
________________________________________
AckStatus
Värden:
•	Pending - Väntar på att skickas
•	Sent - Skickad till venue
•	Failed - Misslyckades skicka
________________________________________
ConnectionType
Värden:
•	Primary - Primär connection
•	Secondary - Backup connection
Varför enum och inte string?
•	Type safety
•	Kan inte skriva fel värde
•	Intellisense i IDE
________________________________________
⚡ Del 5: DOMAIN EVENTS
Vad är en domain event?
•	Notifikation att något viktigt hänt i domänen
•	Immutable (beskriver något som redan hänt)
•	Kan ha flera lyssnare
•	Används för att separera concerns (session vet inte vad som händer när den ändrar status, den bara rapporterar det)
Base Event:
All events har:
•	EventId (Guid) - Unik ID för denna event
•	OccurredAtUtc (DateTime) - När hände det
•	SessionKey (string) - Vilken session gäller det
________________________________________
SessionStatusChangedEvent
När triggas den?
När en sessions status ändras (Stopped → Starting, etc.)
Properties:
•	OldStatus (SessionStatus)
•	NewStatus (SessionStatus)
•	SessionKey (string)
Vem lyssnar?
•	Application layer (SessionManagementService) - loggar, uppdaterar UI
•	UI layer (ViewModels) - uppdaterar färg, enabled/disabled knappar
________________________________________
HeartbeatReceivedEvent
När triggas den?
När en heartbeat kommer från venue.
Properties:
•	SessionKey (string)
•	ReceivedAtUtc (DateTime)
Vem lyssnar?
•	Application layer - uppdaterar "Last Heartbeat" i UI
•	Monitoring - kollar att heartbeats kommer regelbundet
________________________________________
MessageReceivedEvent
När triggas den?
När ett FIX-meddelande (inte heartbeat) kommer in.
Properties:
•	SessionKey (string)
•	MsgType (string)
•	RawMessage (string)
•	ReceivedAtUtc (DateTime)
Vem lyssnar?
•	Application layer (MessageProcessingService) - normaliserar AE till Trade
•	Logging - sparar till MessageIn-tabell
•	UI - uppdaterar message log
________________________________________
MessageSentEvent
När triggas den?
När vi skickat ett FIX-meddelande (t.ex. AR).
Properties:
•	SessionKey (string)
•	MsgType (string)
•	RawMessage (string)
•	SentAtUtc (DateTime)
Vem lyssnar?
•	Logging - sparar till MessageOut-tabell (om ni har sådan)
•	UI - uppdaterar message log
________________________________________
ErrorOccurredEvent
När triggas den?
När något går fel i sessionen.
Properties:
•	SessionKey (string)
•	ErrorMessage (string)
•	Exception (Exception?) - Om det finns en teknisk exception
•	OccurredAtUtc (DateTime)
Vem lyssnar?
•	Application layer - loggar till Serilog
•	UI - visar error-meddelande
•	Monitoring - skickar alert
________________________________________
ConfigurationUpdatedEvent
När triggas den?
När användaren sparar en uppdaterad config.
Properties:
•	SessionKey (string)
•	OldConfiguration (SessionConfiguration)
•	NewConfiguration (SessionConfiguration)
Vem lyssnar?
•	Application layer - sparar till DB
•	Audit logging - loggar vem ändrade vad
________________________________________
🔌 Del 6: INTERFACES (Kontrakt)
ISessionRepository
Ansvar:
Läsa och skriva session-konfiguration till persistent storage (DB).
Metoder:
Query (read):
•	Task<IEnumerable<SessionConfiguration>> GetAllAsync()
•	Hämtar alla konfigurationer från DB
•	Task<SessionConfiguration?> GetByKeyAsync(string sessionKey)
•	Hämtar en specifik config
•	Returnerar null om inte finns
Commands (write):
•	Task SaveAsync(SessionConfiguration config)
•	Sparar (insert om ny, update om befintlig)
•	Använder ConnectionId internt för att avgöra om insert/update
•	Task DeleteAsync(string sessionKey)
•	Tar bort en config
•	Kastar exception om session körs (validation sker i application layer först)
Varför async?
•	ADO.NET kan köra async
•	Låser inte UI under DB-operationer
________________________________________
IFixEngine
Ansvar:
Hantera faktisk FIX-kommunikation via QuickFIX/n.
Lifecycle:
•	Task InitializeAsync(IEnumerable<SessionConfiguration> sessions)
•	Startar QuickFIX-engine med alla konfigurerade sessions
•	Anropas en gång vid startup
•	Task ShutdownAsync()
•	Stänger ner alla sessions graciöst
•	Anropas vid application shutdown
Session Control:
•	Task StartSessionAsync(string sessionKey)
•	Startar en specifik session
•	Kastar exception om sessionKey inte finns
•	Task StopSessionAsync(string sessionKey)
•	Stoppar en specifik session
•	Task RestartSessionAsync(string sessionKey)
•	Stop + Start i en operation
Messaging:
•	Task SendMessageAsync(string sessionKey, FixMessage message)
•	Skickar ett FIX-meddelande (t.ex. AR)
•	FixMessage är en wrapper runt QuickFIX.Message
Events:
•	event EventHandler<SessionStatusChangedEvent> StatusChanged
•	event EventHandler<MessageReceivedEvent> MessageReceived
•	event EventHandler<MessageSentEvent> MessageSent
•	event EventHandler<HeartbeatReceivedEvent> HeartbeatReceived
•	event EventHandler<ErrorOccurredEvent> ErrorOccurred
Varför interface?
•	Kan mocka i tester (MockFixEngine istället för QuickFix)
•	Domain vet inte att QuickFIX finns
•	Kan teoretiskt byta implementation
________________________________________
IMessageLogger
Ansvar:
Logga FIX-meddelanden persistent.
Metoder:
•	Task LogIncomingAsync(string sessionKey, string msgType, string rawMessage)
•	Sparar inkommande meddelande till DB (MessageIn-tabell)
•	Task LogOutgoingAsync(string sessionKey, string msgType, string rawMessage)
•	Sparar utgående meddelande till DB
•	Task<IEnumerable<MessageLogEntry>> GetRecentAsync(string sessionKey, int maxCount = 100)
•	Hämtar senaste N meddelanden för en session
•	För UI-visning
Varför separat från ISessionRepository?
•	Loggar kan bli jättemånga (tusentals per dag)
•	Kanske vill ha olika retention (kanske rensa gamla loggar men behålla config)
•	Olika performance-krav (logging ska vara snabbt, bulk-inserts)
________________________________________
IAckQueueRepository
Ansvar:
Hantera kön av trades som väntar på ACK.
Metoder:
•	Task<IEnumerable<PendingAck>> GetPendingAcksAsync(int maxCount = 100)
•	Hämtar pending ACKs från DB (Trades där AckStatus='Pending')
•	Task UpdateAckStatusAsync(long tradeId, AckStatus newStatus, DateTime? sentUtc)
•	Uppdaterar status efter att AR skickats
•	Task<int> GetPendingCountAsync(string sessionKey)
•	Räknar hur många pending ACKs en session har
•	För UI-visning
________________________________________
🔄 Del 7: Relationer mellan Domain Models
FixSession innehåller SessionConfiguration:
FixSession
  ├── SessionKey (identitet)
  ├── Configuration (SessionConfiguration value object)
  └── State
       ├── Status
       ├── LastHeartbeatUtc
       └── LastError
En FixSession "äger" sin Configuration. Configuration är immutable, så om man vill ändra måste man skapa en ny SessionConfiguration och calla UpdateConfiguration().
________________________________________
SessionConfiguration innehåller ConnectionEndpoint:
SessionConfiguration
  ├── SessionKey
  ├── Endpoint (ConnectionEndpoint value object)
  │     ├── Host
  │     └── Port
  └── ... övriga properties
(Optional - kan också ha Host och Port direkt i SessionConfiguration, men ConnectionEndpoint ger type safety)
________________________________________
Events refererar till Session:
SessionStatusChangedEvent
  ├── SessionKey (vilken session)
  └── NewStatus
Events "pekar" på session via SessionKey, inte via referens. Detta för att:
•	Event kan serialiseras (för messaging/logging)
•	Inget cirkulärt beroende
•	Event kan leva längre än session-objektet i minnet
________________________________________
🎯 Del 8: Viktiga Design-beslut
1. Immutability vs Mutability
Immutable (value objects):
•	SessionConfiguration
•	ConnectionEndpoint
•	MessageLogEntry
•	PendingAck
•	Alla events
Mutable (entities):
•	FixSession (state ändras: status, heartbeat, etc.)
Varför?
•	Value objects ska inte ändras → skapar nya istället
•	Entities har livscykel → state ändras över tid
•	Förhindrar oväntade side-effects
________________________________________
2. Validation
Var valideras vad?
I Value Object konstruktorer:
•	SessionConfiguration: Port 1-65535, SessionKey not null, etc.
•	ConnectionEndpoint: Host not null, Port valid
I Entity metoder:
•	FixSession.Start(): Kan bara anropas om Status == Stopped
•	FixSession.Stop(): Kan bara anropas om Status != Stopped
I Application layer:
•	Affärsregler: "Kan inte spara config om session kör"
•	Kombinationer: "Volbroker-sessions måste ha RequiresAck=true"
Varför uppdelat?
•	Domain validates domain rules (invariants)
•	Application validates business rules (use cases)
•	Infrastructure validates technical rules (DB constraints)
________________________________________
3. Events vs Direct calls
När Event?
•	När flera intressenter ska reagera (StatusChanged → både UI och logging)
•	När producer inte ska veta om consumers (decoupling)
•	När vi vill kunna logga/audit vad som hänt
När Direct call?
•	När det är en command med svar (SaveAsync → returnerar saved object)
•	När det är synkront beteende (Start() ändrar status direkt)
________________________________________
4. Null vs Default values
Nullable (?):
•	LastHeartbeatUtc? - kan vara null om ingen heartbeat än
•	LastError? - kan vara null om inget fel
•	DataDictionaryFile? - optional setting
Not nullable:
•	SessionKey - måste alltid finnas
•	Status - har alltid ett värde (default: Stopped)
•	Configuration - en session har alltid config
________________________________________
📋 Sammanfattning: Vad har vi definierat?
✅ Entities:
•	FixSession (huvudobjekt med lifecycle)
✅ Value Objects:
•	SessionConfiguration (all config, immutable)
•	ConnectionEndpoint (host+port)
•	SessionIdentity (key+venue)
•	MessageLogEntry (loggad message)
•	PendingAck (trade waiting for ACK)
✅ Enums:
•	SessionStatus (Stopped, Starting, LoggedOn, etc.)
•	MessageDirection (Incoming, Outgoing)
•	AckStatus (Pending, Sent, Failed)
•	ConnectionType (Primary, Secondary)
✅ Events:
•	SessionStatusChangedEvent
•	HeartbeatReceivedEvent
•	MessageReceivedEvent
•	MessageSentEvent
•	ErrorOccurredEvent
•	ConfigurationUpdatedEvent
✅ Interfaces:
•	ISessionRepository (DB operations)
•	IFixEngine (QuickFIX wrapper)
•	IMessageLogger (message logging)
•	IAckQueueRepository (ACK queue)

