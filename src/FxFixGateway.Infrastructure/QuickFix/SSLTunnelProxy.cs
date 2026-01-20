using System;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FxFixGateway.Infrastructure.QuickFix
{
    public sealed class SSLTunnelProxy : IDisposable
    {
        private readonly string _sessionKey;
        private readonly string _remoteHost;
        private readonly int _remotePort;
        private readonly int _localPort;
        private readonly string _sniHost;
        private readonly ILogger? _logger;

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptLoopTask;
        private bool _disposed;

        public SSLTunnelProxy(
            string sessionKey,
            string remoteHost,
            int remotePort,
            int localPort,
            string? sniHost = null,
            ILogger? logger = null)
        {
            _sessionKey = sessionKey ?? throw new ArgumentNullException(nameof(sessionKey));
            _remoteHost = remoteHost ?? throw new ArgumentNullException(nameof(remoteHost));
            _remotePort = remotePort;
            _localPort = localPort;
            _sniHost = string.IsNullOrWhiteSpace(sniHost) ? remoteHost : sniHost;
            _logger = logger;
        }

        public void Start()
        {
            if (_listener != null)
                throw new InvalidOperationException("SSL tunnel already started.");

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, _localPort);
            _listener.Start();

            _logger?.LogInformation("[{SessionKey}] SSL Tunnel: localhost:{LocalPort} -> {RemoteHost}:{RemotePort} (SNI={SniHost})",
                _sessionKey, _localPort, _remoteHost, _remotePort, _sniHost);

            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (_disposed) return;

            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _acceptLoopTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }

            _logger?.LogInformation("[{SessionKey}] SSL Tunnel stopped.", _sessionKey);

            _listener = null;
            _cts = null;
            _acceptLoopTask = null;
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TcpClient? localClient;

                    try
                    {
                        localClient = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning("[{SessionKey}] SSL Tunnel accept error: {Error}", _sessionKey, ex.Message);
                        continue;
                    }

                    _ = Task.Run(() => HandleClientAsync(localClient, cancellationToken));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[{SessionKey}] SSL Tunnel accept loop terminated", _sessionKey);
            }
        }

        private async Task HandleClientAsync(TcpClient localClient, CancellationToken cancellationToken)
        {
            using (localClient)
            {
                TcpClient? remoteClient = null;

                try
                {
                    remoteClient = new TcpClient();
                    await remoteClient.ConnectAsync(_remoteHost, _remotePort, cancellationToken).ConfigureAwait(false);

                    using (remoteClient)
                    using (var sslStream = new SslStream(
                        remoteClient.GetStream(),
                        leaveInnerStreamOpen: false,
                        userCertificateValidationCallback: ValidateServerCertificate))
                    {
                        await sslStream.AuthenticateAsClientAsync(
                            new SslClientAuthenticationOptions
                            {
                                TargetHost = _sniHost,
                                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                            }, cancellationToken).ConfigureAwait(false);

                        NetworkStream localStream = localClient.GetStream();

                        Task t1 = PumpAsync(localStream, sslStream, cancellationToken);
                        Task t2 = PumpAsync(sslStream, localStream, cancellationToken);

                        await Task.WhenAny(t1, t2).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("[{SessionKey}] SSL Tunnel client error: {Error}", _sessionKey, ex.Message);
                }
            }
        }

        private static async Task PumpAsync(
            System.IO.Stream source,
            System.IO.Stream destination,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                                                .ConfigureAwait(false);
                    if (bytesRead <= 0)
                        break;

                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                                      .ConfigureAwait(false);
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private bool ValidateServerCertificate(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            _logger?.LogWarning("[{SessionKey}] SSL certificate validation warning: {Errors}", _sessionKey, sslPolicyErrors);
            return true; // STAGE/DEV: Tillåt self-signed certs
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
