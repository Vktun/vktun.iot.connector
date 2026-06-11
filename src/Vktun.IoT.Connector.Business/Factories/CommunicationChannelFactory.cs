using Vktun.IoT.Connector.Business.Services;
using Vktun.IoT.Connector.Communication.Channels;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;
using Vktun.IoT.Connector.Serial.Channels;
using SerialChannelParity = Vktun.IoT.Connector.Serial.Channels.Parity;
using SerialChannelStopBits = Vktun.IoT.Connector.Serial.Channels.StopBits;

namespace Vktun.IoT.Connector.Business.Factories;

public class CommunicationChannelFactory : ICommunicationChannelFactory
{
    private readonly IConfigurationProvider _configProvider;
    private readonly ILogger _logger;
    private readonly IHttpClientFactory? _httpClientFactory;

    public CommunicationChannelFactory(IConfigurationProvider configProvider, ILogger logger, IHttpClientFactory? httpClientFactory = null)
    {
        _configProvider = configProvider;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public ICommunicationChannel CreateChannel(DeviceInfo device)
    {
        var validation = ConnectionSettingsValidator.ValidateAndNormalize(device);
        if (!validation.IsValid || validation.Settings == null)
        {
            throw new InvalidOperationException(
                $"Invalid connection settings for device {device.DeviceId}: {validation.ErrorMessage}");
        }

        ConnectionSettingsValidator.ApplyNormalizedSettings(device, validation.Settings);

        ICommunicationChannel channel = (device.CommunicationType, device.ConnectionMode) switch
        {
            (CommunicationType.Tcp, ConnectionMode.Client) => new TcpClientChannel(_configProvider, _logger),
            (CommunicationType.Tcp, ConnectionMode.Server) => new TcpServerChannel(device.LocalIpAddress, device.LocalPort, _configProvider, _logger),
            (CommunicationType.Udp, _) => new UdpChannel(device.ConnectionMode, device.LocalIpAddress, device.LocalPort, _configProvider, _logger),
            (CommunicationType.TcpOverUdp, _) => new TcpOverUdpChannel(
                device.ConnectionMode,
                device.LocalIpAddress,
                device.LocalPort,
                _configProvider,
                _logger,
                allowAnonymousAcceptedClients: device.ConnectionMode == ConnectionMode.Server),
            (CommunicationType.UdpOverTcp, _) => new UdpOverTcpChannel(
                device.ConnectionMode,
                device.LocalIpAddress,
                device.LocalPort,
                _configProvider,
                _logger),
            (CommunicationType.Can, _) => new CanChannel(
                device.ConnectionMode,
                device.LocalIpAddress,
                device.LocalPort,
                _configProvider,
                _logger),
            (CommunicationType.FourG or CommunicationType.NbIoT, _) => new WirelessIpChannel(
                device.CommunicationType,
                device.ConnectionMode,
                device.LocalIpAddress,
                device.LocalPort,
                _configProvider,
                _logger),
            (CommunicationType.Http, ConnectionMode.Client) => new HttpClientChannel(_configProvider, _logger, _httpClientFactory),
            (CommunicationType.Mqtt, ConnectionMode.Client) => CreateMqttChannel(device),
            (CommunicationType.Serial, _) => new SerialChannel(
                device.SerialPort,
                device.BaudRate,
                _configProvider,
                _logger,
                device.DataBits,
                MapParity(device.Parity),
                MapStopBits(device.StopBits)),
            _ => throw new NotSupportedException($"Unsupported channel type: {device.CommunicationType}/{device.ConnectionMode}")
        };

        return channel;
    }

    public static IReconnectPolicy? CreateReconnectPolicy(DeviceInfo device)
    {
        var maxAttempts = GetInt(device, "ReconnectMaxAttempts");
        var baseIntervalMs = GetInt(device, "ReconnectBaseIntervalMs");
        var maxIntervalMs = GetInt(device, "ReconnectMaxIntervalMs");

        if (maxAttempts == null && baseIntervalMs == null && maxIntervalMs == null)
        {
            return null;
        }

        return new ExponentialBackoffReconnectPolicy(new ReconnectPolicyConfig
        {
            MaxAttempts = maxAttempts ?? 100,
            BaseIntervalMs = baseIntervalMs ?? 1000,
            MaxIntervalMs = maxIntervalMs ?? 30000
        });
    }

    private MqttChannel CreateMqttChannel(DeviceInfo device)
    {
        var mqttConfig = CreateMqttConfig(device);
        var channel = new MqttChannel(_configProvider, _logger, mqttConfig);

        var reconnectPolicy = CreateReconnectPolicy(device);
        if (reconnectPolicy != null)
        {
            channel.ReconnectPolicy = reconnectPolicy;
        }

        return channel;
    }

    private static MqttConfig CreateMqttConfig(DeviceInfo device)
    {
        return new MqttConfig
        {
            Server = GetString(device, "Server", "Host", "BrokerHost") ?? device.IpAddress,
            Port = GetInt(device, "Port", "BrokerPort") ?? (device.Port > 0 ? device.Port : 1883),
            ClientId = GetString(device, "ClientId") ?? (!string.IsNullOrWhiteSpace(device.DeviceId) ? device.DeviceId : Guid.NewGuid().ToString()),
            Username = GetString(device, "Username"),
            Password = GetString(device, "Password"),
            UseTls = GetBool(device, "UseTls") ?? false,
            QosLevel = GetInt(device, "Qos", "QosLevel") ?? 0,
            CleanSession = GetBool(device, "CleanSession") ?? true,
            KeepAlivePeriod = GetInt(device, "KeepAlivePeriod") ?? 60,
            AutoReconnect = GetBool(device, "AutoReconnect") ?? true,
            ReconnectDelay = GetInt(device, "ReconnectDelay") ?? 5000,
            WillTopic = GetString(device, "WillTopic"),
            WillMessage = GetString(device, "WillMessage"),
            SubscribeTopics = GetStringList(device, "SubscribeTopics", "Topics")
        };
    }

    private static string? GetString(DeviceInfo device, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (device.ExtendedProperties.TryGetValue(key, out var value) && value != null)
            {
                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static int? GetInt(DeviceInfo device, params string[] keys)
    {
        var value = GetString(device, keys);
        return int.TryParse(value, out var result) ? result : null;
    }

    private static bool? GetBool(DeviceInfo device, params string[] keys)
    {
        var value = GetString(device, keys);
        return bool.TryParse(value, out var result) ? result : null;
    }

    private static List<string> GetStringList(DeviceInfo device, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!device.ExtendedProperties.TryGetValue(key, out var value) || value == null)
            {
                continue;
            }

            if (value is IEnumerable<string> strings)
            {
                return strings.Where(topic => !string.IsNullOrWhiteSpace(topic)).ToList();
            }

            var text = value.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
        }

        return new List<string>();
    }

    private static SerialChannelParity MapParity(SerialParity parity)
    {
        return parity switch
        {
            SerialParity.Odd => SerialChannelParity.Odd,
            SerialParity.Even => SerialChannelParity.Even,
            SerialParity.Mark => SerialChannelParity.Mark,
            SerialParity.Space => SerialChannelParity.Space,
            _ => SerialChannelParity.None
        };
    }

    private static SerialChannelStopBits MapStopBits(SerialStopBits stopBits)
    {
        return stopBits switch
        {
            SerialStopBits.OnePointFive => SerialChannelStopBits.OnePointFive,
            SerialStopBits.Two => SerialChannelStopBits.Two,
            _ => SerialChannelStopBits.One
        };
    }
}
