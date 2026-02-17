using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.Domain.Utilities;
using FxFixGateway.Domain.ValueObjects;

namespace FxFixGateway.UI.ViewModels
{
    public partial class AckQueueViewModel : ObservableObject, IDisposable
    {
        private readonly IAckQueueRepository _ackQueueRepository;
        private readonly IFixEngine _fixEngine;
        private readonly DispatcherTimer _refreshTimer;
        private string? _currentSessionKey;
        private bool _disposed;

        [ObservableProperty]
        private ObservableCollection<AckEntryViewModel> _acks = new();

        [ObservableProperty]
        private AckEntryViewModel? _selectedAck;

        [ObservableProperty]
        private string _selectedFilter = "All";

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _isRetrying;

        [ObservableProperty]
        private int _pendingCount;

        [ObservableProperty]
        private int _sentTodayCount;

        [ObservableProperty]
        private int _failedCount;

        [ObservableProperty]
        private string _lastRefreshTime = "--";

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public ObservableCollection<string> FilterOptions { get; } = new()
        {
            "All", "Pending", "Sent", "Failed"
        };

        // Computed properties for UI state
        public bool HasFailedAcks => FailedCount > 0;
        public bool HasPendingAcks => PendingCount > 0;
        public bool CanSendSelected => SelectedAck != null
            && SelectedAck.Status == AckStatus.Pending
            && !string.IsNullOrEmpty(SelectedAck.InternTradeId);

        public AckQueueViewModel(IAckQueueRepository ackQueueRepository, IFixEngine fixEngine)
        {
            _ackQueueRepository = ackQueueRepository ?? throw new ArgumentNullException(nameof(ackQueueRepository));
            _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _refreshTimer.Tick += async (s, e) => await RefreshStatisticsAsync();
        }

        partial void OnSelectedAckChanged(AckEntryViewModel? value)
        {
            OnPropertyChanged(nameof(CanSendSelected));
        }

        partial void OnFailedCountChanged(int value)
        {
            OnPropertyChanged(nameof(HasFailedAcks));
        }

        partial void OnPendingCountChanged(int value)
        {
            OnPropertyChanged(nameof(HasPendingAcks));
        }

