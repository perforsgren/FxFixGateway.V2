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
                MessageBox.Show($"Failed to start application: {ex.Message}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Log.Fatal(ex, "Application startup failed");
                Shutdown();
            }
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

            // Infrastructure - Repositories
            services.AddSingleton<ISessionRepository>(sp =>
                new SessionRepository(connectionString));

            services.AddSingleton<IMessageLogger>(sp =>
                new MessageLogRepository(connectionString));

            services.AddSingleton<IAckQueueRepository>(sp =>
                new AckQueueRepository(connectionString));

            // Infrastructure - FIX Engine (stub för nu - kommentera bort tills QuickFIX är klar)
            // services.AddSingleton<IFixEngine, QuickFixEngine>();

            // Temporary: Använd Mock istället
            services.AddSingleton<IFixEngine>(sp => new MockFixEngine());

            // Application Services
            services.AddSingleton<SessionManagementService>();
            services.AddSingleton<MessageProcessingService>();

            // Background Services
            services.AddHostedService<AckPollingService>();

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<SessionListViewModel>();

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
