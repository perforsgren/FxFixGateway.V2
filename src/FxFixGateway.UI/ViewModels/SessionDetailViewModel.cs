using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxFixGateway.Application.Services;

namespace FxFixGateway.UI.ViewModels
{
    public partial class SessionDetailViewModel : ObservableObject
    {
        private readonly SessionManagementService _sessionManagementService;

        [ObservableProperty]
        private SessionViewModel _selectedSession;

        [ObservableProperty]
        private bool _isEditMode;

        public SessionDetailViewModel(SessionManagementService sessionManagementService)
        {
            _sessionManagementService = sessionManagementService ?? throw new ArgumentNullException(nameof(sessionManagementService));
        }

        [RelayCommand]
        private void Edit()
        {
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
    }
}