        public async Task LoadAcksAsync(string sessionKey)
        {
            if (string.IsNullOrEmpty(sessionKey))
            {
                Acks.Clear();
                _refreshTimer.Stop();
                return;
            }

            _currentSessionKey = sessionKey;

            try
            {
                IsLoading = true;
                StatusMessage = "Loading ACK queue...";
                await LoadDataAsync();
                StatusMessage = string.Empty;
                _refreshTimer.Start();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Failed to load ACKs: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadDataAsync()
        {
            if (string.IsNullOrEmpty(_currentSessionKey)) return;

            var stats = await _ackQueueRepository.GetStatisticsAsync(_currentSessionKey);

            PendingCount = stats.PendingCount;
            SentTodayCount = stats.SentTodayCount;
            FailedCount = stats.FailedCount;

            AckStatus? statusFilter = SelectedFilter switch
            {
                "Pending" => AckStatus.Pending,
                "Sent" => AckStatus.Sent,
                "Failed" => AckStatus.Failed,
                _ => null
            };

            var entries = await _ackQueueRepository.GetAcksBySessionAsync(_currentSessionKey, statusFilter, 200);
            var entryList = entries.ToList();

            Acks.Clear();
            foreach (var entry in entryList)
            {
                Acks.Add(new AckEntryViewModel(entry));
            }

            LastRefreshTime = DateTime.Now.ToString("HH:mm:ss");
        }

        private async Task RefreshStatisticsAsync()
        {
            if (string.IsNullOrEmpty(_currentSessionKey) || _disposed) return;

            try
            {
                var stats = await _ackQueueRepository.GetStatisticsAsync(_currentSessionKey);

                // Use full namespace to avoid conflict with FxFixGateway.Application
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var oldPending = PendingCount;
                    var oldSentToday = SentTodayCount;
                    var oldFailed = FailedCount;

                    PendingCount = stats.PendingCount;
                    SentTodayCount = stats.SentTodayCount;
                    FailedCount = stats.FailedCount;
                    LastRefreshTime = DateTime.Now.ToString("HH:mm:ss");

                    if (oldPending != PendingCount || oldSentToday != SentTodayCount || oldFailed != FailedCount)
                    {
                        _ = LoadDataWithErrorHandlingAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueViewModel] RefreshStatisticsAsync error: {ex.Message}");
            }
        }

        partial void OnSelectedFilterChanged(string value)
        {
            if (!string.IsNullOrEmpty(_currentSessionKey))
            {
                _ = LoadDataWithErrorHandlingAsync();
            }
        }

        private async Task LoadDataWithErrorHandlingAsync()
        {
            try
            {
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AckQueueViewModel] LoadDataAsync error: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (!string.IsNullOrEmpty(_currentSessionKey))
            {
                IsLoading = true;
                await LoadDataAsync();
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task RetryFailedAsync()
        {
            if (string.IsNullOrEmpty(_currentSessionKey)) return;

            try
            {
                IsRetrying = true;
                var failedAcks = Acks.Where(a => a.Status == AckStatus.Failed).ToList();

                if (failedAcks.Count == 0)
                {
                    MessageBox.Show("No failed ACKs to retry.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                foreach (var ack in failedAcks)
                {
                    await _ackQueueRepository.UpdateAckStatusAsync(ack.TradeId, AckStatus.Pending, null);
                }

                MessageBox.Show($"Reset {failedAcks.Count} failed ACKs to pending.", "Retry Failed", MessageBoxButton.OK, MessageBoxImage.Information);

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to retry: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsRetrying = false;
            }
        }

        [RelayCommand]
        private async Task RetrySingleAsync(AckEntryViewModel? ack)
        {
            ack ??= SelectedAck;
            if (ack == null || ack.Status != AckStatus.Failed) return;

            try
            {
                await _ackQueueRepository.UpdateAckStatusAsync(ack.TradeId, AckStatus.Pending, null);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to retry: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task SendNowAsync()
        {
            if (SelectedAck == null || SelectedAck.Status != AckStatus.Pending) return;

            if (string.IsNullOrEmpty(SelectedAck.InternTradeId))
            {
                MessageBox.Show(
                    $"Cannot send ACK for {SelectedAck.TradeReportId} - InternTradeId is missing.\n" +
                    "The trade may not have been processed by the downstream system yet.",
                    "Missing Data",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                var arMessage = FixMessageBuilder.BuildTradeCaptureReportAck(SelectedAck.TradeReportId, SelectedAck.InternTradeId);
                await _fixEngine.SendMessageAsync(_currentSessionKey!, arMessage);

                await _ackQueueRepository.UpdateAckStatusAsync(SelectedAck.TradeId, AckStatus.Sent, DateTime.UtcNow);

                await _ackQueueRepository.InsertWorkflowEventAsync(
                    SelectedAck.TradeId,
                    "FIX_ACK",
                    "FIX_ACK_SENT",
                    $"FIX acknowledgment sent (manual)\nTradeReportID: {SelectedAck.TradeReportId}\nMX3 trade ID: {SelectedAck.InternTradeId}");

                MessageBox.Show($"ACK sent for {SelectedAck.TradeReportId}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send ACK: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task CancelAckAsync()
        {
            if (SelectedAck == null || SelectedAck.Status != AckStatus.Pending) return;

            var result = MessageBox.Show(
                $"Cancel ACK for trade {SelectedAck.TradeReportId}?",
                "Confirm Cancel",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _ackQueueRepository.UpdateAckStatusAsync(SelectedAck.TradeId, AckStatus.Failed, null);
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to cancel ACK: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _refreshTimer.Stop();
        }
    }

    public partial class AckEntryViewModel : ObservableObject
    {
        private readonly AckEntry _entry;

        public AckEntryViewModel(AckEntry entry)
        {
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        }

        public long TradeId => _entry.TradeId;
        public string SessionKey => _entry.SessionKey;
        public string TradeReportId => _entry.TradeReportId;
        public string InternTradeId => _entry.InternTradeId;
        public AckStatus Status => _entry.Status;
        public DateTime CreatedUtc => _entry.CreatedUtc;
        public DateTime? SentUtc => _entry.SentUtc;

        public string StatusText => Status.ToString();
        public string CreatedFormatted => CreatedUtc.ToLocalTime().ToString("HH:mm:ss");
        public string SentFormatted => SentUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "--";

        public string StatusColor => Status switch
        {
            AckStatus.Pending => "#FFF9C4",
            AckStatus.Sent => "#C8E6C9",
            AckStatus.Failed => "#FFCDD2",
            _ => "#FFFFFF"
        };

        public string StatusIcon => Status switch
        {
            AckStatus.Pending => "⏳",
            AckStatus.Sent => "✓",
            AckStatus.Failed => "✗",
            _ => ""
        };

        public bool CanRetry => Status == AckStatus.Failed;
        public bool CanSendNow => Status == AckStatus.Pending && !string.IsNullOrEmpty(InternTradeId);
        public bool IsMissingInternTradeId => Status == AckStatus.Pending && string.IsNullOrEmpty(InternTradeId);

        public string WaitingTime
        {
            get
            {
                if (Status != AckStatus.Pending) return string.Empty;
                var elapsed = DateTime.UtcNow - CreatedUtc;
                if (elapsed.TotalMinutes >= 1)
                    return $"waiting {(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
                return $"waiting {(int)elapsed.TotalSeconds}s";
            }
        }
    }
}