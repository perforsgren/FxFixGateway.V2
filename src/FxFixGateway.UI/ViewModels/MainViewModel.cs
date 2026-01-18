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

            _sessionDetail = new SessionDetailViewModel(
                _sessionManagementService,
                messageLogger,
                ackQueueRepository,
                fixEngine);

            // ÄNDRAT: Använd SessionSelected event istället för PropertyChanged
            _sessionList.SessionSelected += OnSessionSelected;
        }

        public async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                StatusBarText = "Loading sessions...";

                _logger.LogInformation("Initializing Main ViewModel...");

                await _sessionManagementService.InitializeAsync();
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

        // ÄNDRAT: Ta emot event från SessionListViewModel
        private void OnSessionSelected(object? sender, SessionViewModel? session)
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
