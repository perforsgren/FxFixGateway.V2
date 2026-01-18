🏗️ STEG 1: ARKITEKTUR-ÖVERSIKT
________________________________________
📦 Del 1: Projekt-struktur
Solution: FxFixGateway.sln
FxFixGateway/
│
├── src/
│   │
│   ├── 1. FxFixGateway.Domain/              [.NET 8 Class Library]
│   │   └── Innehåll: Pure domain logic, inga beroenden
│   │
│   ├── 2. FxFixGateway.Infrastructure/      [.NET 8 Class Library]
│   │   └── Innehåll: DB, QuickFIX, external dependencies
│   │
│   ├── 3. FxFixGateway.Application/         [.NET 8 Class Library]
│   │   └── Innehåll: Business logic, orchestration
│   │
│   └── 4. FxFixGateway.UI/                  [.NET 8 WPF Application]
│       └── Innehåll: WPF views, viewmodels, UI-logik
│
└── tests/
    ├── FxFixGateway.Domain.Tests/
    ├── FxFixGateway.Infrastructure.Tests/
    └── FxFixGateway.Application.Tests/
________________________________________
📋 Del 2: Vad gör varje projekt?
1. FxFixGateway.Domain (Hjärtat)
Ansvar: Definiera "vad systemet är" (inte "hur det fungerar")
Innehåller:
Entities (saker som har identitet):
•	FixSession - En FIX-session med lifecycle
•	Har ett SessionKey (identitet)
•	Har configuration
•	Har state (status, heartbeat, error)
•	Kan starta/stoppa
Value Objects (saker som definieras av sina värden):
•	SessionConfiguration - Immutable config för en session
•	Host, Port, SenderCompID, TargetCompID
•	SSL-settings, heartbeat-intervall, etc.
•	Två configurations är lika om alla värden är lika
•	SessionIdentity - SessionKey + VenueCode
•	ConnectionEndpoint - Host + Port (kanske overkill, men ger type-safety)
Enums:
•	SessionStatus - Stopped, Starting, Connecting, LoggedOn, Disconnecting, Error
•	MessageDirection - Incoming, Outgoing
•	AckStatus - Pending, Sent, Failed
Domain Events (saker som hänt):
•	SessionStarted
•	SessionConnected
•	SessionDisconnected
•	MessageReceived
•	MessageSent
•	HeartbeatReceived
•	ErrorOccurred
Interfaces (kontrakt som infrastructure måste implementera):
•	ISessionRepository - Spara/läsa sessions från DB
•	IFixEngine - Starta/stoppa FIX-sessions, skicka meddelanden
•	IMessageLogger - Logga FIX-meddelanden
INGA beroenden till andra projekt! Detta är pure C#.
Varför separat projekt?
•	Kan testa utan DB eller QuickFIX
•	Återanvändbart om ni bygger API senare
•	Tydliga regler för vad som är "business logic"
________________________________________
2. FxFixGateway.Infrastructure (Teknik-lagret)
Ansvar: Implementera interfaces från Domain med riktig teknik
Innehåller:
Database (ADO.NET):
•	SessionRepository - Implementerar ISessionRepository
•	Läser från fix_connection_config tabell
•	Skriver updates
•	ADO.NET med MySqlConnection
•	MessageLogRepository - Implementerar IMessageLogger
•	Skriver till MessageIn tabell (när AE kommer)
•	Läser loggar för UI
•	AckQueueRepository
•	Läser pending ACKs från DB
•	Uppdaterar ACK-status
QuickFIX Integration:
•	QuickFixEngine - Implementerar IFixEngine
•	Wrapper runt FxFixGateway.Engine.QuickFix
•	Översätter mellan domain-events och QuickFIX-callbacks
•	Hanterar QuickFIX-livscykel (init, start, stop, dispose)
•	QuickFixMessageBuilder
•	Bygger FIX-meddelanden (AR, heartbeats, etc.)
Logging:
•	SerilogLogger - Structured logging till fil/konsol
•	DatabaseLogger - Persistent logging till DB (om ni vill)
Configuration:
•	AppSettingsProvider - Läser appsettings.json
Beroenden:
•	→ FxFixGateway.Domain (implementerar dess interfaces)
•	→ MySql.Data (för ADO.NET)
•	→ QuickFIX/n (via FxFixGateway.Engine.QuickFix)
•	→ Serilog
Varför separat projekt?
•	Kan byta DB utan att röra Domain eller Application
•	Kan mocka bort QuickFIX för tester
•	Tydlig separation mellan "vad" (domain) och "hur" (infrastructure)
________________________________________
3. FxFixGateway.Application (Business Logic)
Ansvar: Orkestrera domain + infrastructure för att utföra use cases
Innehåller:
Services (application services, inte domain services):
•	SessionManagementService
•	Koordinerar start/stop av sessions
•	Hanterar auto-start av enabled sessions
•	Registrerar event-handlers
•	MessageProcessingService
•	När AE kommer in: normalisera med FxTradeHub
•	Skriv till DB (MessageIn + Trade)
•	AckPollingService
•	Background-loop som pollar DB för pending ACKs
•	Bygger AR-meddelanden
•	Skickar via IFixEngine
DTOs (Data Transfer Objects):
•	SessionDto - För att transportera session-data mellan lager
•	MessageLogDto - För att visa meddelanden i UI
Mappers:
•	SessionMapper - Konverterar mellan Domain entities och DTOs
Beroenden:
•	→ FxFixGateway.Domain (använder entities och interfaces)
•	→ FxFixGateway.Infrastructure (får injected implementations)
•	→ FxTradeHub (eget shared library för normalisering)
Varför separat projekt?
•	Business logic separerad från UI
•	Kan testa use cases utan UI
•	Återanvändbart om ni bygger API/Service senare
________________________________________
4. FxFixGateway.UI (WPF)
Ansvar: Visa data och ta emot input från användare
Innehåller:
Views (XAML):
•	MainWindow.xaml - Huvudfönster
•	SessionListView.xaml - Grid med alla sessions
•	SessionDetailView.xaml - Detail-panel för vald session
•	MessageLogView.xaml - Logg-lista
•	SettingsView.xaml - Eventuella inställningar
ViewModels:
•	MainViewModel - Huvudlogik
•	SessionListViewModel - ObservableCollection av sessions
•	SessionDetailViewModel - En vald session
•	MessageLogViewModel - Loggmeddelanden
Services (UI-specific):
•	NavigationService - Byta mellan views
•	DialogService - Visa error/confirmation dialogs
•	NotificationService - Toast-notiser (optional)
Converters:
•	StatusToColorConverter - SessionStatus → färg
•	BoolToVisibilityConverter - Standard WPF stuff
Behaviors:
•	Reusable UI behaviors (drag-drop, etc.)
Beroenden:
•	→ FxFixGateway.Application (använder services)
•	→ FxFixGateway.Domain (känner till domain models för binding)
•	→ CommunityToolkit.Mvvm (för INotifyPropertyChanged, RelayCommand)
•	→ MaterialDesignThemes eller liknande (för theming)
Varför separat projekt?
•	Kan byta UI-teknologi (Blazor, Avalonia) utan att röra logik
•	Testbart (ViewModels kan unit-testas)
•	Clean separation
________________________________________
🔄 Del 3: Hur pratar projekten med varandra?
Dependency Flow (viktigaste regeln):
UI → Application → Domain ← Infrastructure
                    ↑
                    └── Interfaces definieras här
