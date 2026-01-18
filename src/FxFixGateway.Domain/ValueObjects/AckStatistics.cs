namespace FxFixGateway.Domain.ValueObjects
{
    /// <summary>
    /// Statistik för ACKs för en session.
    /// </summary>
    public sealed class AckStatistics
    {
        public int PendingCount { get; }
        public int SentTodayCount { get; }
        public int FailedCount { get; }

        public AckStatistics(int pendingCount, int sentTodayCount, int failedCount)
        {
            PendingCount = pendingCount;
            SentTodayCount = sentTodayCount;
            FailedCount = failedCount;
        }

        public static AckStatistics Empty => new(0, 0, 0);
    }
}