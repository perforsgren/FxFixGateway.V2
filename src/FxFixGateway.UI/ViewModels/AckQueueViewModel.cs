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
    public partial class AckQueueViewModel : ObservableObject
    {
        private readonly IAckQueueRepository _ackQueueRepository;
        private readonly IFixEngine _fixEngine;
        private string? _currentSessionKey;

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
        }

        public async Task LoadAcksAsync(string sessionKey)
        {
            if (string.IsNullOrEmpty(sessionKey))
            {
                Acks.Clear();
                return;
            }

            _currentSessionKey = sessionKey;

            try
            {
                IsLoading = true;

                // Hämta statistik
                var stats = await _ackQueueRepository.GetStatisticsAsync(sessionKey);
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

                var entries = await _ackQueueRepository.GetAcksBySessionAsync(sessionKey, statusFilter, 200);

                Acks.Clear();
                foreach (var entry in entries)
                {
                    Acks.Add(new AckEntryViewModel(entry));
                }
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

        partial void OnSelectedFilterChanged(string value)
        {
            if (!string.IsNullOrEmpty(_currentSessionKey))
            {
                _ = LoadAcksAsync(_currentSessionKey);
            }
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (!string.IsNullOrEmpty(_currentSessionKey))
            {
                await LoadAcksAsync(_currentSessionKey);
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

        private string BuildAckMessage(Domain.ValueObjects.PendingAck ack)
        {
            const char SOH = '\x01';
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.fff");

            var body = $"35=AR{SOH}" +
                      $"571={ack.TradeReportId}{SOH}" +
                      $"939=0{SOH}" +
                      $"568=1{SOH}" +
                      $"17={ack.InternTradeId}{SOH}" +
                      $"52={timestamp}{SOH}";

            return body;
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
    }
}