Regeln:
•	Domain beror på INGENTING
•	Infrastructure implementerar Domains interfaces
•	Application använder både Domain och Infrastructure
•	UI använder Application (och känner till Domain models för binding)
________________________________________
Exempel-flöde: Användaren klickar "Start Session"
1. UI (MainWindow)
   User klickar "Start"-knappen
   ↓

2. UI (SessionViewModel)
   StartCommand.Execute()
   ↓

3. Application (SessionManagementService)
   await StartSessionAsync(sessionKey)
   ↓
   - Hämtar session från repository
   - Validerar att den är Stopped
   - Anropar session.Start()
   ↓

4. Domain (FixSession)
   Start() method
   - Ändrar status till Starting
   - Raises event: SessionStatusChanged
   ↓

5. Application (SessionManagementService)
   - Lyssnar på event
   - Anropar IFixEngine.StartSession(sessionKey)
   ↓

6. Infrastructure (QuickFixEngine)
   StartSession(sessionKey)
   - Startar QuickFIX session
   - QuickFIX callbacks kommer senare...
   ↓

7. Infrastructure (QuickFixEngine)
   OnQuickFixConnected()
   - Publicerar domain event: SessionConnected
   ↓

8. Application (SessionManagementService)
   - Fångar event
   - Uppdaterar session.Status = LoggedOn
   - Sparar till repository
   ↓

