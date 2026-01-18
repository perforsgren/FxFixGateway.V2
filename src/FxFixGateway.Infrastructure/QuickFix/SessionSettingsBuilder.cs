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
            _fileStorePath = fileStorePath ?? "store";
            _fileLogPath = fileLogPath ?? "log";
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

            // Ta första config för DEFAULT-värden (alla borde ha samma)
            var firstConfig = configurations.First();

            // [DEFAULT] section
            sb.AppendLine("[DEFAULT]");
            sb.AppendLine("ConnectionType=initiator");
            sb.AppendLine($"BeginString={firstConfig.FixVersion}");
            sb.AppendLine($"HeartBtInt={firstConfig.HeartBtIntSec}");
            sb.AppendLine($"ReconnectInterval={firstConfig.ReconnectIntervalSeconds}");
            sb.AppendLine($"StartTime={firstConfig.StartTime:hh\\:mm\\:ss}");
            sb.AppendLine($"EndTime={firstConfig.EndTime:hh\\:mm\\:ss}");
            sb.AppendLine($"FileStorePath={_fileStorePath}");
            sb.AppendLine($"FileLogPath={_fileLogPath}");
            sb.AppendLine();

            // Data Dictionary
            sb.AppendLine("UseDataDictionary=Y");
            if (!string.IsNullOrEmpty(_dataDictionaryPath) && File.Exists(_dataDictionaryPath))
            {
                sb.AppendLine($"DataDictionary={_dataDictionaryPath}");
            }
            else if (!string.IsNullOrEmpty(firstConfig.DataDictionaryFile))
            {
                sb.AppendLine($"DataDictionary={firstConfig.DataDictionaryFile}");
            }
            sb.AppendLine();

            // Validation
            sb.AppendLine("ValidateFieldsOutOfOrder=N");
            sb.AppendLine("ValidateUserDefinedFields=N");
            sb.AppendLine();

            sb.AppendLine("AllowUnknownMsgFields=Y");
            sb.AppendLine();

            sb.AppendLine("CheckLatency=N");
            sb.AppendLine("ResetOnLogon=Y");
            sb.AppendLine("ResetOnLogout=Y");
            sb.AppendLine("ResetOnDisconnect=Y");
            sb.AppendLine();

            // SSL settings (om första config använder SSL)
            if (firstConfig.UseSsl)
            {
                sb.AppendLine("SSLEnable=Y");
                sb.AppendLine("SSLProtocols=Tls12");
                sb.AppendLine("SSLValidateCertificates=N");
                sb.AppendLine("SSLCheckCertificateRevocation=N");

                if (!string.IsNullOrEmpty(firstConfig.SslServerName))
                {
                    sb.AppendLine($"SSLServerName={firstConfig.SslServerName}");
                }
                sb.AppendLine();
            }

            // [SESSION] sections - en per konfiguration
            foreach (var config in configurations)
            {
                sb.AppendLine("[SESSION]");
                sb.AppendLine($"SenderCompID={config.SenderCompId}");
                sb.AppendLine($"TargetCompID={config.TargetCompId}");
                sb.AppendLine();
                sb.AppendLine($"SocketConnectHost={config.Host}");
                sb.AppendLine($"SocketConnectPort={config.Port}");
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
