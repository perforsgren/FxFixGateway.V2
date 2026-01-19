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
        private int _pendingCount;

        [ObservableProperty]
        private int _sentTodayCount;

        [ObservableProperty]
        private int _failedCount;

        public ObservableCollection<string> FilterOptions { get; } = new()
        {
            "All", "Pending", "Sent", "Failed"
        };

        public AckQueueViewModel(IAckQueueRepository ackQueueRepository, IFixEngine fixEngine)
        {
            _ackQueueRepository = ackQueueRepository ?? throw new ArgumentNullException(nameof(ackQueueRepository));
            _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));

            // Auto-refresh timer (every 5 seconds)
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _refreshTimer.Tick += async (s, e) => await RefreshStatisticsAsync();
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
                await LoadDataAsync();

                // Start auto-refresh
                _refreshTimer.Start();
            }
            catch (Exception ex)
            {
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

            // Hämta statistik
            var stats = await _ackQueueRepository.GetStatisticsAsync(_currentSessionKey);
            PendingCount = stats.PendingCount;
            SentTodayCount = stats.SentTodayCount;
            FailedCount = stats.FailedCount;

            // Hämta ACKs med filter
            AckStatus? statusFilter = SelectedFilter switch
            {
                "Pending" => AckStatus.Pending,
                "Sent" => AckStatus.Sent,
                "Failed" => AckStatus.Failed,
                _ => null
            };

            var entries = await _ackQueueRepository.GetAcksBySessionAsync(_currentSessionKey, statusFilter, 200);

            Acks.Clear();
            foreach (var entry in entries)
            {
                Acks.Add(new AckEntryViewModel(entry));
            }
        }

        private async Task RefreshStatisticsAsync()
        {
            if (string.IsNullOrEmpty(_currentSessionKey) || _disposed) return;

            try
            {
                var stats = await _ackQueueRepository.GetStatisticsAsync(_currentSessionKey);

                // Only update on UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var oldPending = PendingCount;
                    PendingCount = stats.PendingCount;
                    SentTodayCount = stats.SentTodayCount;
                    FailedCount = stats.FailedCount;

                    // If pending count changed, refresh the list too
                    if (oldPending != PendingCount)
                    {
                        _ = LoadDataAsync();
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
                _ = LoadDataAsync();
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
                var failedAcks = Acks.Where(a => a.Status == AckStatus.Failed).ToList();

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
        }

        [RelayCommand]
        private async Task SendNowAsync()
        {
            if (SelectedAck == null || SelectedAck.Status != AckStatus.Pending) return;

            try
            {
                // Bygg och skicka ACK-meddelande
                var arMessage = BuildAckMessage(SelectedAck);
                await _fixEngine.SendMessageAsync(_currentSessionKey!, arMessage);

                // Uppdatera status
                await _ackQueueRepository.UpdateAckStatusAsync(SelectedAck.TradeId, AckStatus.Sent, DateTime.UtcNow);

                // Skriv workflow event
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
                    // Markera som Failed (eller lägg till en Cancelled status)
                    await _ackQueueRepository.UpdateAckStatusAsync(SelectedAck.TradeId, AckStatus.Failed, null);
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to cancel ACK: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string BuildAckMessage(AckEntryViewModel ack)
        {
            const char SOH = '\x01';
            var body = new System.Text.StringBuilder();

            body.Append($"35=AR{SOH}");
            body.Append($"571={ack.TradeReportId}{SOH}");
            body.Append($"939=0{SOH}");

            if (!string.IsNullOrEmpty(ack.InternTradeId))
            {
                body.Append($"818={ack.InternTradeId}{SOH}");
            }

            var bodyStr = body.ToString();
            var header = $"8=FIX.4.4{SOH}9={bodyStr.Length}{SOH}";
            var messageWithoutChecksum = header + bodyStr;

            var sum = 0;
            foreach (var c in messageWithoutChecksum)
            {
                sum += (byte)c;
            }
            var checksum = sum % 256;

            return $"{messageWithoutChecksum}10={checksum:D3}{SOH}";
        }

        public void Dispose()
        {
            _disposed = true;
            _refreshTimer.Stop();
        }
    }

    /// <summary>
    /// ViewModel för en enskild ACK-post.
    /// </summary>
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
        public string CreatedFormatted => CreatedUtc.ToString("HH:mm:ss");
        public string SentFormatted => SentUtc?.ToString("HH:mm:ss") ?? "--";

        public string StatusColor => Status switch
        {
            AckStatus.Pending => "#FFF9C4",  // Yellow
            AckStatus.Sent => "#C8E6C9",     // Green
            AckStatus.Failed => "#FFCDD2",   // Red
            _ => "#FFFFFF"
        };

        public string WaitingTime
        {
            get
            {
                if (Status != AckStatus.Pending) return string.Empty;
                var elapsed = DateTime.UtcNow - CreatedUtc;
                return $"waiting {(int)elapsed.TotalSeconds}s";
            }
        }

        public string TimeFormatted => CreatedUtc.ToString("HH:mm:ss");
        public string DirectionText => SentUtc.HasValue ? "OUT" : "IN";
        public string MsgType => "ACK"; // Assuming the message type is ACK for all entries

        public string Summary => $"TradeReportID: {TradeReportId}, Status: {StatusText}";
    }
}
