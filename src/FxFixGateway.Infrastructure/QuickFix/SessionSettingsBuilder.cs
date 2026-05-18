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

            // Validation - stäng av strikt validering
            sb.AppendLine("ValidateFieldsOutOfOrder=N");
            sb.AppendLine("ValidateUserDefinedFields=N");
            sb.AppendLine("ValidateFieldsHaveValues=N");
            sb.AppendLine("AllowUnknownMsgFields=Y");
            sb.AppendLine("CheckCompID=N");
            sb.AppendLine("CheckLatency=N");
            sb.AppendLine("ResetOnLogon=Y");
            sb.AppendLine("ResetOnLogout=Y");
            sb.AppendLine("ResetOnDisconnect=Y");
            sb.AppendLine();

            // [SESSION] sections
            foreach (var config in configurations)
            {
                sb.AppendLine("[SESSION]");
                sb.AppendLine($"SenderCompID={config.SenderCompId}");
                sb.AppendLine($"TargetCompID={config.TargetCompId}");
                sb.AppendLine($"HeartBtInt={config.HeartBtIntSec}");
                sb.AppendLine($"ReconnectInterval={config.ReconnectIntervalSeconds}");

                // Data dictionary
                var dictionaryFile = GetDataDictionaryForSession(config);
                if (!string.IsNullOrEmpty(dictionaryFile) && File.Exists(dictionaryFile))
                {
                    sb.AppendLine("UseDataDictionary=Y");
                    sb.AppendLine($"DataDictionary={dictionaryFile}");
                    sb.AppendLine("ValidateIncomingMessage=N"); // ← tillåt custom/ogiltiga värden från servern
                }
                else
                {
                    sb.AppendLine("UseDataDictionary=N");
                }
                sb.AppendLine();

                // Connection settings
                if (config.UseSSLTunnel && config.SslLocalPort.HasValue)
                {
                    sb.AppendLine($"SocketConnectHost=127.0.0.1");
                    sb.AppendLine($"SocketConnectPort={config.SslLocalPort.Value}");
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine($"SocketConnectHost={config.Host}");
                    sb.AppendLine($"SocketConnectPort={config.Port}");
                    sb.AppendLine();

                    if (config.UseSsl)
                    {
                        sb.AppendLine("SSLEnable=Y");
                        sb.AppendLine("SSLProtocols=Tls12");
                        sb.AppendLine("SSLValidateCertificates=N");
                        sb.AppendLine("SSLCheckCertificateRevocation=N");

                        if (!string.IsNullOrEmpty(config.SslServerName))
                        {
                            sb.AppendLine($"SSLServerName={config.SslServerName}");
                        }
                        sb.AppendLine();
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Bestämmer vilken data dictionary som ska användas per session.
        /// Sessions med DataDictionaryFile konfigurerat i DB använder den.
        /// Fenics-sessions behöver dictionary för multi-leg repeating groups (tag 555/600).
        /// </summary>
        private string GetDataDictionaryForSession(SessionConfiguration config)
        {
            // 1. Om sessionen har en explicit konfigurerad dictionary-fil, använd den
            if (!string.IsNullOrEmpty(config.DataDictionaryFile))
            {
                var configPath = config.DataDictionaryFile;
                if (File.Exists(configPath))
                    return configPath;

                // Försök relativt till applikationens directory
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var relativePath = Path.Combine(appDir, configPath);
                if (File.Exists(relativePath))
                    return relativePath;
            }

            // 2. Fallback: Använd Fenics-dictionary för kända Fenics-sessions
            if (config.VenueCode?.Contains("FENICS", StringComparison.OrdinalIgnoreCase) == true ||
                config.SessionKey?.Contains("FENICS", StringComparison.OrdinalIgnoreCase) == true)
            {
                var fenicsDict = FindDictionaryFile("FIX44_Fenics.xml");
                if (!string.IsNullOrEmpty(fenicsDict))
                    return fenicsDict;
            }

            // 3. Fallback: Gemensam dictionary om den finns
            if (!string.IsNullOrEmpty(_dataDictionaryPath) && File.Exists(_dataDictionaryPath))
                return _dataDictionaryPath;

            return null;
        }

        private string FindDictionaryFile(string filename)
        {
            // Sök i vanliga platser
            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename),
                Path.Combine(Directory.GetCurrentDirectory(), filename),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QuickFix", filename),
                filename
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        /// <summary>
        /// Exponerar config-innehållet för debug-ändamål.
        /// </summary>
        public string BuildConfigFileContentPublic(IEnumerable<SessionConfiguration> configurations)
        {
            return BuildConfigFileContent(configurations.ToList());
        }
    }
}
