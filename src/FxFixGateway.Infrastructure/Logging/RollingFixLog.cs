using System;
using System.IO;
using System.Text;

namespace FxFixGateway.Infrastructure.Logging
{
    public sealed class RollingFixLog : global::QuickFix.Logger.ILog, IDisposable
    {
        private readonly string  _basePath;
        private readonly string  _sessionPrefix;
        private readonly long    _maxFileSizeBytes;
        private readonly int     _retainedDays;

        private StreamWriter? _writer;
        private string?       _currentFilePath;
        private DateTime      _currentDate;
        private int           _rollIndex;
        private readonly object _lock = new();

        public RollingFixLog(string sessionPrefix, string basePath,
            long maxFileSizeBytes = 20 * 1024 * 1024, int retainedDays = 7)
        {
            _sessionPrefix    = sessionPrefix;
            _basePath         = basePath;
            _maxFileSizeBytes = maxFileSizeBytes;
            _retainedDays     = retainedDays;

            Directory.CreateDirectory(_basePath);
            OpenWriter();
        }

        public void OnIncoming(string msg) => Write("incoming", msg);
        public void OnOutgoing(string msg) => Write("outgoing", msg);
        public void OnEvent(string s)      => Write("event", s);
        public void Clear() { }

        private void Write(string direction, string msg)
        {
            lock (_lock)
            {
                EnsureWriter();
                try
                {
                    _writer!.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [{direction}] {msg}");
                    _writer.Flush();
                }
                catch { /* Tyst — loggen får inte krascha gatewayen */ }
            }
        }

        private void EnsureWriter()
        {
            var today     = DateTime.UtcNow.Date;
            var needsRoll = _currentDate != today
                         || (_writer != null && new FileInfo(_currentFilePath!).Length >= _maxFileSizeBytes);

            if (!needsRoll) return;

            CloseWriter();
            if (_currentDate != today)
            {
                _currentDate = today;
                _rollIndex   = 0;
                PurgeOldFiles();
            }
            else
            {
                _rollIndex++;
            }
            OpenWriter();
        }

        private void OpenWriter()
        {
            _currentDate     = DateTime.UtcNow.Date;
            var suffix       = _rollIndex == 0 ? "" : $"_{_rollIndex}";
            var fileName     = $"{_sessionPrefix}.{_currentDate:yyyyMMdd}{suffix}.log";
            _currentFilePath = Path.Combine(_basePath, fileName);
            _writer          = new StreamWriter(_currentFilePath, append: true, Encoding.UTF8)
            {
                AutoFlush = false
            };
        }

        private void CloseWriter()
        {
            try { _writer?.Flush(); _writer?.Dispose(); }
            catch { /* ignorera */ }
            _writer = null;
        }

        private void PurgeOldFiles()
        {
            try
            {
                var cutoff = DateTime.UtcNow.Date.AddDays(-_retainedDays);
                foreach (var file in Directory.GetFiles(_basePath, $"{_sessionPrefix}.*.log"))
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
            }
            catch { /* ignorera */ }
        }

        public void Dispose()
        {
            lock (_lock) CloseWriter();
        }
    }
}