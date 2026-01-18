using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxFixGateway.Application.Services;
using FxFixGateway.Domain.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FxFixGateway.UI.ViewModels
{
    public partial class SessionListViewModel : ObservableObject
    {
        private readonly SessionManagementService _sessionManagementService;
        private readonly IFixEngine _fixEngine;

        [ObservableProperty]
        private ObservableCollection<SessionViewModel> _sessions = new ObservableCollection<SessionViewModel>();

        [ObservableProperty]
        private SessionViewModel _selectedSession;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _showOnlyEnabled = false;

        // Alias for XAML compatibility
        public string FilterText
        {
            get => SearchText;
            set => SearchText = value;
        }

        public int RunningSessionsCount => Sessions.Count(s =>
            s.Status == FxFixGateway.Domain.Enums.SessionStatus.LoggedOn ||
            s.Status == FxFixGateway.Domain.Enums.SessionStatus.Connecting ||
            s.Status == FxFixGateway.Domain.Enums.SessionStatus.Starting);

        public event EventHandler<SessionViewModel?>? SessionSelected;

        public SessionListViewModel(
            SessionManagementService sessionManagementService,
            IFixEngine fixEngine)  // ← LÄGG TILL
        {
            _sessionManagementService = sessionManagementService ?? throw new ArgumentNullException(nameof(sessionManagementService));
            _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));  // ← LÄGG TILL
        }

        partial void OnSelectedSessionChanged(SessionViewModel? value)
        {
            SessionSelected?.Invoke(this, value);
        }

        public async Task LoadSessionsAsync()
        {
            try
            {
                var sessions = _sessionManagementService.GetAllSessions();

                Sessions.Clear();
                foreach (var session in sessions)
                {
                    var viewModel = new SessionViewModel(session, _fixEngine);  // ← ANVÄND _fixEngine
                    Sessions.Add(viewModel);
                }
            }
            catch (Exception ex)
            {
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
