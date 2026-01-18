using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.UI.ViewModels
{
    public partial class MessageLogViewModel : ObservableObject
    {
        private readonly IMessageLogger _messageLogger;
        private string? _currentSessionKey;

        [ObservableProperty]
        private ObservableCollection<MessageLogEntryViewModel> _messages = new();

        [ObservableProperty]
        private MessageLogEntryViewModel? _selectedMessage;

        [ObservableProperty]
        private string _filterText = string.Empty;

        [ObservableProperty]
        private string _selectedDirection = "All";

        [ObservableProperty]
        private string _selectedMsgType = "All";

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _rawMessageText = string.Empty;

        public ObservableCollection<string> DirectionOptions { get; } = new()
        {
            "All", "Incoming", "Outgoing"
        };

        public ObservableCollection<string> MsgTypeOptions { get; } = new()
        {
            "All", "AE", "AR", "0", "A", "5", "8"
        };

        public MessageLogViewModel(IMessageLogger messageLogger)
        {
            _messageLogger = messageLogger ?? throw new ArgumentNullException(nameof(messageLogger));
        }

        public async Task LoadMessagesAsync(string sessionKey)
        {
            if (string.IsNullOrEmpty(sessionKey))
            {
                Messages.Clear();
                return;
            }

            _currentSessionKey = sessionKey;

            try
            {
                IsLoading = true;

                var entries = await _messageLogger.GetRecentAsync(sessionKey, 500);
                
                Messages.Clear();
                foreach (var entry in entries)
                {
                    Messages.Add(new MessageLogEntryViewModel(entry));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load messages: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        partial void OnSelectedMessageChanged(MessageLogEntryViewModel? value)
        {
            RawMessageText = value?.RawText ?? string.Empty;
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (!string.IsNullOrEmpty(_currentSessionKey))
            {
                await LoadMessagesAsync(_currentSessionKey);
            }
        }

        [RelayCommand]
        private void CopyRawMessage()
        {
            if (!string.IsNullOrEmpty(RawMessageText))
            {
                try
                {
                    Clipboard.SetText(RawMessageText);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to copy to clipboard: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private void ClearMessages()
        {
            Messages.Clear();
            RawMessageText = string.Empty;
        }

        public ObservableCollection<MessageLogEntryViewModel> FilteredMessages
        {
            get
            {
                var filtered = Messages.AsEnumerable();

                // Filter by direction
                if (SelectedDirection != "All")
                {
                    var direction = SelectedDirection == "Incoming" 
                        ? MessageDirection.Incoming 
                        : MessageDirection.Outgoing;
                    filtered = filtered.Where(m => m.Direction == direction);
                }

                // Filter by message type
                if (SelectedMsgType != "All")
                {
                    filtered = filtered.Where(m => m.MsgType == SelectedMsgType);
                }

                // Filter by text
                if (!string.IsNullOrWhiteSpace(FilterText))
                {
                    var search = FilterText.ToLowerInvariant();
                    filtered = filtered.Where(m => 
                        m.Summary.ToLowerInvariant().Contains(search) ||
                        m.MsgType.ToLowerInvariant().Contains(search) ||
                        m.RawText.ToLowerInvariant().Contains(search));
                }

                return new ObservableCollection<MessageLogEntryViewModel>(filtered);
            }
        }

        partial void OnFilterTextChanged(string value) => OnPropertyChanged(nameof(FilteredMessages));
        partial void OnSelectedDirectionChanged(string value) => OnPropertyChanged(nameof(FilteredMessages));
        partial void OnSelectedMsgTypeChanged(string value) => OnPropertyChanged(nameof(FilteredMessages));
    }

    /// <summary>
    /// ViewModel för en enskild meddelandeloggpost.
    /// </summary>
    public partial class MessageLogEntryViewModel : ObservableObject
    {
        private readonly MessageLogEntry _entry;

        public MessageLogEntryViewModel(MessageLogEntry entry)
        {
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        }

        public DateTime Timestamp => _entry.Timestamp;
        public string TimeFormatted => _entry.Timestamp.ToString("HH:mm:ss.fff");
        public MessageDirection Direction => _entry.Direction;
        public string DirectionText => _entry.Direction == MessageDirection.Incoming ? "IN" : "OUT";
        public string MsgType => _entry.MsgType;
        public string Summary => _entry.Summary;
        public string RawText => _entry.RawText;

        public string DirectionColor => _entry.Direction == MessageDirection.Incoming 
            ? "#E3F2FD"  // Light blue
            : "#E8F5E9"; // Light green
    }
}