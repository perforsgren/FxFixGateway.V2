using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxFixGateway.Application.Services;

namespace FxFixGateway.UI.ViewModels
{
    public partial class SessionListViewModel : ObservableObject
    {
        private readonly SessionManagementService _sessionManagementService;

        [ObservableProperty]
        private ObservableCollection<SessionViewModel> _sessions = new ObservableCollection<SessionViewModel>();

        [ObservableProperty]
        private SessionViewModel _selectedSession;

        [ObservableProperty]
        private string _searchText = string.Empty;

        public SessionListViewModel(SessionManagementService sessionManagementService)
        {
            _sessionManagementService = sessionManagementService ?? throw new ArgumentNullException(nameof(sessionManagementService));
        }

        public async Task LoadSessionsAsync()
        {
            try
            {
                var sessions = _sessionManagementService.GetAllSessions();

                Sessions.Clear();
                foreach (var session in sessions)
                {
                    var viewModel = new SessionViewModel(session);
                    Sessions.Add(viewModel);
                }
            }
            catch (Exception ex)
            {
                // Log error
                System.Diagnostics.Debug.WriteLine($"Failed to load sessions: {ex.Message}");
                throw;
            }
        }

        [RelayCommand]
        private void AddSession()
        {
            // TODO: Implement add session dialog
            System.Windows.MessageBox.Show("Add session functionality not yet implemented");
        }

        [RelayCommand]
        private void FilterSessions()
        {
            // TODO: Implement filtering
        }

        partial void OnSearchTextChanged(string value)
        {
            FilterSessions();
        }
    }
}
