using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Events;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.UI.ViewModels
{
    public partial class MessageLogViewModel : ObservableObject, IDisposable
    {
        private readonly IMessageLogger _messageLogger;
        private readonly IFixEngine? _fixEngine;
        private string? _currentSessionKey;
        private bool _disposed;

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

        public MessageLogViewModel(IMessageLogger messageLogger, IFixEngine? fixEngine = null)
        {
            _messageLogger = messageLogger ?? throw new ArgumentNullException(nameof(messageLogger));
            _fixEngine = fixEngine;

            // Subscribe to real-time events if engine is available
            if (_fixEngine != null)
            {
                _fixEngine.MessageReceived += OnMessageReceived;
                _fixEngine.MessageSent += OnMessageSent;
            }
        }

        private void OnMessageReceived(object? sender, MessageReceivedEvent e)
        {
            if (_disposed || e.SessionKey != _currentSessionKey) return;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;

                var entry = new MessageLogEntry(
                    e.ReceivedAtUtc,
                    MessageDirection.Incoming,
                    e.MsgType,
                    GetMessageSummary(e.MsgType),
                    e.RawMessage);

                // Insert at top (newest first)
                Messages.Insert(0, new MessageLogEntryViewModel(entry));
                OnPropertyChanged(nameof(FilteredMessages));
            });
        }

        private void OnMessageSent(object? sender, MessageSentEvent e)
        {
            if (_disposed || e.SessionKey != _currentSessionKey) return;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;

                var entry = new MessageLogEntry(
                    e.SentAtUtc,
                    MessageDirection.Outgoing,
                    e.MsgType,
                    GetMessageSummary(e.MsgType),
                    e.RawMessage);

                // Insert at top (newest first)
                Messages.Insert(0, new MessageLogEntryViewModel(entry));
                OnPropertyChanged(nameof(FilteredMessages));
            });
        }

        private string GetMessageSummary(string msgType)
        {
            return msgType switch
            {
                "0" => "Heartbeat",
                "A" => "Logon",
                "5" => "Logout",
                "AE" => "TradeCaptureReport",
                "AR" => "TradeCaptureReportAck",
                "8" => "ExecutionReport",
                "1" => "TestRequest",
                "2" => "ResendRequest",
                "3" => "Reject",
                "4" => "SequenceReset",
                _ => $"MsgType {msgType}"
            };
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
                
                OnPropertyChanged(nameof(FilteredMessages));
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
            OnPropertyChanged(nameof(FilteredMessages));
        }

        public ObservableCollection<MessageLogEntryViewModel> FilteredMessages
        {
            get
            {
                var filtered = Messages.AsEnumerable();

                if (SelectedDirection != "All")
                {
                    var direction = SelectedDirection == "Incoming" 
                        ? MessageDirection.Incoming 
                        : MessageDirection.Outgoing;
                    filtered = filtered.Where(m => m.Direction == direction);
                }

                if (SelectedMsgType != "All")
                {
                    filtered = filtered.Where(m => m.MsgType == SelectedMsgType);
                }

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

        public void Dispose()
        {
            _disposed = true;
            if (_fixEngine != null)
            {
                _fixEngine.MessageReceived -= OnMessageReceived;
                _fixEngine.MessageSent -= OnMessageSent;
            }
        }
    }

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
            ? "#E3F2FD"
            : "#E8F5E9";
    }
}