9. Domain (FixSession)
   - Status ändras
   - Raises event: SessionStatusChanged
   ↓

10. UI (SessionViewModel)
    - Lyssnar på event (via service)
    - Uppdaterar Status property
    - WPF binding uppdaterar UI automatiskt
    - Knappen blir disabled (via IsEnabled binding)
Notera: Events flödar både "nedåt" (UI → Application → Infrastructure) och "uppåt" (Infrastructure → Domain events → Application → UI updates)
________________________________________
Exempel-flöde: AE-meddelande kommer in
1. Volbroker
   Skickar AE (TradeCaptureReport)
   ↓

2. QuickFIX/n (FxFixGateway.Engine.QuickFix)
   OnMessage() callback
   ↓

3. Infrastructure (QuickFixEngine)
   HandleIncomingMessage(message)
   - Raises domain event: MessageReceived
   ↓

4. Application (MessageProcessingService)
   OnMessageReceived(event)
   - Anropar IMessageLogger.LogIncoming(message)
   - Anropar FxTradeHub.ParseAE(message)
   - Skapar Trade-objekt
   - Sparar till DB via repository
   ↓

5. Infrastructure (MessageLogRepository)
   SaveMessage(messageDto)
   - ADO.NET: INSERT into MessageIn
   - ADO.NET: INSERT into Trades (via FxTradeHub)
   ↓

6. Database
   Ny rad i MessageIn, ny rad i Trades
   ↓

7. Blotter (separat app)
   Pollar Trades-tabellen
   - Ser ny trade
   - Visar i UI
   - Användare bokar...
________________________________________
Exempel-flöde: ACK Polling (ny funktionalitet)
1. Application (AckPollingService)
   Background loop (varje sekund)
   - Anropar repository.GetPendingAcks()
   ↓

2. Infrastructure (AckQueueRepository)
   Query: SELECT * FROM Trades WHERE AckStatus = 'Pending'
   - Returnerar lista
   ↓

3. Application (AckPollingService)
   För varje pending ACK:
   - Anropar QuickFixMessageBuilder.BuildAR(ack)
   - Anropar IFixEngine.SendMessage(sessionKey, arMessage)
   ↓

4. Infrastructure (QuickFixEngine)
   SendMessage(sessionKey, message)
   - QuickFIX skickar meddelandet
   ↓

5. Application (AckPollingService)
   - Anropar repository.UpdateAckStatus(ackId, 'Sent')
   ↓

6. Infrastructure (AckQueueRepository)
   UPDATE Trades SET AckStatus = 'Sent', SentUtc = NOW()
