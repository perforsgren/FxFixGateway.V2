using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxFixGateway.Application.Services;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Interfaces;

namespace FxFixGateway.UI.ViewModels
{
    public partial class SessionDetailViewModel : ObservableObject, IDisposable
    {
        private readonly SessionManagementService _sessionManagementService;

        [ObservableProperty]
        private SessionViewModel? _selectedSession;

        [ObservableProperty]
        private bool _isEditMode;

        [ObservableProperty]
        private MessageLogViewModel? _messageLog;

        [ObservableProperty]
        private AckQueueViewModel? _ackQueue;

        public SessionDetailViewModel(
            SessionManagementService sessionManagementService,
            IMessageLogger? messageLogger = null,
            IAckQueueRepository? ackQueueRepository = null,
            IFixEngine? fixEngine = null)
        {
            _sessionManagementService = sessionManagementService ?? throw new ArgumentNullException(nameof(sessionManagementService));

            // Pass fixEngine to MessageLogViewModel for real-time updates
            if (messageLogger != null)
            {
                _messageLog = new MessageLogViewModel(messageLogger, fixEngine);
            }

            if (ackQueueRepository != null && fixEngine != null)
            {
                _ackQueue = new AckQueueViewModel(ackQueueRepository, fixEngine);
            }
        }

        public bool CanStart => SelectedSession?.Status == SessionStatus.Stopped ||
                                SelectedSession?.Status == SessionStatus.Error;

        public bool CanStop => SelectedSession?.Status == SessionStatus.LoggedOn ||
                               SelectedSession?.Status == SessionStatus.Connecting ||
                               SelectedSession?.Status == SessionStatus.Starting;

        public bool CanRestart => SelectedSession?.Status == SessionStatus.LoggedOn;

        partial void OnSelectedSessionChanged(SessionViewModel? oldValue, SessionViewModel? newValue)
        {
            if (oldValue != null)
            {
                oldValue.PropertyChanged -= OnSelectedSessionPropertyChanged;
            }

            if (newValue != null)
            {
                newValue.PropertyChanged += OnSelectedSessionPropertyChanged;
            }

            RefreshButtonStates();

            if (newValue != null)
            {
                if (MessageLog != null)
                {
                    _ = MessageLog.LoadMessagesAsync(newValue.SessionKey);
                }

                if (AckQueue != null)
                {
                    _ = AckQueue.LoadAcksAsync(newValue.SessionKey);
                }
            }
        }

        private void OnSelectedSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SessionViewModel.Status))
            {
                RefreshButtonStates();
            }
        }

        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task StartAsync()
        {
            if (SelectedSession == null) return;

            try
            {
                await _sessionManagementService.StartSessionAsync(SelectedSession.SessionKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start session: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand(CanExecute = nameof(CanStop))]
        private async Task StopAsync()
        {
            if (SelectedSession == null) return;

            try
            {
                await _sessionManagementService.StopSessionAsync(SelectedSession.SessionKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to stop session: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand(CanExecute = nameof(CanRestart))]
        private async Task RestartAsync()
        {
            if (SelectedSession == null) return;

            try
            {
                await _sessionManagementService.RestartSessionAsync(SelectedSession.SessionKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to restart session: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshButtonStates()
        {
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanRestart));
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            RestartCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void Edit()
        {
            if (!CanStart)
            {
                MessageBox.Show("Stop the session before editing.", "Cannot Edit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            IsEditMode = true;
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            try
            {
                if (SelectedSession?.Session?.Configuration != null)
                {
                    await _sessionManagementService.SaveSessionConfigurationAsync(SelectedSession.Session.Configuration);
                    IsEditMode = false;
                    MessageBox.Show("Configuration saved successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void CancelEdit()
        {
            IsEditMode = false;
        }

        [RelayCommand]
        private async Task DeleteAsync()
        {
            if (SelectedSession == null) return;

            if (!CanStart)
            {
                MessageBox.Show("Stop the session before deleting.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete session '{SelectedSession.SessionKey}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _sessionManagementService.DeleteSessionAsync(SelectedSession.SessionKey);
                    MessageBox.Show("Session deleted successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete session: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public void Dispose()
        {
            MessageLog?.Dispose();
        }
    }
}
