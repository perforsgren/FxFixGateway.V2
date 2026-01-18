using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxFixGateway.Application.Services;
using FxFixGateway.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.UI.ViewModels
{
    /// <summary>
    /// MainViewModel - huvudorchestrator för applikationen.
    /// </summary>
    public partial class MainViewModel : ViewModelBase
    {
        private readonly SessionManagementService _sessionManagementService;
        private readonly ILogger<MainViewModel> _logger;

        [ObservableProperty]
        private SessionListViewModel _sessionList;

        [ObservableProperty]
        private SessionDetailViewModel _sessionDetail;

        [ObservableProperty]
        private string _statusBarText = "Ready";

        [ObservableProperty]
        private bool _isLoading;

        public MainViewModel(
            SessionManagementService sessionManagementService,
            SessionListViewModel sessionListViewModel,
            IMessageLogger messageLogger,
            IAckQueueRepository ackQueueRepository,
            IFixEngine fixEngine,
            ILogger<MainViewModel> logger)
        {
            _sessionManagementService = sessionManagementService ?? throw new ArgumentNullException(nameof(sessionManagementService));
            _sessionList = sessionListViewModel ?? throw new ArgumentNullException(nameof(sessionListViewModel));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Skapa SessionDetailViewModel med alla dependencies
            _sessionDetail = new SessionDetailViewModel(
                _sessionManagementService, 
                messageLogger, 
                ackQueueRepository, 
                fixEngine);

            // Lyssna på session selection
            _sessionList.PropertyChanged += SessionListOnPropertyChanged;
        }

        /// <summary>
        /// Initierar applikationen - laddar sessions från DB och startar enabled.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                StatusBarText = "Loading sessions...";

                _logger.LogInformation("Initializing Main ViewModel...");

                // Initiera SessionManagementService (laddar från DB, startar enabled sessions)
                await _sessionManagementService.InitializeAsync();

                // Ladda sessions till UI
                await _sessionList.LoadSessionsAsync();

                var sessionCount = _sessionList.Sessions.Count;
                var runningCount = _sessionList.RunningSessionsCount;

                StatusBarText = $"{sessionCount} sessions loaded, {runningCount} running";

                _logger.LogInformation("Main ViewModel initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Main ViewModel");
                StatusBarText = "Error loading sessions";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void SessionListOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SessionListViewModel.SelectedSession))
            {
                OnSessionSelected(_sessionList.SelectedSession);
            }
        }

        private void OnSessionSelected(SessionViewModel? session)
        {
            SessionDetail.SelectedSession = session;
        }

        [RelayCommand]
        private async Task RefreshAll()
        {
            await _sessionList.LoadSessionsAsync();
            StatusBarText = $"Refreshed at {DateTime.Now:HH:mm:ss}";
        }

        [RelayCommand]
        private void Exit()
        {
            _logger.LogInformation("Application exit requested");
            System.Windows.Application.Current.Shutdown();
        }
    }
}