________________________________________
🎨 Del 4: Interfaces (kontrakt mellan lagren)
Låt mig definiera de viktigaste:
ISessionRepository (Domain interface, Infrastructure implementerar)
Ansvar: Läsa och skriva session-konfiguration till persistent storage
Metoder:
•	Task<IEnumerable<SessionConfiguration>> GetAllAsync()
•	Task<SessionConfiguration?> GetByKeyAsync(string sessionKey)
•	Task SaveAsync(SessionConfiguration config)
•	Task DeleteAsync(string sessionKey)
Varför interface?
•	Domain bryr sig inte OM det är MySQL, PostgreSQL eller fil
•	Lätt att mocka för tester
•	Kan byta DB-teknologi senare
________________________________________
IFixEngine (Domain interface, Infrastructure implementerar)
Ansvar: Hantera FIX-kommunikation
Metoder:
•	Task InitializeAsync(IEnumerable<SessionConfiguration> sessions)
•	Task StartSessionAsync(string sessionKey)
•	Task StopSessionAsync(string sessionKey)
•	Task SendMessageAsync(string sessionKey, object message)
•	Task ShutdownAsync()
Events:
•	event EventHandler<SessionStatusChangedEvent> StatusChanged
•	event EventHandler<MessageReceivedEvent> MessageReceived
•	event EventHandler<HeartbeatReceivedEvent> HeartbeatReceived
•	event EventHandler<ErrorOccurredEvent> ErrorOccurred
Varför interface?
•	Kan mocka QuickFIX för tester (använd MockFixEngine istället)
•	Domain-lager bryr sig inte om att det är QuickFIX
•	Kan byta till annan FIX-implementation senare (osannolikt, men teoretiskt möjligt)
________________________________________
IMessageLogger (Domain interface, Infrastructure implementerar)
Ansvar: Logga FIX-meddelanden
Metoder:
•	Task LogIncomingAsync(string sessionKey, string msgType, string rawMessage)
•	Task LogOutgoingAsync(string sessionKey, string msgType, string rawMessage)
•	Task<IEnumerable<MessageLogEntry>> GetRecentAsync(string sessionKey, int count = 100)
________________________________________
IAckQueueRepository (Application interface, Infrastructure implementerar)
Ansvar: Hantera ACK-kön
Metoder:
•	Task<IEnumerable<PendingAck>> GetPendingAcksAsync()
•	Task UpdateAckStatusAsync(long ackId, AckStatus status, DateTime? sentUtc)
________________________________________
📊 Del 5: Data-flöde genom systemet
Configuration Data (från DB):
MySQL (fix_connection_config)
  ↓ [ADO.NET query]
Infrastructure (SessionRepository)
  ↓ [returnerar SessionConfiguration]
Application (SessionManagementService)
  ↓ [mappar till SessionDto]
UI (SessionViewModel)
  ↓ [bindar till properties]
WPF (DataGrid)
Runtime State (från QuickFIX):
QuickFIX callbacks
  ↓ [OnLogon, OnLogout, etc.]
Infrastructure (QuickFixEngine)
  ↓ [raises domain event]
Application (SessionManagementService)
  ↓ [uppdaterar FixSession entity]
Domain (FixSession)
  ↓ [raises StatusChanged event]
Application → UI
  ↓ [ViewModel updates property]
WPF (Binding uppdaterar UI)
User Actions (från UI):
User clicks "Start"
  ↓ [button command]
UI (SessionViewModel.StartCommand)
  ↓ [anropar service]
Application (SessionManagementService.StartAsync)
  ↓ [använder IFixEngine]
Infrastructure (QuickFixEngine.StartSession)
  ↓ [QuickFIX startar]
... och sen kommer events tillbaka uppåt (se ovan)
________________________________________
🎯 Del 6: Viktigaste Design-principerna
1. Dependency Inversion
Domain definierar interfaces → Infrastructure implementerar dem
2. Single Responsibility
•	Domain: "Vad är en Session?"
•	Infrastructure: "Hur sparar vi den?"
•	Application: "Vad händer när användaren vill starta den?"
•	UI: "Hur visar vi den?"
3. Separation of Concerns
Varje lager har sitt ansvar, inget läcker
4. Testability
Alla interfaces kan mockas → unit tests utan DB/QuickFIX
5. Flexibility
Kan byta:
•	DB (MySQL → PostgreSQL)
•	UI (WPF → Blazor)
•	FIX engine (QuickFIX → annan)
...utan att ändra Domain

