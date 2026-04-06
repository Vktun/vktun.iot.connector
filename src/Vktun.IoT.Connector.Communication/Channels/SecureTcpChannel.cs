using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Communication.Channels;

/// <summary>
/// 安全TCP通道 - 支持TLS加密的TCP客户端通道
/// </summary>
public class SecureTcpChannel : CommunicationChannelBase
{
    private readonly IAuthenticationProvider? _authProvider;
    private readonly TlsConfig? _tlsConfig;
    private readonly ConcurrentDictionary<string, SslStream?> _sslStreams;
    private Socket? _clientSocket;
    private readonly object _connectLock = new();

    public SecureTcpChannel(
        IConfigurationProvider configProvider,
        ILogger logger,
        IAuthenticationProvider? authProvider = null,
        TlsConfig? tlsConfig = null)
        : base(configProvider, logger)
    {
        _authProvider = authProvider;
        _tlsConfig = tlsConfig;
        _sslStreams = new ConcurrentDictionary<string, SslStream?>();
    }

    public override CommunicationType CommunicationType => CommunicationType.Tcp;
    public override ConnectionMode ConnectionMode => ConnectionMode.Client;

    public override Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = true;
        return Task.FromResult(true);
    }

    public override async Task CloseAsync()
    {
        foreach (var deviceId in _connections.Keys.ToList())
        {
            await DisconnectDeviceAsync(deviceId);
        }

        _clientSocket?.Dispose();
        _clientSocket = null;
        _isConnected = false;
    }

    public override async Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        var deviceId = device.DeviceId;

        lock (_connectLock)
        {
            if (_connections.ContainsKey(deviceId))
            {
                _logger.Debug($"Device {deviceId} is already connected");
                return true;
            }
        }

        try
        {
            _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            _clientSocket.NoDelay = true;
            _clientSocket.ReceiveBufferSize = 8192;
            _clientSocket.SendBufferSize = 8192;

            var endPoint = new IPEndPoint(IPAddress.Parse(device.IpAddress), device.Port);
            await _clientSocket.ConnectAsync(endPoint, cancellationToken);

            var networkStream = new NetworkStream(_clientSocket, true);
            Stream stream = networkStream;

            if (_tlsConfig != null && _tlsConfig.Enabled)
            {
                stream = await PerformTlsHandshakeAsync(networkStream, device.IpAddress, cancellationToken);
                if (stream == null)
                {
                    _logger.Error($"TLS handshake failed for device {deviceId}");
                    _clientSocket.Dispose();
                    _clientSocket = null;
                    return false;
                }
            }

            var connection = new DeviceConnection
            {
                DeviceId = deviceId,
                Socket = _clientSocket,
                RemoteEndPoint = endPoint,
                ConnectTime = DateTime.UtcNow,
                LastActiveTime = DateTime.UtcNow,
                ReceiveBuffer = new byte[8192],
                CancellationTokenSource = new CancellationTokenSource()
            };

            _connections[deviceId] = connection;

            if (stream is SslStream sslStream)
            {
                _sslStreams[deviceId] = sslStream;
            }

            _isConnected = true;
            OnDeviceConnected(deviceId, device);

            _logger.Info($"Device {deviceId} connected successfully{(stream is SslStream ? " with TLS" : "")}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error connecting to device {deviceId}: {ex.Message}", ex);
            OnErrorOccurred(deviceId, "Connection failed", ex);
            return false;
        }
    }

    public override async Task DisconnectDeviceAsync(string deviceId)
    {
        if (!_connections.TryRemove(deviceId, out var connection))
        {
            return;
        }

        try
        {
            if (_sslStreams.TryRemove(deviceId, out var sslStream))
            {
                sslStream?.Close();
                sslStream?.Dispose();
            }

            connection.CancellationTokenSource?.Cancel();
            connection.Socket?.Shutdown(SocketShutdown.Both);
            connection.Socket?.Dispose();

            OnDeviceDisconnected(deviceId, "Disconnected by request");
            _logger.Info($"Device {deviceId} disconnected");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error disconnecting device {deviceId}: {ex.Message}", ex);
        }
    }

    public override async Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(deviceId, out var connection))
        {
            _logger.Warning($"Device {deviceId} is not connected");
            return 0;
        }

        try
        {
            if (_sslStreams.TryGetValue(deviceId, out var sslStream) && sslStream != null)
            {
                await sslStream.WriteAsync(data, 0, data.Length, cancellationToken);
                await sslStream.FlushAsync(cancellationToken);
            }
            else if (connection.Socket != null)
            {
                await connection.Socket.SendAsync(data, SocketFlags.None, cancellationToken);
            }

            connection.BytesSent += data.Length;
            connection.LastActiveTime = DateTime.UtcNow;

            _logger.Debug($"Sent {data.Length} bytes to device {deviceId}");
            return data.Length;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error sending data to device {deviceId}: {ex.Message}", ex);
            OnErrorOccurred(deviceId, "Send failed", ex);
            return 0;
        }
    }

    public override async Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        return await SendAsync(deviceId, data.ToArray(), cancellationToken);
    }

    public override async IAsyncEnumerable<ReceivedData> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested && _isConnected)
        {
            List<ReceivedData> receivedDataList = new();

            foreach (var kvp in _connections)
            {
                var deviceId = kvp.Key;
                var connection = kvp.Value;

                if (connection.Socket == null || !connection.Socket.Connected)
                    continue;

                var receivedData = await TryReceiveAsync(deviceId, connection, cancellationToken);
                if (receivedData != null)
                {
                    receivedDataList.Add(receivedData);
                }
            }

            foreach (var data in receivedDataList)
            {
                yield return data;
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private async Task<ReceivedData?> TryReceiveAsync(string deviceId, DeviceConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            if (_sslStreams.TryGetValue(deviceId, out var sslStream) && sslStream != null && sslStream.CanRead)
            {
                var buffer = new byte[8192];
                var bytesRead = await sslStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                if (bytesRead > 0)
                {
                    connection.BytesReceived += bytesRead;
                    connection.LastActiveTime = DateTime.UtcNow;

                    return new ReceivedData
                    {
                        DeviceId = deviceId,
                        Data = buffer.AsMemory(0, bytesRead).ToArray(),
                        Timestamp = DateTime.UtcNow
                    };
                }
            }
            else if (connection.Socket != null && connection.Socket.Available > 0)
            {
                var buffer = new byte[8192];
                var bytesRead = await connection.Socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);

                if (bytesRead > 0)
                {
                    connection.BytesReceived += bytesRead;
                    connection.LastActiveTime = DateTime.UtcNow;

                    return new ReceivedData
                    {
                        DeviceId = deviceId,
                        Data = buffer.AsMemory(0, bytesRead).ToArray(),
                        Timestamp = DateTime.UtcNow
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error receiving data from device {deviceId}: {ex.Message}", ex);
            OnErrorOccurred(deviceId, "Receive failed", ex);
        }

        return null;
    }

    private async Task<SslStream?> PerformTlsHandshakeAsync(
        NetworkStream networkStream,
        string targetHost,
        CancellationToken cancellationToken)
    {
        try
        {
            var sslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

            if (_tlsConfig?.AllowedVersions != null && _tlsConfig.AllowedVersions.Count > 0)
            {
                sslProtocols = SslProtocols.None;
                foreach (var version in _tlsConfig.AllowedVersions)
                {
                    sslProtocols |= version switch
                    {
                        TlsVersion.Tls12 => SslProtocols.Tls12,
                        TlsVersion.Tls13 => SslProtocols.Tls13,
                        _ => SslProtocols.None
                    };
                }
            }

            var sslStream = new SslStream(networkStream, false, ValidateRemoteCertificate);
            var clientCertificates = new X509CertificateCollection();

            if (_tlsConfig?.RequireClientCertificate == true && !string.IsNullOrEmpty(_tlsConfig.CertificatePath))
            {
                var clientCert = new X509Certificate2(_tlsConfig.CertificatePath, _tlsConfig.CertificatePassword);
                clientCertificates.Add(clientCert);
            }

            await sslStream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = targetHost,
                    ClientCertificates = clientCertificates,
                    EnabledSslProtocols = sslProtocols,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                },
                cancellationToken);

            _logger.Info($"TLS handshake completed. Protocol: {sslStream.SslProtocol}");
            return sslStream;
        }
        catch (Exception ex)
        {
            _logger.Error($"TLS handshake error: {ex.Message}", ex);
            return null;
        }
    }

    private bool ValidateRemoteCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        _logger.Warning($"Certificate validation errors: {sslPolicyErrors}");

#if DEBUG
        _logger.Warning("Accepting certificate in DEBUG mode despite errors");
        return true;
#else
        return false;
#endif
    }

    public override async ValueTask DisposeAsync()
    {
        await CloseAsync();

        foreach (var kvp in _sslStreams)
        {
            kvp.Value?.Dispose();
        }
        _sslStreams.Clear();

        await base.DisposeAsync();
    }
}