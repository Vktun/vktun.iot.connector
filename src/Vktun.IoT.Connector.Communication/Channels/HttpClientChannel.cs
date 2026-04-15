using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Communication.Channels;

public class HttpClientChannel : CommunicationChannelBase
{
    private const string ClientName = "Vktun.IoT.Connector.Http";

    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory? _ownedHttpClientFactory;
    private readonly ConcurrentDictionary<string, HttpDeviceEndpoint> _endpoints = new();
    private readonly HttpConfig _httpConfig;

    public override CommunicationType CommunicationType => CommunicationType.Http;
    public override ConnectionMode ConnectionMode => ConnectionMode.Client;

    public HttpClientChannel(IConfigurationProvider configProvider, ILogger logger, IHttpClientFactory? httpClientFactory = null)
        : base(configProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(configProvider);

        _httpConfig = configProvider.GetConfig().Http;
        var factory = httpClientFactory ?? new PooledHttpClientFactory(_httpConfig);
        _ownedHttpClientFactory = httpClientFactory == null ? factory : null;
        _httpClient = factory.CreateClient(ClientName);
        ChannelId = "HttpClient";
    }

    public override Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = true;
        return Task.FromResult(true);
    }

    public override async Task CloseAsync()
    {
        if (!_isConnected && _connections.IsEmpty)
        {
            return;
        }

        _isConnected = false;
        var deviceIds = _connections.Keys.ToArray();
        foreach (var deviceId in deviceIds)
        {
            await DisconnectDeviceAsync(deviceId).ConfigureAwait(false);
        }
    }

    public override Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default)
    {
        return SendAsync(deviceId, new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public override async Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || !_connections.TryGetValue(deviceId, out var connection))
        {
            return 0;
        }

        if (!_endpoints.TryGetValue(deviceId, out var endpoint))
        {
            OnErrorOccurred(deviceId, $"HTTP endpoint for device {deviceId} is not connected.");
            return 0;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_httpConfig.RequestTimeout > 0)
        {
            timeoutCts.CancelAfter(_httpConfig.RequestTimeout);
        }

        try
        {
            using var request = CreateRequest(endpoint, data);
            OnDataSent(deviceId, data.ToArray(), data.Length);
            connection.BytesSent += data.Length;
            connection.LastActiveTime = DateTime.Now;

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
            var responseData = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                OnErrorOccurred(deviceId, $"HTTP request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).");
                return 0;
            }

            connection.BytesReceived += responseData.Length;
            connection.LastActiveTime = DateTime.Now;
            OnDataReceived(deviceId, responseData);
            return data.Length;
        }
        catch (OperationCanceledException)
        {
            _logger.Debug($"HTTP request canceled for device {deviceId}.");
            return 0;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(deviceId, $"HTTP request failed: {ex.Message}", ex);
            return 0;
        }
    }

    public override async IAsyncEnumerable<ReceivedData> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotSupportedException("HttpClientChannel uses request-response data reception (DataReceived event). Use SendAsync to issue HTTP requests.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public override Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!_isConnected)
        {
            _isConnected = true;
        }

        if (_connections.ContainsKey(device.DeviceId))
        {
            return Task.FromResult(true);
        }

        if (!TryCreateEndpoint(device, out var endpoint, out var errorMessage))
        {
            OnErrorOccurred(device.DeviceId, errorMessage);
            return Task.FromResult(false);
        }

        var connection = new DeviceConnection
        {
            DeviceId = device.DeviceId,
            RemoteEndPoint = null,
            ConnectTime = DateTime.Now,
            LastActiveTime = DateTime.Now,
            ReceiveBuffer = Array.Empty<byte>(),
            CancellationTokenSource = new CancellationTokenSource()
        };

        _connections[device.DeviceId] = connection;
        _endpoints[device.DeviceId] = endpoint;
        ChannelId = $"HttpClient_{endpoint.RequestUri.Host}_{endpoint.RequestUri.Port}";
        OnDeviceConnected(device.DeviceId, device);
        return Task.FromResult(true);
    }

    public override Task DisconnectDeviceAsync(string deviceId)
    {
        if (_connections.TryRemove(deviceId, out var connection))
        {
            connection.CancellationTokenSource?.Cancel();
            connection.CancellationTokenSource?.Dispose();
            _endpoints.TryRemove(deviceId, out _);
            OnDeviceDisconnected(deviceId, "Disconnected");
        }

        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _httpClient.Dispose();

        if (_ownedHttpClientFactory is IDisposable disposableFactory)
        {
            disposableFactory.Dispose();
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    private HttpRequestMessage CreateRequest(HttpDeviceEndpoint endpoint, ReadOnlyMemory<byte> data)
    {
        var request = new HttpRequestMessage(endpoint.Method, endpoint.RequestUri);
        foreach (var header in endpoint.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (data.Length > 0 || endpoint.Method != HttpMethod.Get)
        {
            request.Content = new ByteArrayContent(data.ToArray());
            request.Content.Headers.TryAddWithoutValidation("Content-Type", endpoint.ContentType);
        }

        return request;
    }

    private bool TryCreateEndpoint(DeviceInfo device, out HttpDeviceEndpoint endpoint, out string errorMessage)
    {
        endpoint = default!;
        errorMessage = string.Empty;

        var requestUriText = GetString(device, "Url", "RequestUri", "EndpointUrl");
        Uri requestUri;
        if (!string.IsNullOrWhiteSpace(requestUriText) &&
            Uri.TryCreate(requestUriText, UriKind.Absolute, out var absoluteRequestUri))
        {
            requestUri = absoluteRequestUri;
        }
        else
        {
            var baseAddressText = GetString(device, "BaseAddress", "BaseUrl");
            Uri? baseAddress = null;
            if (!string.IsNullOrWhiteSpace(baseAddressText))
            {
                baseAddress = TryCreateBaseUri(baseAddressText);
            }
            else if (Uri.TryCreate(device.IpAddress, UriKind.Absolute, out var absoluteIpAddressUri))
            {
                baseAddress = absoluteIpAddressUri;
            }
            else if (!string.IsNullOrWhiteSpace(device.IpAddress))
            {
                var scheme = GetString(device, "Scheme") ?? _httpConfig.DefaultScheme;
                var port = device.Port > 0 ? $":{device.Port}" : string.Empty;
                baseAddress = TryCreateBaseUri($"{scheme}://{device.IpAddress}{port}/");
            }

            if (baseAddress == null)
            {
                errorMessage = "HTTP client mode requires BaseAddress, BaseUrl, Url, RequestUri, EndpointUrl, or IpAddress.";
                return false;
            }

            var path = GetString(device, "Path", "EndpointPath") ?? requestUriText ?? string.Empty;
            requestUri = string.IsNullOrWhiteSpace(path) ? baseAddress : new Uri(baseAddress, path);
        }

        var methodName = GetString(device, "Method", "HttpMethod") ?? _httpConfig.DefaultMethod;
        var contentType = GetString(device, "ContentType") ?? _httpConfig.DefaultContentType;
        endpoint = new HttpDeviceEndpoint(requestUri, new HttpMethod(methodName), contentType, GetHeaders(device));
        return true;
    }

    private static Uri? TryCreateBaseUri(string value)
    {
        var normalized = value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static string? GetString(DeviceInfo device, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!device.ExtendedProperties.TryGetValue(key, out var value) || value == null)
            {
                continue;
            }

            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    return jsonElement.GetString();
                }

                continue;
            }

            var text = value.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static Dictionary<string, string> GetHeaders(DeviceInfo device)
    {
        if (!device.ExtendedProperties.TryGetValue("Headers", out var headersValue) || headersValue == null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (headersValue is Dictionary<string, string> stringHeaders)
        {
            return new Dictionary<string, string>(stringHeaders, StringComparer.OrdinalIgnoreCase);
        }

        if (headersValue is IDictionary<string, object> objectHeaders)
        {
            return objectHeaders
                .Where(pair => pair.Value != null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        if (headersValue is JsonElement { ValueKind: JsonValueKind.Object } jsonElement)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in jsonElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    headers[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }

            return headers;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record HttpDeviceEndpoint(
        Uri RequestUri,
        HttpMethod Method,
        string ContentType,
        Dictionary<string, string> Headers);

    private sealed class PooledHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly SocketsHttpHandler _handler;

        public PooledHttpClientFactory(HttpConfig config)
        {
            _handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = Math.Max(1, config.MaxConnectionsPerServer),
                PooledConnectionLifetime = TimeSpan.FromSeconds(Math.Max(1, config.PooledConnectionLifetimeSeconds)),
                UseCookies = false
            };
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }

        public void Dispose()
        {
            _handler.Dispose();
        }
    }
}
