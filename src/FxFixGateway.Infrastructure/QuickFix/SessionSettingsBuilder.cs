using FxFixGateway.Domain.ValueObjects;
using MySqlX.XDevAPI;
using QuickFix;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FxFixGateway.Infrastructure.QuickFix
{
    /// <summary>
    /// Bygger QuickFIX SessionSettings dynamiskt från SessionConfiguration.
    /// Genererar både [DEFAULT] section och en [SESSION] per konfiguration.
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

            var settings = new SessionSettings();

            // [DEFAULT] section - gäller alla sessions
            var defaultDict = new Dictionary();
            defaultDict.SetString("ConnectionType", "initiator");
            defaultDict.SetString("ReconnectInterval", "30");
            defaultDict.SetString("FileStorePath", _fileStorePath);
            defaultDict.SetString("FileLogPath", _fileLogPath);
            defaultDict.SetString("StartTime", "00:00:00");
            defaultDict.SetString("EndTime", "00:00:00");
            defaultDict.SetString("UseDataDictionary", "Y");
            defaultDict.SetString("ValidateUserDefinedFields", "N");
            defaultDict.SetString("ValidateFieldsOutOfOrder", "N");
            defaultDict.SetString("ValidateFieldsHaveValues", "N");
            defaultDict.SetString("ValidateUnorderedGroupFields", "N");
            defaultDict.SetString("CheckLatency", "N");

            // Data dictionary path (om angiven)
            if (!string.IsNullOrEmpty(_dataDictionaryPath) && File.Exists(_dataDictionaryPath))
            {
                defaultDict.SetString("DataDictionary", _dataDictionaryPath);
            }

            settings.Set(defaultDict);

            // [SESSION] sections - en per konfiguration
            foreach (var config in configList)
            {
                var sessionId = new SessionID(
                    config.FixVersion,
                    config.SenderCompId,
                    config.TargetCompId);

                var sessionDict = new Dictionary();

                // Connection details
                sessionDict.SetString("SocketConnectHost", config.Host);
                sessionDict.SetLong("SocketConnectPort", config.Port);
                sessionDict.SetString("BeginString", config.FixVersion);
                sessionDict.SetString("SenderCompID", config.SenderCompId);
                sessionDict.SetString("TargetCompID", config.TargetCompId);
                sessionDict.SetLong("HeartBtInt", config.HeartBtIntSec);

                // Timing
                sessionDict.SetLong("ReconnectInterval", config.ReconnectIntervalSeconds);

                if (config.StartTime != TimeSpan.Zero || config.EndTime != TimeSpan.Zero)
                {
                    sessionDict.SetString("StartTime", config.StartTime.ToString(@"hh\:mm\:ss"));
                    sessionDict.SetString("EndTime", config.EndTime.ToString(@"hh\:mm\:ss"));
                }

                // Authentication (om användarnamn finns)
                if (!string.IsNullOrEmpty(config.LogonUsername))
                {
                    sessionDict.SetString("Username", config.LogonUsername);

                    if (!string.IsNullOrEmpty(config.Password))
                    {
                        sessionDict.SetString("Password", config.Password);
                    }
                }

                // SSL (QuickFIX har begränsat SSL-stöd, ofta behövs stunnel)
                // Vi loggar bara SSL-flaggan här för framtida användning
                if (config.UseSsl)
                {
                    // QuickFIX native har inte SSL - behöver stunnel eller liknande
                    // Logga varning eller skapa stunnel-konfiguration här
                }

                settings.Set(sessionId, sessionDict);
            }

            return settings;
        }
    }
}
