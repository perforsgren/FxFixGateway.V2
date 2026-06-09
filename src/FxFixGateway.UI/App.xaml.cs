using FxFixGateway.Application.BackgroundServices;
using FxFixGateway.Application.Services;
using FxFixGateway.Domain.Enums;                                          // DisconnectReason
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Infrastructure.Logging;
using FxFixGateway.Infrastructure.Notifications;                          // PushoverNotificationService
using FxFixGateway.Infrastructure.Persistence;
using FxFixGateway.Infrastructure.PostMarker;                             // PostMarkerSession, PostMarkerAckChannel, PostMarkerPayloadHandler
using FxFixGateway.Infrastructure.QuickFix;
using FxFixGateway.UI.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using FxTradeHub.Domain.Services;
using FxTradeHub.Domain.Parsing;
using FxTradeHub.Services.Ingest;
using FxTradeHub.Services.Parsing;
using FxTradeHub.Data.MySql.Repositories;
using FxSharedConfig;




namespace FxFixGateway.UI
{
    public partial class App : System.Windows.Application
    {
        private IHost? _host;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            SerilogConfiguration.Configure();

#if DEBUG
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level =
                System.Diagnostics.SourceLevels.Critical;
#endif

            try
            {
                _host = Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration((context, config) =>
                    {
                        config.SetBasePath(Directory.GetCurrentDirectory());
                        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    })
                    .ConfigureServices((context, services) =>
                    {
                        ConfigureServices(services, context.Configuration);
                    })
                    .UseSerilog()
                    .Build();

                await _host.StartAsync();

                // Ensure MessageProcessingService is instantiated to register event handlers
                _ = _host.Services.GetRequiredService<MessageProcessingService>();

                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "APPLICATION STARTUP FAILED");

                var fullError = GetFullExceptionDetails(ex);
                var errorLogPath = Path.Combine(Directory.GetCurrentDirectory(), "startup_error.txt");
                File.WriteAllText(errorLogPath, fullError);

                MessageBox.Show(
                    $"{fullError}\n\n(Sparat till: {errorLogPath})",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
            }
        }

