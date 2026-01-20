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
        private int _activeConnections;

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
            
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _localPort);
                _listener.Start();

                _logger?.LogInformation(
                    "[{SessionKey}] SSL Tunnel STARTED: localhost:{LocalPort} -> {RemoteHost}:{RemotePort} (SNI={SniHost})",
                    _sessionKey, _localPort, _remoteHost, _remotePort, _sniHost);

                _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
            }
            catch (SocketException ex)
            {
                _logger?.LogError(ex, 
                    "[{SessionKey}] SSL Tunnel FAILED TO START on port {LocalPort}: {Message}",
                    _sessionKey, _localPort, ex.Message);
                throw;
            }
        }

        public void Stop()
        {
            if (_disposed) return;

            _logger?.LogInformation("[{SessionKey}] SSL Tunnel stopping...", _sessionKey);

            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _acceptLoopTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }

            _logger?.LogInformation("[{SessionKey}] SSL Tunnel stopped. Active connections: {Count}", 
                _sessionKey, _activeConnections);

            _listener = null;
            _cts = null;
            _acceptLoopTask = null;
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            _logger?.LogDebug("[{SessionKey}] SSL Tunnel accept loop started", _sessionKey);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TcpClient? localClient;

                    try
                    {
                        localClient = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                        Interlocked.Increment(ref _activeConnections);
                        
                        _logger?.LogDebug("[{SessionKey}] SSL Tunnel: QuickFIX connected (active: {Count})", 
                            _sessionKey, _activeConnections);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger?.LogDebug("[{SessionKey}] SSL Tunnel accept loop cancelled", _sessionKey);
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger?.LogDebug("[{SessionKey}] SSL Tunnel listener disposed", _sessionKey);
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "[{SessionKey}] SSL Tunnel accept error: {Error}", 
                            _sessionKey, ex.Message);
                        continue;
                    }

                    _ = Task.Run(() => HandleClientAsync(localClient, cancellationToken));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[{SessionKey}] SSL Tunnel accept loop terminated unexpectedly", _sessionKey);
            }
        }

        private async Task HandleClientAsync(TcpClient localClient, CancellationToken cancellationToken)
        {
            var connectionId = Guid.NewGuid().ToString("N")[..8];
            
            _logger?.LogDebug("[{SessionKey}][{ConnId}] Connecting to remote {Host}:{Port}...", 
                _sessionKey, connectionId, _remoteHost, _remotePort);

            using (localClient)
            {
                TcpClient? remoteClient = null;

                try
                {
                    // 1. Connect to remote
                    remoteClient = new TcpClient();
                    
                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    connectCts.CancelAfter(TimeSpan.FromSeconds(30));
                    
                    await remoteClient.ConnectAsync(_remoteHost, _remotePort, connectCts.Token).ConfigureAwait(false);
                    
                    _logger?.LogDebug("[{SessionKey}][{ConnId}] TCP connected, starting SSL handshake...", 
                        _sessionKey, connectionId);

                    // 2. SSL handshake
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

                        _logger?.LogInformation(
                            "[{SessionKey}][{ConnId}] SSL tunnel established (Protocol: {Protocol}, Cipher: {Cipher})",
                            _sessionKey, connectionId, sslStream.SslProtocol, sslStream.CipherAlgorithm);

                        // 3. Start pumping data
                        NetworkStream localStream = localClient.GetStream();

                        Task t1 = PumpAsync(localStream, sslStream, "QuickFIX->Remote", connectionId, cancellationToken);
                        Task t2 = PumpAsync(sslStream, localStream, "Remote->QuickFIX", connectionId, cancellationToken);

                        await Task.WhenAny(t1, t2).ConfigureAwait(false);
                        
                        _logger?.LogDebug("[{SessionKey}][{ConnId}] Connection closed normally", 
                            _sessionKey, connectionId);
                    }
                }
                catch (SocketException ex)
                {
                    _logger?.LogWarning("[{SessionKey}][{ConnId}] Socket error: {Message} (ErrorCode: {Code})", 
                        _sessionKey, connectionId, ex.Message, ex.SocketErrorCode);
                }
                catch (AuthenticationException ex)
                {
                    _logger?.LogError("[{SessionKey}][{ConnId}] SSL authentication failed: {Message}", 
                        _sessionKey, connectionId, ex.Message);
                }
                catch (OperationCanceledException)
                {
                    _logger?.LogDebug("[{SessionKey}][{ConnId}] Connection cancelled", _sessionKey, connectionId);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("[{SessionKey}][{ConnId}] Connection error: {Type}: {Message}", 
                        _sessionKey, connectionId, ex.GetType().Name, ex.Message);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeConnections);
                }
            }
        }

        private async Task PumpAsync(
            System.IO.Stream source,
            System.IO.Stream destination,
            string direction,
            string connectionId,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            long totalBytes = 0;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                                                .ConfigureAwait(false);
                    if (bytesRead <= 0)
                    {
                        _logger?.LogDebug("[{SessionKey}][{ConnId}] {Direction}: Stream ended (total: {Bytes} bytes)", 
                            _sessionKey, connectionId, direction, totalBytes);
                        break;
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                                      .ConfigureAwait(false);
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                    
                    totalBytes += bytesRead;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.LogDebug("[{SessionKey}][{ConnId}] {Direction}: Pump ended ({Type})", 
                    _sessionKey, connectionId, direction, ex.GetType().Name);
            }
        }

        private bool ValidateServerCertificate(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                _logger?.LogDebug("[{SessionKey}] SSL certificate OK: {Subject}", 
                    _sessionKey, certificate?.Subject);
                return true;
            }

            _logger?.LogWarning(
                "[{SessionKey}] SSL certificate warning: {Errors} (Subject: {Subject}) - Accepting anyway for DEV/STAGE",
                _sessionKey, sslPolicyErrors, certificate?.Subject);
            
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
