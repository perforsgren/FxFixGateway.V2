using QuickFix;

namespace FxFixGateway.Infrastructure.Logging
{
    /// <summary>
    /// ILogFactory som skapar RollingFixLog per QuickFIX-session.
    /// Konfigureras med maxFileSizeBytes och retainedDays.
    /// </summary>
    public sealed class RollingFixLogFactory : global::QuickFix.Logger.ILogFactory
    {
        private readonly string _basePath;
        private readonly long   _maxFileSizeBytes;
        private readonly int    _retainedDays;

        /// <param name="basePath">Katalog för FIX-audit-loggarna, t.ex. "log"</param>
        /// <param name="maxFileSizeBytes">Max storlek per fil innan rotation (default 20 MB)</param>
        /// <param name="retainedDays">Antal dagar att behålla gamla loggar (default 7)</param>
        public RollingFixLogFactory(
            string basePath       = "log",
            long maxFileSizeBytes = 20 * 1024 * 1024,
            int retainedDays     = 7)
        {
            _basePath         = basePath;
            _maxFileSizeBytes = maxFileSizeBytes;
            _retainedDays     = retainedDays;
        }

        public global::QuickFix.Logger.ILog Create(global::QuickFix.SessionID sessionId)
        {
            var prefix = $"{sessionId.BeginString}-{sessionId.SenderCompID}-{sessionId.TargetCompID}"
                .Replace("/", "_").Replace("\\", "_").Replace(":", "_");
            return new RollingFixLog(prefix, _basePath, _maxFileSizeBytes, _retainedDays);
        }

        public global::QuickFix.Logger.ILog CreateNonSessionLog()
            => new RollingFixLog("quickfix-nonsession", _basePath, _maxFileSizeBytes, _retainedDays);
    }
}