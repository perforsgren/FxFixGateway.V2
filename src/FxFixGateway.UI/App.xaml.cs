using FxFixGateway.Application.BackgroundServices;
using FxFixGateway.Application.Services;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Infrastructure.Logging;
using FxFixGateway.Infrastructure.Persistence;
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

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
            {
                // Ignorera känd race condition i QuickFIX/n vid disconnect
                if (args.Exception is ObjectDisposedException ode &&
                    args.Exception.StackTrace?.Contains("SocketInitiatorThread") == true)
                    return;

                if (args.Exception.Source?.Contains("QuickFix") == true ||
                    args.Exception.StackTrace?.Contains("QuickFix") == true)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[QuickFIX FirstChance] {args.Exception.GetType().Name}: {args.Exception.Message}\n{args.Exception.StackTrace}");
                }
            };

            base.OnStartup(e);

            SerilogConfiguration.Configure();

#if DEBUG
            // Suppress WPF binding errors in debug output (MaterialDesign HintAssist issue)
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Critical;
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

                _host.Start();

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

        protected override async void OnExit(ExitEventArgs e)
        {
            Log.Information("Application shutting down...");

            try
            {
                if (_host != null)
                {
                    var fixEngine = _host.Services.GetService<IFixEngine>();
                    if (fixEngine != null)
                    {
                        Log.Information("Shutting down FIX engine gracefully...");
                        await fixEngine.ShutdownAsync();
                        await Task.Delay(1000); // Give QuickFIX time to send logout messages
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during graceful shutdown");
            }

            _host?.Dispose();
            SerilogConfiguration.Close();

            base.OnExit(e);
        }

        private void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = AppDbConfig.GetConnectionString("fix_config_dev");

            var safeConnStr = System.Text.RegularExpressions.Regex.Replace(
                connectionString, @"Password=[^;]*", "Password=***");
            Log.Information("Using GatewayDb connection string: {ConnectionString}", safeConnStr);

            var stpConnectionString = AppDbConfig.GetConnectionString("trade_stp");

            var safeSTPConnStr = System.Text.RegularExpressions.Regex.Replace(
                stpConnectionString, @"Password=[^;]*", "Password=***");
            Log.Information("Using STP connection string: {ConnectionString}", safeSTPConnStr);

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
                    new FenicsFixAeParser(lookupRepo)
                };

                return new MessageInParserOrchestrator(messageInRepo, stpRepo, parsers);
            });

            // Proxy registreras som singleton — QuickFixEngine sätter inner-sender efter InitializeAsync
            services.AddSingleton<QuickFixSenderProxy>();
            services.AddSingleton<IMarketDataSubscriber>(sp => sp.GetRequiredService<QuickFixSenderProxy>());

            // Market Data — connection string till fxvol
            var fxvolConnectionString = AppDbConfig.GetConnectionString("VolManager");

            // Market Data — repositories
            services.AddSingleton<IMarketSubscriptionRepository>(sp =>
                new MarketSubscriptionRepository(connectionString));      // fix_config_dev

            services.AddSingleton<IMarketInstrumentRepository>(sp =>
                new MarketInstrumentRepository(fxvolConnectionString));  // fxvol

            services.AddSingleton<IMarketDataSnapshotRepository>(sp =>
                new MarketDataSnapshotRepository(fxvolConnectionString));  // fxvol

            services.AddSingleton<IMarketDataService>(sp =>
            {
                var snapshotRepo = sp.GetRequiredService<IMarketDataSnapshotRepository>();
                var instrRepo    = sp.GetRequiredService<IMarketInstrumentRepository>();
                var logger       = sp.GetRequiredService<ILogger<MarketDataService>>();
                return new MarketDataService(snapshotRepo, instrRepo, logger);
            });

            // Market Data — orchestration
            services.AddSingleton<IMarketDataOrchestrator>(sp =>
            {
                var subscriber   = sp.GetRequiredService<IMarketDataSubscriber>();
                var instrRepo    = sp.GetRequiredService<IMarketInstrumentRepository>();
                var logger       = sp.GetRequiredService<ILogger<MarketDataOrchestrator>>();
                return new MarketDataOrchestrator(subscriber, instrRepo, logger);
            });

            services.AddSingleton<ISecurityListService>(sp =>
            {
                var subRepo      = sp.GetRequiredService<IMarketSubscriptionRepository>();
                var instrRepo    = sp.GetRequiredService<IMarketInstrumentRepository>();
                var orchestrator = sp.GetRequiredService<IMarketDataOrchestrator>();
                var logger       = sp.GetRequiredService<ILogger<SecurityListService>>();
                return new SecurityListService(subRepo, instrRepo, orchestrator, logger);
            });

            // Infrastructure - FIX Engine
            services.AddSingleton<IFixEngine>(sp =>
            {
                var logger         = sp.GetRequiredService<ILogger<QuickFixEngine>>();
                var dataDictPath   = Path.Combine(Directory.GetCurrentDirectory(), "FIX44_Volbroker.xml");
                var messageInSvc   = sp.GetRequiredService<FxTradeHub.Domain.Services.IMessageInService>();
                var tradeOrch      = sp.GetRequiredService<FxTradeHub.Domain.Parsing.IMessageInParserOrchestrator>();
                var secListSvc     = sp.GetRequiredService<ISecurityListService>();
                var mdOrchestrator = sp.GetRequiredService<IMarketDataOrchestrator>();
                var senderProxy    = sp.GetRequiredService<QuickFixSenderProxy>();
                var mdService      = sp.GetRequiredService<IMarketDataService>();   // ← NY

                return new QuickFixEngine(
                    logger,
                    dataDictPath,
                    messageInSvc,
                    tradeOrch,
                    secListSvc,
                    mdOrchestrator,
                    senderProxy,
                    mdService);   // ← NY
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
        }


    }
}
