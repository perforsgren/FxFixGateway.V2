using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FxFixGateway.Domain.ValueObjects;
using QuickFix;

namespace FxFixGateway.Infrastructure.QuickFix
{
    /// <summary>
    /// Bygger QuickFIX SessionSettings dynamiskt från SessionConfiguration.
    /// Skapar en temporär .cfg fil och läser den.
    /// </summary>
    public class SessionSettingsBuilder
    {
        private readonly string _fileStorePath;
        private readonly string _fileLogPath;
        private readonly string _dataDictionaryPath;

        public SessionSettingsBuilder(
            string fileStorePath = null,
            string fileLogPath = null,
            string dataDictionaryPath = null)
        {
            _fileStorePath = fileStorePath ?? Path.Combine(Directory.GetCurrentDirectory(), "store");
            _fileLogPath = fileLogPath ?? Path.Combine(Directory.GetCurrentDirectory(), "log");
            _dataDictionaryPath = dataDictionaryPath;

            // Skapa directories om de inte finns
            Directory.CreateDirectory(_fileStorePath);
            Directory.CreateDirectory(_fileLogPath);
        }

        /// <summary>
        /// Bygger QuickFIX SessionSettings från flera SessionConfiguration.
        /// </summary>
        public SessionSettings Build(IEnumerable<SessionConfiguration> configurations)
        {
            if (configurations == null)
                throw new ArgumentNullException(nameof(configurations));

            var configList = configurations.ToList();
            if (configList.Count == 0)
                throw new ArgumentException("At least one SessionConfiguration is required.");

            // Bygg config-fil innehåll
            var configContent = BuildConfigFileContent(configList);

            // Skapa temporär fil
            var tempConfigFile = Path.Combine(Path.GetTempPath(), $"quickfix_{Guid.NewGuid():N}.cfg");

            try
            {
                File.WriteAllText(tempConfigFile, configContent);
                var settings = new SessionSettings(tempConfigFile);
                return settings;
            }
            finally
            {
                // Cleanup temp file
                if (File.Exists(tempConfigFile))
                {
                    try { File.Delete(tempConfigFile); } catch { /* Ignore */ }
                }
            }
        }

        private string BuildConfigFileContent(List<SessionConfiguration> configurations)
        {
            var sb = new StringBuilder();

            // [DEFAULT] section
            sb.AppendLine("[DEFAULT]");
            sb.AppendLine("ConnectionType=initiator");
            sb.AppendLine("ReconnectInterval=30");
            sb.AppendLine($"FileStorePath={_fileStorePath}");
            sb.AppendLine($"FileLogPath={_fileLogPath}");
            sb.AppendLine("StartTime=00:00:00");
            sb.AppendLine("EndTime=00:00:00");
            sb.AppendLine("UseDataDictionary=Y");
            sb.AppendLine("ValidateUserDefinedFields=N");
            sb.AppendLine("ValidateFieldsOutOfOrder=N");
            sb.AppendLine("ValidateFieldsHaveValues=N");
            sb.AppendLine("ValidateUnorderedGroupFields=N");
            sb.AppendLine("CheckLatency=N");

            if (!string.IsNullOrEmpty(_dataDictionaryPath) && File.Exists(_dataDictionaryPath))
            {
                sb.AppendLine($"DataDictionary={_dataDictionaryPath}");
            }

            sb.AppendLine();

            // [SESSION] sections - en per konfiguration
            foreach (var config in configurations)
            {
                sb.AppendLine("[SESSION]");
                sb.AppendLine($"BeginString={config.FixVersion}");
                sb.AppendLine($"SenderCompID={config.SenderCompId}");
                sb.AppendLine($"TargetCompID={config.TargetCompId}");
                sb.AppendLine($"SocketConnectHost={config.Host}");
                sb.AppendLine($"SocketConnectPort={config.Port}");
                sb.AppendLine($"HeartBtInt={config.HeartBtIntSec}");
                sb.AppendLine($"ReconnectInterval={config.ReconnectIntervalSeconds}");

                if (config.StartTime != TimeSpan.Zero || config.EndTime != TimeSpan.Zero)
                {
                    sb.AppendLine($"StartTime={config.StartTime:hh\\:mm\\:ss}");
                    sb.AppendLine($"EndTime={config.EndTime:hh\\:mm\\:ss}");
                }

                if (!string.IsNullOrEmpty(config.LogonUsername))
                {
                    sb.AppendLine($"Username={config.LogonUsername}");

                    if (!string.IsNullOrEmpty(config.Password))
                    {
                        sb.AppendLine($"Password={config.Password}");
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
