using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FxFixGateway.Domain.ValueObjects;
using QuickFix;

namespace FxFixGateway.Infrastructure.QuickFix
{
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

            Directory.CreateDirectory(_fileStorePath);
            Directory.CreateDirectory(_fileLogPath);
        }

        public SessionSettings Build(IEnumerable<SessionConfiguration> configurations)
        {
            if (configurations == null)
                throw new ArgumentNullException(nameof(configurations));

            var configList = configurations.ToList();
            if (configList.Count == 0)
                throw new ArgumentException("At least one SessionConfiguration is required.");

            var settings = new SessionSettings();

            // [DEFAULT] section
            var defaultDict = new QuickFix.Dictionary();  // ← FIX: QuickFix.Dictionary, inte Dictionary<,>
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

            if (!string.IsNullOrEmpty(_dataDictionaryPath) && File.Exists(_dataDictionaryPath))
            {
                defaultDict.SetString("DataDictionary", _dataDictionaryPath);
            }

            settings.Set(defaultDict);

            // [SESSION] sections
            foreach (var config in configList)
            {
                var sessionId = new SessionID(
                    config.FixVersion,
                    config.SenderCompId,
                    config.TargetCompId);

                var sessionDict = new QuickFix.Dictionary();  // ← FIX: QuickFix.Dictionary

                sessionDict.SetString("SocketConnectHost", config.Host);
                sessionDict.SetLong("SocketConnectPort", config.Port);
                sessionDict.SetString("BeginString", config.FixVersion);
                sessionDict.SetString("SenderCompID", config.SenderCompId);
                sessionDict.SetString("TargetCompID", config.TargetCompId);
                sessionDict.SetLong("HeartBtInt", config.HeartBtIntSec);
                sessionDict.SetLong("ReconnectInterval", config.ReconnectIntervalSeconds);

                if (config.StartTime != TimeSpan.Zero || config.EndTime != TimeSpan.Zero)
                {
                    sessionDict.SetString("StartTime", config.StartTime.ToString(@"hh\:mm\:ss"));
                    sessionDict.SetString("EndTime", config.EndTime.ToString(@"hh\:mm\:ss"));
                }

                if (!string.IsNullOrEmpty(config.LogonUsername))
                {
                    sessionDict.SetString("Username", config.LogonUsername);

                    if (!string.IsNullOrEmpty(config.Password))
                    {
                        sessionDict.SetString("Password", config.Password);
                    }
                }

                settings.Set(sessionId, sessionDict);
            }

            return settings;
        }
    }
}
