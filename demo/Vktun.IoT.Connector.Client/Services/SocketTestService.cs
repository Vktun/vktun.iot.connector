using Vktun.IoT.Connector.Client.Models;
using Vktun.IoT.Connector.Communication.Channels;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Configuration.Providers;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;

namespace Vktun.IoT.Connector.Client.Services;

public class SocketTestService : ISocketTestService
{
    private readonly ILogger _logger;
    private readonly IConfigurationProvider _configProvider;
    private readonly SemaphoreSlim _sync = new(1, 1);

    private ICommunicationChannel? _channel;
    private DeviceInfo? _device;
    private ConnectionConfig? _currentConfig;

    public bool IsConnected => _channel?.IsConnected == true && _device != null;
    public ConnectionConfig? CurrentConfig => _currentConfig;

    public event EventHandler<string>? LogMessage;
    public event EventHandler<byte[]>? DataReceived;

    public SocketTestService()
    {
        _logger = new ConsoleLogger(LogLevel.Info);
        _configProvider = new JsonConfigurationProvider(_logger);
    }

    public async Task<bool> ConnectAsync(ConnectionConfig config, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);

            var device = BuildDevice(config);
            var validation = ConnectionSettingsValidator.ValidateAndNormalize(device);
            if (!validation.IsValid || validation.Settings == null)
            {
                EmitLog($"Invalid connection settings: {validation.ErrorMessage}");
                return false;
            }

            ConnectionSettingsValidator.ApplyNormalizedSettings(device, validation.Settings);

            var channel = CreateChannel(device);
            AttachChannelEvents(channel);

            if (!await channel.OpenAsync(cancellationToken).ConfigureAwait(false))
            {
                DetachChannelEvents(channel);
                EmitLog("Failed to open channel.");
                return false;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(config.Timeout > 0 ? config.Timeout : 3000);

            var connected = await channel.ConnectDeviceAsync(device, timeoutCts.Token).ConfigureAwait(false);
            if (!connected)
            {
                await channel.CloseAsync().ConfigureAwait(false);
                DetachChannelEvents(channel);
                EmitLog("Failed to connect.");
                return false;
            }

            _channel = channel;
            _device = device;
            _currentConfig = config;
            EmitLog($"Connected: {device.CommunicationType}/{device.ConnectionMode}.");
            return true;
        }
        catch (OperationCanceledException)
        {
            EmitLog("Connect was canceled.");
            return false;
        }
        catch (Exception ex)
        {
            EmitLog($"Connect failed: {ex.Message}");
            return false;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _sync.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<int> SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (_channel == null || _device == null)
        {
            EmitLog("No active connection.");
            return 0;
        }

        var sent = await _channel.SendAsync(_device.DeviceId, data, cancellationToken).ConfigureAwait(false);
        EmitLog($"Sent {sent} bytes.");
        return sent;
    }

    private async Task DisconnectCoreAsync()
    {
        if (_channel != null && _device != null)
        {
            try
            {
                await _channel.DisconnectDeviceAsync(_device.DeviceId).ConfigureAwait(false);
                await _channel.CloseAsync().ConfigureAwait(false);
                EmitLog("Disconnected.");
            }
            catch (Exception ex)
            {
                EmitLog($"Disconnect failed: {ex.Message}");
            }
            finally
            {
                DetachChannelEvents(_channel);
                await _channel.DisposeAsync().ConfigureAwait(false);
            }
        }

        _channel = null;
        _device = null;
        _currentConfig = null;
    }

    private void OnChannelDataReceived(object? sender, Vktun.IoT.Connector.Core.Interfaces.DataReceivedEventArgs e)
    {
        DataReceived?.Invoke(this, e.Data);
        EmitLog($"Received {e.Data.Length} bytes from {e.DeviceId}.");
    }

    private void OnChannelError(object? sender, ChannelErrorEventArgs e)
    {
        EmitLog($"Channel error: {e.Message}");
    }

    private void OnChannelDeviceConnected(object? sender, DeviceConnectedEventArgs e)
    {
        EmitLog($"Device connected: {e.DeviceId}");
    }

    private void OnChannelDeviceDisconnected(object? sender, DeviceDisconnectedEventArgs e)
    {
        EmitLog($"Device disconnected: {e.DeviceId}. Reason: {e.Reason}");
    }

    private void AttachChannelEvents(ICommunicationChannel channel)
    {
        channel.DataReceived += OnChannelDataReceived;
        channel.ErrorOccurred += OnChannelError;
        channel.DeviceConnected += OnChannelDeviceConnected;
        channel.DeviceDisconnected += OnChannelDeviceDisconnected;
    }

    private void DetachChannelEvents(ICommunicationChannel channel)
    {
        channel.DataReceived -= OnChannelDataReceived;
        channel.ErrorOccurred -= OnChannelError;
        channel.DeviceConnected -= OnChannelDeviceConnected;
        channel.DeviceDisconnected -= OnChannelDeviceDisconnected;
    }

    private static DeviceInfo BuildDevice(ConnectionConfig config)
    {
        return new DeviceInfo
        {
            DeviceId = "SOCKET_DEBUG_DEVICE",
            DeviceName = "Socket Debug Device",
            CommunicationType = config.CommunicationType,
            ConnectionMode = config.ConnectionMode,
            IpAddress = config.IpAddress,
            Port = config.Port,
            LocalIpAddress = config.LocalIpAddress,
            LocalPort = config.LocalPort,
            ProtocolType = ProtocolType.Custom,
            ProtocolId = "SocketDebugProtocol"
        };
    }

    private ICommunicationChannel CreateChannel(DeviceInfo device)
    {
        return (device.CommunicationType, device.ConnectionMode) switch
        {
            (CommunicationType.Tcp, ConnectionMode.Client) => new TcpClientChannel(_configProvider, _logger),
            (CommunicationType.Tcp, ConnectionMode.Server) => new TcpServerChannel(device.LocalIpAddress, device.LocalPort, _configProvider, _logger),
            (CommunicationType.Udp, _) => new UdpChannel(device.ConnectionMode, device.LocalIpAddress, device.LocalPort, _configProvider, _logger),
            _ => throw new NotSupportedException($"Unsupported socket mode: {device.CommunicationType}/{device.ConnectionMode}")
        };
    }

    private void EmitLog(string message)
    {
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}

