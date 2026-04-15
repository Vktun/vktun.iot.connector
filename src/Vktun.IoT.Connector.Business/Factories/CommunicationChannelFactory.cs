using Vktun.IoT.Connector.Communication.Channels;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;
using Vktun.IoT.Connector.Serial.Channels;

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

        return (device.CommunicationType, device.ConnectionMode) switch
        {
            (CommunicationType.Tcp, ConnectionMode.Client) => new TcpClientChannel(_configProvider, _logger),
            (CommunicationType.Tcp, ConnectionMode.Server) => new TcpServerChannel(device.LocalIpAddress, device.LocalPort, _configProvider, _logger),
            (CommunicationType.Udp, _) => new UdpChannel(device.ConnectionMode, device.LocalIpAddress, device.LocalPort, _configProvider, _logger),
            (CommunicationType.Http, ConnectionMode.Client) => new HttpClientChannel(_configProvider, _logger, _httpClientFactory),
            (CommunicationType.Mqtt, ConnectionMode.Client) => new MqttChannel(_configProvider, _logger, CreateMqttConfig(device)),
            (CommunicationType.Serial, _) => new SerialChannel(device.SerialPort, device.BaudRate, _configProvider, _logger),
            _ => throw new NotSupportedException($"Unsupported channel type: {device.CommunicationType}/{device.ConnectionMode}")
        };
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
}