        private static string GetFullExceptionDetails(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            var current = ex;
            var level = 0;

            while (current != null)
            {
                var indent = new string(' ', level * 2);
                sb.AppendLine($"{indent}=== Exception Level {level} ===");
                sb.AppendLine($"{indent}Type: {current.GetType().FullName}");
                sb.AppendLine($"{indent}Message: {current.Message}");
                sb.AppendLine($"{indent}Source: {current.Source}");
                sb.AppendLine($"{indent}StackTrace:");
                sb.AppendLine($"{indent}{current.StackTrace}");
                sb.AppendLine();

                current = current.InnerException;
                level++;
            }

            return sb.ToString();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Application shutting down...");

            Task.Run(async () =>
            {
                try
                {
                    if (_host != null)
                    {
                        var fixApp = _host.Services.GetService<QuickFixApplication>();
                        if (fixApp != null)
                            fixApp.PendingDisconnectReason = DisconnectReason.UserExit;

                        var push = _host.Services.GetService<IPushNotificationService>();
                        if (push != null)
                        {
                            try
                            {
                                await push.SendDisconnectAsync("Gateway", DisconnectReason.UserExit)
                                          .WaitAsync(TimeSpan.FromSeconds(4))
                                          .ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Push notification failed during exit");
                            }
                        }

                        await _host.StopAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        _host.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error during shutdown");
                }
            }).GetAwaiter().GetResult();  // ← blockerar Dispatcher-tråden tills allt är klart

            SerilogConfiguration.Close();
            base.OnExit(e);
        }

        private void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // DIFFERENTIATED pool sizes based on actual concurrent usage.
            // minPoolSize=0: no permanently held connections — they are created on demand
            // and recycled after ConnectionLifeTime=300s when idle (e.g. outside market hours).
            var connectionString = SetConnectionPoolSize(
                AppDbConfig.GetConnectionString("fix_config_prod"),
                maxPoolSize: 20,
                minPoolSize: 0);

            var safeConnStr = System.Text.RegularExpressions.Regex.Replace(
                connectionString, @"Password=[^;]*", "Password=***");
            Log.Information("Using GatewayDb connection string: {ConnectionString} (pool: 0-20)", safeConnStr);

            var stpConnectionString = SetConnectionPoolSize(
                AppDbConfig.GetConnectionString("trade_stp"),
                maxPoolSize: 30,
                minPoolSize: 0);

            var safeSTPConnStr = System.Text.RegularExpressions.Regex.Replace(
                stpConnectionString, @"Password=[^;]*", "Password=***");
            Log.Information("Using STP connection string: {ConnectionString} (pool: 0-30)", safeSTPConnStr);

            var fxvolConnectionString = SetConnectionPoolSize(
                AppDbConfig.GetConnectionString("VolManager"),
                maxPoolSize: 60,
                minPoolSize: 0);

            var safeFxvolConnStr = System.Text.RegularExpressions.Regex.Replace(
                fxvolConnectionString, @"Password=[^;]*", "Password=***");
            Log.Information("Using VolManager connection string: {ConnectionString} (pool: 0-40)", safeFxvolConnStr);

            // Infrastructure - Repositories
            services.AddSingleton<ISessionRepository>(sp =>
                new SessionRepository(connectionString));

            services.AddSingleton<IMessageLogger>(sp =>
                new MessageLogRepository(connectionString));

            services.AddSingleton<IAckQueueRepository>(sp =>
                new AckQueueRepository(stpConnectionString));

            // FxTradeHub services
            services.AddSingleton<IMessageInService>(sp =>
            {
                var repository = new MessageInRepository(stpConnectionString);
                var service = new MessageInService(repository);
                return service;
            });

            services.AddSingleton<IMessageInParserOrchestrator>(sp =>
            {
                var messageInRepo = new MessageInRepository(stpConnectionString);
                var stpRepo = new MySqlStpRepository(stpConnectionString);
                var lookupRepo = new MySqlStpLookupRepository(stpConnectionString);

                var parsers = new List<IInboundMessageParser>
                {
                    new VolbrokerFixAeParser(lookupRepo),
                    new FenicsFixAeParser(lookupRepo),
                    new FXOhubXmlFileParser(lookupRepo)
                };

                return new MessageInParserOrchestrator(messageInRepo, stpRepo, parsers);
            });

            // Proxy registreras som singleton — QuickFixEngine sätter inner-sender efter InitializeAsync
            services.AddSingleton<QuickFixSenderProxy>();
            services.AddSingleton<IMarketDataSubscriber>(sp => sp.GetRequiredService<QuickFixSenderProxy>());

            // Market Data — repositories
            services.AddSingleton<IMarketSubscriptionRepository>(sp =>
                new MarketSubscriptionRepository(connectionString));      // fix_config_prod

            services.AddSingleton<IMarketInstrumentRepository>(sp =>
                new MarketInstrumentRepository(fxvolConnectionString));  // fxvol

            services.AddSingleton<IMarketDataSnapshotRepository>(sp =>
                new MarketDataSnapshotRepository(fxvolConnectionString));  // fxvol

            services.AddSingleton<IMarketDataService>(sp =>
            {
                var snapshotRepo = sp.GetRequiredService<IMarketDataSnapshotRepository>();
                var instrRepo = sp.GetRequiredService<IMarketInstrumentRepository>();
                var logger = sp.GetRequiredService<ILogger<MarketDataService>>();
                return new MarketDataService(snapshotRepo, instrRepo, logger);
            });

            // Market Data — orchestration
            services.AddSingleton<IMarketDataOrchestrator>(sp =>
            {
                var subscriber = sp.GetRequiredService<IMarketDataSubscriber>();
                var instrRepo = sp.GetRequiredService<IMarketInstrumentRepository>();
                var logger = sp.GetRequiredService<ILogger<MarketDataOrchestrator>>();
                return new MarketDataOrchestrator(subscriber, instrRepo, logger);
            });

            services.AddSingleton<ISecurityListService>(sp =>
            {
                var subRepo = sp.GetRequiredService<IMarketSubscriptionRepository>();
                var instrRepo = sp.GetRequiredService<IMarketInstrumentRepository>();
                var orchestrator = sp.GetRequiredService<IMarketDataOrchestrator>();
                var logger = sp.GetRequiredService<ILogger<SecurityListService>>();
                return new SecurityListService(subRepo, instrRepo, orchestrator, logger);
            });

            // Infrastructure - FIX Engine
            services.AddSingleton<IFixEngine>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<QuickFixEngine>>();
                var dataDictPath = Path.Combine(Directory.GetCurrentDirectory(), "FIX44_Volbroker.xml");
                var messageInSvc = sp.GetRequiredService<IMessageInService>();
                var tradeOrch = sp.GetRequiredService<IMessageInParserOrchestrator>();
                var secListSvc = sp.GetRequiredService<ISecurityListService>();
                var mdOrchestrator = sp.GetRequiredService<IMarketDataOrchestrator>();
                var senderProxy = sp.GetRequiredService<QuickFixSenderProxy>();
                var mdService = sp.GetRequiredService<IMarketDataService>();
                var quoteRequestSvc = sp.GetRequiredService<IQuoteRequestService>();
                var heartbeatNotifier = sp.GetRequiredService<ISessionHeartbeatNotifier>();
                var pushNotification = sp.GetRequiredService<IPushNotificationService>(); // ← lägg till

                return new QuickFixEngine(
                    logger,
                    dataDictPath,
                    messageInSvc,
                    tradeOrch,
                    secListSvc,
                    mdOrchestrator,
                    senderProxy,
                    mdService,
                    quoteRequestSvc,
                    heartbeatNotifier,
                    pushNotification); // ← lägg till
            });

            // Application Services
            services.AddSingleton<SessionManagementService>();

            services.AddSingleton<MessageProcessingService>(sp =>
            {
                var fixEngine = sp.GetRequiredService<IFixEngine>();
                var messageLogger = sp.GetRequiredService<IMessageLogger>();
                var logger = sp.GetRequiredService<ILogger<MessageProcessingService>>();
                return new MessageProcessingService(fixEngine, messageLogger, logger);
            });

            // Background Services
            services.AddHostedService<AckPollingService>();

            // Gateway heartbeat — skriver till fxvol.gateway_heartbeat var 2:e sekund
            services.AddSingleton<IGatewayHeartbeatRepository>(sp =>
                new GatewayHeartbeatRepository(fxvolConnectionString));

            services.AddSingleton<GatewayHeartbeatService>();
            services.AddHostedService(sp => sp.GetRequiredService<GatewayHeartbeatService>());
            services.AddSingleton<ISessionHeartbeatNotifier>(sp =>
                sp.GetRequiredService<GatewayHeartbeatService>());

            // PostMarker — FXOhub venue
            services.AddSingleton<PostMarkerAckChannel>();
            services.AddSingleton<IPostMarkerPayloadHandler>(sp =>
                new PostMarkerPayloadHandler(
                    sp.GetRequiredService<PostMarkerAckChannel>(),
                    sp.GetRequiredService<IMessageInService>(),
                    sp.GetRequiredService<IMessageInParserOrchestrator>(),
                    sp.GetRequiredService<ILogger<PostMarkerPayloadHandler>>()));
            services.AddSingleton<IPostMarkerSession, PostMarkerSession>();
            services.AddSingleton<IPostMarkerRepository>(sp =>
                new PostMarkerRepository(stpConnectionString));
            services.AddHostedService<PostMarkerHostedService>();
            services.AddHostedService<PostMarkerAcceptPollingService>();

            // ViewModels
            services.AddTransient<SessionListViewModel>();
            services.AddTransient<MainViewModel>();

            // Views
            services.AddTransient<MainWindow>(sp =>
            {
                var mainViewModel = sp.GetRequiredService<MainViewModel>();
                var window = new MainWindow
                {
                    DataContext = mainViewModel
                };
                return window;
            });

            // Logging
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog();
            });

            var path = System.Configuration.ConfigurationManager.AppSettings["AppConfigPath"];
            Log.Information("AppConfigPath = {Path}", path);

            // Quote Request — repository + service
            services.AddSingleton<IQuoteRequestRepository>(sp =>
                new QuoteRequestRepository(fxvolConnectionString));

            services.AddSingleton<IQuoteRequestService>(sp =>
            {
                var quoteRepo = sp.GetRequiredService<IQuoteRequestRepository>();
                var instrRepo = sp.GetRequiredService<IMarketInstrumentRepository>();
                var subRepo = sp.GetRequiredService<IMarketSubscriptionRepository>();
                var logger = sp.GetRequiredService<ILogger<QuoteRequestService>>();
                return new QuoteRequestService(quoteRepo, instrRepo, subRepo, logger);
            });

            services.AddSingleton<IPushNotificationService, PushoverNotificationService>();
            services.AddHostedService<FixEngineHostedService>();
        }

        /// <summary>
        /// Augments a MySQL connection string with pool settings suited for a
        /// high-frequency WPF + FIX-engine workload running in Release mode.
        /// </summary>
        private static string AugmentPoolSettings(string connectionString)
        {
            var builder = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(connectionString)
            {
                MinimumPoolSize = 5,
                MaximumPoolSize = 200,
                ConnectionTimeout = 30,
                DefaultCommandTimeout = 30
            };
            return builder.ConnectionString;
        }

        /// <summary>
        /// Sets connection pool size based on database role and expected concurrent load.
        /// ConnectionLifeTime ensures idle connections are recycled, allowing pool to shrink under low load.
        /// </summary>
        /// <remarks>
        /// Pool sizing rationale:
        /// - fix_config_prod (20): Sessions + heartbeat updates (low frequency, ~2-10 concurrent)
        /// - trade_stp (50): Trade capture + acknowledgments (medium frequency, ~15-20 concurrent)
        /// - VolManager (100): Market data snapshots/trades (high frequency, ~50-65 concurrent)
        /// 
        /// Total: 170 connections vs 600 before
        /// This leaves room for other applications within MySQL's max_connections limit.
        /// </remarks>
        private static string SetConnectionPoolSize(
            string connectionString,
            int maxPoolSize,
            int minPoolSize)
        {
            var builder = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(connectionString)
            {
                MinimumPoolSize = (uint)minPoolSize,
                MaximumPoolSize = (uint)maxPoolSize,
                ConnectionTimeout = 30,
                DefaultCommandTimeout = 30,
                ConnectionLifeTime = 30  // Idle connections stängs efter 30s — pool shrinks snabbt efter startup-spik
            };
            return builder.ConnectionString;
        }

        public IServiceProvider? Services => _host?.Services;
    }
}