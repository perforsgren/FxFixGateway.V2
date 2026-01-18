using System;
using System.IO;
using System.Windows;
using FxFixGateway.Application.BackgroundServices;
using FxFixGateway.Application.Services;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Infrastructure.Logging;
using FxFixGateway.Infrastructure.Persistence;
using FxFixGateway.UI.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using FxFixGateway.Infrastructure;

namespace FxFixGateway.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private IHost? _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Konfigurera Serilog först
            SerilogConfiguration.Configure();

            try
            {
                // Bygg Host med Dependency Injection
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

                // Starta Host
                _host.Start();

                // Skapa och visa MainWindow
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                // Logga ALLT till fil och console
                Log.Fatal(ex, "APPLICATION STARTUP FAILED");
                
                // Bygg fullständigt felmeddelande med alla inner exceptions
                var fullError = GetFullExceptionDetails(ex);
                
                // Skriv till fil också (ifall Serilog inte hunnit konfigurera)
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

            _host?.Dispose();
            SerilogConfiguration.Close();

            base.OnExit(e);
        }

        private void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Configuration
            var connectionString = configuration.GetConnectionString("GatewayDb")
                ?? "Server=localhost;Database=fix_config_dev;User=root;Password=yourpassword;";

            // LOGGA connection string (utan lösenord)
            var safeConnStr = System.Text.RegularExpressions.Regex.Replace(
                connectionString, @"Password=[^;]*", "Password=***");
            Log.Information("Using connection string: {ConnectionString}", safeConnStr);

            // Infrastructure - Repositories
            services.AddSingleton<ISessionRepository>(sp =>
                new SessionRepository(connectionString));

            services.AddSingleton<IMessageLogger>(sp =>
                new MessageLogRepository(connectionString));

            services.AddSingleton<IAckQueueRepository>(sp =>
                new AckQueueRepository(connectionString));

            // Infrastructure - FIX Engine (Mock för nu)
            services.AddSingleton<IFixEngine, MockFixEngine>();

            // Application Services
            services.AddSingleton<SessionManagementService>();
            services.AddSingleton<MessageProcessingService>();

            // Background Services
            services.AddHostedService<AckPollingService>();

            // ViewModels
            services.AddTransient<SessionListViewModel>();
            
            services.AddTransient<MainViewModel>(sp =>
            {
                var sessionManagementService = sp.GetRequiredService<SessionManagementService>();
                var sessionListViewModel = sp.GetRequiredService<SessionListViewModel>();
                var messageLogger = sp.GetRequiredService<IMessageLogger>();
                var logger = sp.GetRequiredService<ILogger<MainViewModel>>();
                
                return new MainViewModel(
                    sessionManagementService,
                    sessionListViewModel,
                    messageLogger,
                    logger);
            });

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
        }
    }
}
