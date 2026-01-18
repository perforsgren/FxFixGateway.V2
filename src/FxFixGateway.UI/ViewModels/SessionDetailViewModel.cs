using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxFixGateway.Application.Services;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Interfaces;

namespace FxFixGateway.UI.ViewModels
{
    public partial class SessionDetailViewModel : ObservableObject
    {
        private readonly SessionManagementService _sessionManagementService;

        [ObservableProperty]
        private SessionViewModel? _selectedSession;

        [ObservableProperty]
        private bool _isEditMode;

        [ObservableProperty]
        private MessageLogViewModel? _messageLog;

        public SessionDetailViewModel(
            SessionManagementService sessionManagementService,
            IMessageLogger? messageLogger = null)
        {
            _sessionManagementService = sessionManagementService ?? throw new ArgumentNullException(nameof(sessionManagementService));
            
            // Skapa MessageLogViewModel om messageLogger finns
            if (messageLogger != null)
            {
                _messageLog = new MessageLogViewModel(messageLogger);
            }
        }

        // Computed properties for button states
        public bool CanStart => SelectedSession?.Status == SessionStatus.Stopped || 
                                SelectedSession?.Status == SessionStatus.Error;
        
        public bool CanStop => SelectedSession?.Status == SessionStatus.LoggedOn || 
                               SelectedSession?.Status == SessionStatus.Connecting ||
                               SelectedSession?.Status == SessionStatus.Starting;
        
        public bool CanRestart => SelectedSession?.Status == SessionStatus.LoggedOn;

        partial void OnSelectedSessionChanged(SessionViewModel? value)
        {
            // Notify that button states may have changed
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanRestart));

            // Load messages for the selected session
            if (value != null && MessageLog != null)
            {
                _ = MessageLog.LoadMessagesAsync(value.SessionKey);
            }
        }

        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task StartAsync()
        {
            if (SelectedSession == null) return;

            try
            {
                await _sessionManagementService.StartSessionAsync(SelectedSession.SessionKey);
                RefreshButtonStates();
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
                RefreshButtonStates();
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
                RefreshButtonStates();
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
            if (!CanStart) // Can only edit when stopped
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
            // TODO: Reload original values
        }

        [RelayCommand]
        private async Task DeleteAsync()
        {
            if (SelectedSession == null) return;

            if (!CanStart) // Can only delete when stopped
            {
                MessageBox.Show("Stop the session before deleting.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Är du säker på att du vill ta bort sessionen '{SelectedSession.SessionKey}'?",
                "Bekräfta borttagning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _sessionManagementService.DeleteSessionAsync(SelectedSession.SessionKey);
                    MessageBox.Show("Sessionen har raderats", "Framgång", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Misslyckades med att radera session: {ex.Message}", "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
