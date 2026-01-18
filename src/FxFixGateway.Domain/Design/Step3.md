🎨 STEG 3: DEFINIERA UI-FLÖDEN OCH VIEWMODELS
________________________________________
📐 Del 1: Huvudlayout (MainWindow)
Övergripande struktur:
┌─────────────────────────────────────────────────────────────────┐
│  FxFixGateway - [Environment: PROD]                    [_][□][X] │
├─────────────────────────────────────────────────────────────────┤
│  [Menu]  Sessions  Logs  Settings                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌─────────────────────────┐  ┌─────────────────────────────┐  │
│  │                         │  │                             │  │
│  │   SESSION LIST          │  │   SESSION DETAIL            │  │
│  │   (vänster panel)       │  │   (höger panel)             │  │
│  │                         │  │                             │  │
│  │   40% av bredd          │  │   60% av bredd              │  │
│  │                         │  │                             │  │
│  └─────────────────────────┘  └─────────────────────────────┘  │
│                                                                   │
├─────────────────────────────────────────────────────────────────┤
│  Status: 3 sessions running, 1 stopped | Last sync: 10:23:45    │
└─────────────────────────────────────────────────────────────────┘
Layout-typ: Master-Detail (lista till vänster, detaljer till höger)
Varför denna layout?
•	Standard för admin-verktyg
•	Kan se många sessions samtidigt
•	Kan fokusera på en session i detalj
•	Splitter mellan paneler (användaren kan ändra bredd)
________________________________________
📋 Del 2: SESSION LIST (vänster panel)
Wireframe:
┌───────────────────────────────────────────┐
│ Sessions (4)                    [+ Add]   │
├───────────────────────────────────────────┤
│ [Filter: ___________] [All ▼] [Enabled ☑]│
├───────────────────────────────────────────┤
│                                           │
│ ●  VOLBROKER_PRIMARY      LoggedOn   ✓   │
│    Volbroker - Primary                    │
│    HB: 10:23:45                           │
│                                           │
│ ●  VOLBROKER_SECONDARY    Stopped    ✓   │
│    Volbroker - Backup                     │
│    HB: --                                 │
│                                           │
│ ●  FASTMATCH_PRIMARY      Connecting  ✓  │
│    FastMatch - Primary                    │
│    HB: 10:22:10                           │
│                                           │
│ ○  TESTBROKER_DEV         Error      ☐   │
│    Test Broker                            │
│    Error: Connection timeout              │
│                                           │
└───────────────────────────────────────────┘
Varje session-rad visar:
Rad 1:
•	Status-indikator (färgad cirkel)
•	🟢 Grön = LoggedOn
•	🟡 Gul = Starting/Connecting
•	🔴 Röd = Error
•	⚪ Grå = Stopped
•	🟠 Orange = Disconnecting
•	SessionKey (bold text)
•	Status (text)
•	IsEnabled (checkbox ✓/☐)
Rad 2:
•	Description (mindre text, grå)
Rad 3:
•	Last Heartbeat eller Error message (mycket liten text)
Filter-funktioner:
Textfilter:
•	Sök på SessionKey, VenueCode eller Description
•	Live-filtrering när användaren skriver
Dropdown:
•	All
•	Running (LoggedOn, Connecting)
•	Stopped
•	Error
•	By Venue (Volbroker, FastMatch, etc.)
Checkbox:
•	"Show only enabled" - visar bara IsEnabled=true
Actions:
[+ Add] knapp:
•	Skapar ny session med default-värden
•	Öppnar i detail-panel för redigering
•	Session får namn "NEW_SESSION" tills användaren ändrar
Right-click context menu på session:
•	Start
•	Stop
•	Restart
•	Edit
•	Delete
•	Copy SessionKey
•	View Logs
Double-click på session:
•	Öppnar detail-panel (om inte redan öppen)
•	Går till "Details" tab
________________________________________
📝 Del 3: SESSION DETAIL (höger panel)
Tab-struktur:
┌─────────────────────────────────────────────────────────────┐
│ [Details] [Status] [Messages] [ACKs]                       │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   (Tab-innehåll här)                                        │
│                                                             │
└─────────────────────────────────────────────────────────────┘
________________________________________
TAB 1: Details (Configuration)
┌─────────────────────────────────────────────────────────────┐
│ VOLBROKER_PRIMARY                    [Edit] [Save] [Delete] │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│ ┌─ General ────────────────────────────────────────────┐   │
│ │                                                       │   │
│ │  Session Key:    [VOLBROKER_PRIMARY____________]     │   │
│ │  Venue Code:     [VOLBROKER____________________]     │   │
│ │  Type:           [Primary ▼]                         │   │
│ │  Description:    [Volbroker primary connection___]   │   │
│ │  Enabled:        [✓] Auto-start when gateway starts │   │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
│ ┌─ Connection ──────────────────────────────────────────┐   │
│ │                                                       │   │
│ │  Host:           [fix.volbroker.com____________]     │   │
│ │  Port:           [9876____]                          │   │
│ │  Use SSL:        [✓]                                 │   │
│ │  SSL Server:     [fix.volbroker.com____________]     │   │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
│ ┌─ FIX Protocol ────────────────────────────────────────┐   │
│ │                                                       │   │
│ │  FIX Version:    [FIX.4.4 ▼]                         │   │
│ │  Sender CompID:  [OURCOMPANY___________________]     │   │
│ │  Target CompID:  [VOLBROKER____________________]     │   │
│ │  Heartbeat Int:  [30__] seconds                      │   │
│ │                                                       │   │
│ │  Data Dictionary:[✓] Use                             │   │
│ │  Dictionary File:[FIX44_Volbroker.xml__________] [...] │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
│ ┌─ Session Schedule ────────────────────────────────────┐   │
│ │                                                       │   │
│ │  Start Time:     [06:00__] (UTC)                     │   │
│ │  End Time:       [22:00__] (UTC)                     │   │
│ │  Reconnect:      [30__] seconds                      │   │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
│ ┌─ Authentication ──────────────────────────────────────┐   │
│ │                                                       │   │
│ │  Username:       [tradinguser__________________]     │   │
│ │  Password:       [●●●●●●●●●●__________________]      │   │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
│ ┌─ Acknowledgments ─────────────────────────────────────┐   │
│ │                                                       │   │
│ │  Requires ACK:   [✓]                                 │   │
│ │  ACK Mode:       [Automatic ▼]                       │   │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
│ ┌─ Audit ──────────────────────────────────────────────┐   │
│ │                                                       │   │
│ │  Created:        2024-01-15 08:30:00 UTC             │   │
│ │  Last Updated:   2024-03-20 14:22:10 UTC             │   │
│ │  Updated By:     john.doe                            │   │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
Beteende:
Edit mode:
•	Alla fält är disabled by default (read-only)
•	Klick på [Edit] → enabled (kan ändra)
•	[Save] och [Delete] aktiveras
•	Kan INTE editera om session kör (LoggedOn/Connecting)
•	[Edit] knapp är disabled med tooltip: "Stop session before editing"
Validation:
•	Real-time när användaren lämnar fält
•	Röd border + error-text under fält om invalid
•	[Save] disabled tills allt är valid
Save:
•	Sparar till DB via SessionManagementService
•	Visar success-toast: "Configuration saved"
•	Går tillbaka till read-only mode
•	Uppdaterar UpdatedBy och UpdatedUtc
Delete:
•	Visar confirmation dialog: "Delete session VOLBROKER_PRIMARY?"
•	Kan INTE delete om session kör
•	Efter delete: tas bort från lista, detail-panel töms
________________________________________
TAB 2: Status (Runtime information)
┌─────────────────────────────────────────────────────────────┐
│ VOLBROKER_PRIMARY - Runtime Status                          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  [Start] [Stop] [Restart]                                   │
│                                                             │
│ ┌─ Current Status ──────────────────────────────────────┐   │
│ │                                                       │   │
│ │  Status:         ● LoggedOn                          │   │
│ │  Uptime:         02:14:33                            │   │
│ │                                                       │   │
│ │  Last Logon:     2024-03-20 08:15:22 UTC             │   │
│ │  Last Logout:    2024-03-19 22:05:10 UTC             │   │
│ │  Last Heartbeat: 10:29:45 (4 seconds ago)            │   │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
│ ┌─ Statistics (Today) ──────────────────────────────────┐   │
│ │                                                       │   │
│ │  Messages In:    1,247                               │   │
│ │  Messages Out:   423                                 │   │
│ │  Heartbeats:     720                                 │   │
│ │  Errors:         0                                   │   │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
│ ┌─ Last Error ──────────────────────────────────────────┐   │
│ │                                                       │   │
│ │  (No errors)                                         │   │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
│ ┌─ Session History (Last 24h) ──────────────────────────┐   │
│ │                                                       │   │
│ │  [Graph showing status over time]                    │   │
│ │                                                       │   │
│ │  LoggedOn  ████████████░░░░████████████████         │   │
│ │  Stopped   ░░░░░░░░░░░░████░░░░░░░░░░░░░░░         │   │
│ │            00:00    06:00    12:00    18:00         │   │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
Beteende:
Buttons:
•	[Start] - Enabled om status = Stopped eller Error
•	[Stop] - Enabled om status = LoggedOn, Connecting, Starting
•	[Restart] - Enabled om status = LoggedOn eller Stopped
Auto-refresh:
•	"Last Heartbeat" uppdateras automatiskt var sekund
•	Status-färg ändras live när events kommer
•	Uptime-räknare tickar
Last Error:
•	Tom om inga errors
•	Visar senaste error-meddelandet med timestamp om finns
•	Röd text
________________________________________
TAB 3: Messages (Message Log)
┌─────────────────────────────────────────────────────────────┐
│ VOLBROKER_PRIMARY - Message Log                             │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│ [Filter: _______] [All ▼] [In/Out ▼]  [Clear] [Export]     │
│                                                             │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ Time       Dir  Type  Summary                          │ │
│ ├─────────────────────────────────────────────────────────┤ │
│ │ 10:29:45   IN   0     Heartbeat                        │ │
│ │ 10:29:40   OUT  0     Heartbeat                        │ │
│ │ 10:29:32   IN   AE    TradeCaptureReport (EUR/USD)     │ │
│ │ 10:29:33   OUT  AR    TradeCaptureReportAck            │ │
│ │ 10:29:15   IN   0     Heartbeat                        │ │
│ │ 10:29:10   OUT  0     Heartbeat                        │ │
│ │ 10:28:55   IN   AE    TradeCaptureReport (GBP/USD)     │ │
│ │ ...                                                     │ │
│ │ [100 more messages]                                     │ │
│ │                                                         │ │
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│ ┌─ RAW FIX (Selected Message) ──────────────────────────┐   │
│ │                                                       │   │
│ │  8=FIX.4.4|9=235|35=AE|49=VOLBROKER|56=OURCOMPANY|   │   │
│ │  34=1247|52=20240320-10:29:32|571=VB12345|...        │   │
│ │                                                       │   │
│ │  [Copy to Clipboard]                                 │   │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
Beteende:
Message List:
•	Senaste meddelanden först (descending time)
•	Auto-scroll till toppen när nytt meddelande kommer
•	Max 500 rader (som nu)
•	Virtualization (för performance)
Selection:
•	Click på rad → visar RAW FIX i nedre panel
•	Double-click → öppnar i popup för större vy
Filters:
•	Text-filter: söker i Summary och MsgType
•	Direction: All, Incoming, Outgoing
•	Type: All, Heartbeats, AE, AR, Custom
Actions:
•	[Clear] - Rensar listan (bara UI, DB påverkas ej)
•	[Export] - Exporterar till CSV eller text-fil
Color-coding:
•	Incoming = ljusblå bakgrund
•	Outgoing = ljusgrön bakgrund
•	Errors = röd text
________________________________________
TAB 4: ACKs (Acknowledgment Queue)
┌─────────────────────────────────────────────────────────────┐
│ VOLBROKER_PRIMARY - ACK Queue                               │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│ Pending: 2  |  Sent (today): 145  |  Failed: 0              │
│                                                             │
│ [Refresh] [Retry Failed]                                    │
│                                                             │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ Status   TradeReportID  InternID   Created      Sent   │ │
│ ├─────────────────────────────────────────────────────────┤ │
│ │ Pending  VB12347        INT-00234  10:29:50     --     │ │
│ │ Pending  VB12346        INT-00233  10:29:32     --     │ │
│ │ Sent     VB12345        INT-00232  10:29:15  10:29:16  │ │
│ │ Sent     VB12344        INT-00231  10:28:44  10:28:45  │ │
│ │ Sent     VB12343        INT-00230  10:28:20  10:28:21  │ │
│ │ ...                                                     │ │
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│ ┌─ ACK Details (Selected) ──────────────────────────────┐   │
│ │                                                       │   │
│ │  TradeReportID:  VB12347                             │   │
│ │  Intern TradeID: INT-00234                           │   │
│ │  Created:        2024-03-20 10:29:50 UTC             │   │
│ │  Status:         Pending (waiting 5 seconds)         │   │
│ │                                                       │   │
│ │  [Send Now]  [Cancel ACK]                            │   │
│ │                                                       │   │
│ └───────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
Beteende:
Auto-refresh:
•	Pending count uppdateras live när blotter lägger till ny ACK
•	Status ändras från Pending → Sent när gateway skickat
Actions:
•	[Refresh] - Manuell refresh från DB
•	[Retry Failed] - Försöker skicka alla Failed ACKs igen
•	[Send Now] - Forcear omedelbar send (istället för att vänta på polling)
•	[Cancel ACK] - Markerar som cancelled (sätter status till något annat)
Color-coding:
•	Pending = gul bakgrund
•	Sent = grön bakgrund
•	Failed = röd bakgrund
Filter:
•	Show: All, Pending Only, Sent (last hour), Failed
________________________________________
🎭 Del 4: ViewModels (MVVM)
MainViewModel
Ansvar:
•	Orchestrera hela applikationen
•	Äger SessionListViewModel och SessionDetailViewModel
•	Hanterar navigation mellan sessions
Properties:
•	SessionListViewModel SessionList - Lista över alla sessions
•	SessionDetailViewModel? SelectedSessionDetail - Vald session (null om ingen)
•	string StatusBarText - Text i statusbar
•	int RunningSessions - Antal körande sessions
•	DateTime LastSync - Senaste sync med DB
Commands:
•	AddSessionCommand - Skapar ny session
•	RefreshAllCommand - Uppdaterar från DB
•	ExitCommand - Stänger applikationen
Methods:
•	OnSessionSelected(SessionViewModel session) - När användaren väljer session i lista
•	LoadSessionsAsync() - Laddar alla sessions vid startup
________________________________________
SessionListViewModel
Ansvar:
•	Visa lista över alla sessions
•	Filtrering och sortering
•	Selection-handling
Properties:
•	ObservableCollection<SessionViewModel> Sessions - Alla sessions
•	SessionViewModel? SelectedSession - Vald session
•	string FilterText - Textfilter
•	string FilterType - "All", "Running", etc.
•	bool ShowOnlyEnabled - Checkbox för enabled-filter
Computed Properties:
•	IEnumerable<SessionViewModel> FilteredSessions - Filtrerad lista (LINQ över Sessions)
•	int TotalCount - Antal sessions
•	int RunningCount - Antal körande
Commands:
•	AddSessionCommand - Lägg till ny
•	DeleteSessionCommand - Ta bort vald
•	RefreshCommand - Uppdatera från DB
Methods:
•	ApplyFilter() - Applicerar filter på Sessions
•	LoadFromServiceAsync() - Hämtar från SessionManagementService
________________________________________
SessionViewModel
Ansvar:
•	Representera EN session i UI
•	Hantera commands för denna session
•	Binda till både FixSession (domain) och SessionConfiguration
Properties från Domain (FixSession):
•	string SessionKey
•	SessionStatus Status
•	DateTime? LastHeartbeatUtc
•	string? LastError
Properties från Configuration:
•	string VenueCode
•	string Description
•	bool IsEnabled
•	string Host
•	int Port
•	... (alla config-fält)
Computed Properties (för UI):
•	string StatusText - "LoggedOn", "Stopped", etc.
•	Brush StatusColor - Grön, röd, etc. (via converter eller computed)
•	string LastHeartbeatDisplay - "10:29:45 (5 seconds ago)"
•	bool CanStart - true om Status == Stopped
•	bool CanStop - true om Status != Stopped
•	bool CanEdit - true om Status == Stopped
Commands:

