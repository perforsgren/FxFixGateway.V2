using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace FxFixGateway.Infrastructure.Notifications
{
    public sealed class PushoverNotificationService : IPushNotificationService, IDisposable
    {
        private readonly HttpClient _http;
        private readonly string     _token;
        private readonly string     _user;
        private readonly ILogger<PushoverNotificationService> _logger;

        private readonly bool _notifyOnUnknown;
        private readonly bool _notifyOnUserExit;
        private readonly bool _notifyOnCrash;
        private readonly bool _notifyOnOsShutdown;
        private readonly bool _notifyOnCounterpartyLogout;
        private readonly bool _notifyOnNetworkError;
        private readonly bool _notifyOnSessionDisabled;

        public PushoverNotificationService(
            IConfiguration configuration,
            ILogger<PushoverNotificationService> logger)
        {
            _logger = logger;

            var section = configuration.GetSection("Pushover");
            _token = section["Token"] ?? throw new InvalidOperationException("Pushover:Token is not configured.");
            _user  = section["User"]  ?? throw new InvalidOperationException("Pushover:User is not configured.");

            var flags = section.GetSection("Notify");
            _notifyOnUnknown            = flags.GetValue<bool>("Unknown",            true);
            _notifyOnUserExit           = flags.GetValue<bool>("UserExit",           false);
            _notifyOnCrash              = flags.GetValue<bool>("Crash",              true);
            _notifyOnOsShutdown         = flags.GetValue<bool>("OsShutdown",         true);
            _notifyOnCounterpartyLogout = flags.GetValue<bool>("CounterpartyLogout", true);
            _notifyOnNetworkError       = flags.GetValue<bool>("NetworkError",        true);
            _notifyOnSessionDisabled    = flags.GetValue<bool>("SessionDisabled",     false);

            var handler = new HttpClientHandler
            {
                UseProxy = true,
                Proxy    = WebRequest.GetSystemWebProxy(),
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            handler.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <inheritdoc/>
        public Task SendDisconnectAsync(string sessionKey, DisconnectReason reason)
        {
            if (!ShouldNotify(reason))
            {
                _logger.LogDebug("Push notification suppressed for reason {Reason} on session {Session}.", reason, sessionKey);
                return Task.CompletedTask;
            }

            var title   = "FX Gateway – Session Disconnected";
            var message = $"{ReasonEmoji(reason)} {sessionKey}\nAnledning: {ReasonLabel(reason)}";

            return SendAsync(title, message);
        }

        /// <inheritdoc/>
        public async Task SendAsync(string title, string message)
        {
            try
            {
                var values = new Dictionary<string, string>
                {
                    ["token"]   = _token,
                    ["user"]    = _user,
                    ["title"]   = title,
                    ["message"] = message
                };

                using var content = new FormUrlEncodedContent(values);
                var response = await _http.PostAsync("https://api.pushover.net/1/messages.json", content)
                                          .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                _logger.LogInformation("Push notification sent: {Title}", title);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Push notification HTTP error: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push notification failed: {Message}", ex.Message);
            }
        }

        private bool ShouldNotify(DisconnectReason reason) => reason switch
        {
            DisconnectReason.Unknown            => _notifyOnUnknown,
            DisconnectReason.UserExit           => _notifyOnUserExit,
            DisconnectReason.Crash              => _notifyOnCrash,
            DisconnectReason.OsShutdown         => _notifyOnOsShutdown,
            DisconnectReason.CounterpartyLogout => _notifyOnCounterpartyLogout,
            DisconnectReason.NetworkError       => _notifyOnNetworkError,
            DisconnectReason.SessionDisabled    => _notifyOnSessionDisabled,
            _                                   => true
        };

        private static string ReasonLabel(DisconnectReason reason) => reason switch
        {
            DisconnectReason.UserExit           => "Användaren stängde applikationen",
            DisconnectReason.Crash              => "Applikationen kraschade",
            DisconnectReason.OsShutdown         => "OS-omstart / shutdown",
            DisconnectReason.CounterpartyLogout => "Motparten skickade Logout",
            DisconnectReason.NetworkError       => "Nätverksfel",
            DisconnectReason.SessionDisabled    => "Session inaktiverad i konfiguration",
            _                                   => "Okänd anledning"
        };

        private static string ReasonEmoji(DisconnectReason reason) => reason switch
        {
            DisconnectReason.UserExit           => "🔌",
            DisconnectReason.Crash              => "💥",
            DisconnectReason.OsShutdown         => "🔄",
            DisconnectReason.CounterpartyLogout => "📤",
            DisconnectReason.NetworkError       => "🌐",
            DisconnectReason.SessionDisabled    => "⛔",
            _                                   => "❓"
        };

        public void Dispose() => _http.Dispose();
    }
}