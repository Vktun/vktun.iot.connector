using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Utils;

public sealed class NormalizedConnectionSettings
{
    public CommunicationType CommunicationType { get; init; }
    public ConnectionMode ConnectionMode { get; init; }
    public IPAddress? RemoteAddress { get; init; }
    public int RemotePort { get; init; }
    public IPAddress LocalAddress { get; init; } = IPAddress.Any;
    public int LocalPort { get; init; }

    public string RemoteIpAddressText => RemoteAddress?.ToString() ?? string.Empty;

    public string LocalIpAddressText => LocalAddress.Equals(IPAddress.Any)
        ? string.Empty
        : LocalAddress.ToString();
}

public sealed class ConnectionValidationResult
{
    public bool IsValid { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public NormalizedConnectionSettings? Settings { get; init; }
}

public static class ConnectionSettingsValidator
{
    public static ConnectionValidationResult ValidateAndNormalize(
        CommunicationType communicationType,
        ConnectionMode connectionMode,
        string? ipAddress,
        int port,
        string? localIpAddress,
        int localPort)
    {
        if (communicationType is not CommunicationType.Tcp and not CommunicationType.Udp)
        {
            return new ConnectionValidationResult
            {
                IsValid = true,
                Settings = new NormalizedConnectionSettings
                {
                    CommunicationType = communicationType,
                    ConnectionMode = connectionMode,
                    RemoteAddress = TryResolveIpAddress(ipAddress, allowWildcard: true, out _),
                    RemotePort = port,
                    LocalAddress = TryResolveIpAddress(localIpAddress, allowWildcard: true, out _) ?? IPAddress.Any,
                    LocalPort = localPort
                }
            };
        }

        return connectionMode switch
        {
            ConnectionMode.Client => ValidateClient(communicationType, connectionMode, ipAddress, port, localIpAddress, localPort),
            ConnectionMode.Server => ValidateServer(communicationType, connectionMode, ipAddress, port, localIpAddress, localPort),
            _ => Invalid($"Unsupported connection mode: {connectionMode}.")
        };
    }

    public static ConnectionValidationResult ValidateAndNormalize(DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return ValidateAndNormalize(
            device.CommunicationType,
            device.ConnectionMode,
            device.IpAddress,
            device.Port,
            device.LocalIpAddress,
            device.LocalPort);
    }

    public static bool TryNormalize(DeviceInfo device, out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(device);

        var result = ValidateAndNormalize(device);
        if (!result.IsValid || result.Settings == null)
        {
            errorMessage = result.ErrorMessage;
            return false;
        }

        ApplyNormalizedSettings(device, result.Settings);
        errorMessage = string.Empty;
        return true;
    }

    public static void ApplyNormalizedSettings(DeviceInfo device, NormalizedConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(settings);

        device.IpAddress = settings.RemoteIpAddressText;
        device.Port = settings.RemotePort;
        device.LocalIpAddress = settings.LocalIpAddressText;
        device.LocalPort = settings.LocalPort;
    }

    private static ConnectionValidationResult ValidateClient(
        CommunicationType communicationType,
        ConnectionMode connectionMode,
        string? ipAddress,
        int port,
        string? localIpAddress,
        int localPort)
    {
        var remoteAddress = TryResolveIpAddress(ipAddress, allowWildcard: false, out var remoteError);
        if (remoteAddress == null)
        {
            return Invalid(remoteError);
        }

        if (!IsValidPort(port))
        {
            return Invalid("Client mode requires a valid remote port between 1 and 65535.");
        }

        if (!TryResolveOptionalLocalAddress(localIpAddress, out var resolvedLocalAddress, out var localError))
        {
            return Invalid(localError);
        }

        if (localPort != 0 && !IsValidPort(localPort))
        {
            return Invalid("Local port must be 0 or between 1 and 65535.");
        }

        return Valid(new NormalizedConnectionSettings
        {
            CommunicationType = communicationType,
            ConnectionMode = connectionMode,
            RemoteAddress = remoteAddress,
            RemotePort = port,
            LocalAddress = resolvedLocalAddress ?? IPAddress.Any,
            LocalPort = localPort
        });
    }

    private static ConnectionValidationResult ValidateServer(
        CommunicationType communicationType,
        ConnectionMode connectionMode,
        string? ipAddress,
        int port,
        string? localIpAddress,
        int localPort)
    {
        if (!TryResolveOptionalRemoteFilter(ipAddress, out var remoteAddress, out var remoteError))
        {
            return Invalid(remoteError);
        }

        var effectiveLocalPort = localPort > 0 ? localPort : port;
        if (!IsValidPort(effectiveLocalPort))
        {
            return Invalid("Server mode requires a valid local listening port between 1 and 65535.");
        }

        if (!TryResolveOptionalLocalAddress(localIpAddress, out var resolvedLocalAddress, out var localError))
        {
            return Invalid(localError);
        }

        return Valid(new NormalizedConnectionSettings
        {
            CommunicationType = communicationType,
            ConnectionMode = connectionMode,
            RemoteAddress = remoteAddress,
            RemotePort = 0,
            LocalAddress = resolvedLocalAddress ?? IPAddress.Any,
            LocalPort = effectiveLocalPort
        });
    }

    private static bool TryResolveOptionalRemoteFilter(string? ipAddress, out IPAddress? address, out string errorMessage)
    {
        if (IsWildcard(ipAddress))
        {
            address = null;
            errorMessage = string.Empty;
            return true;
        }

        address = TryResolveIpAddress(ipAddress, allowWildcard: false, out errorMessage);
        return address != null;
    }

    private static bool TryResolveOptionalLocalAddress(string? localIpAddress, out IPAddress? address, out string errorMessage)
    {
        if (IsWildcard(localIpAddress))
        {
            address = IPAddress.Any;
            errorMessage = string.Empty;
            return true;
        }

        address = TryResolveIpAddress(localIpAddress, allowWildcard: false, out errorMessage);
        return address != null;
    }

    private static IPAddress? TryResolveIpAddress(string? value, bool allowWildcard, out string errorMessage)
    {
        if (IsWildcard(value))
        {
            if (allowWildcard)
            {
                errorMessage = string.Empty;
                return IPAddress.Any;
            }

            errorMessage = "A concrete IPv4 address is required.";
            return null;
        }

        if (value != null && value.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = string.Empty;
            return IPAddress.Loopback;
        }

        if (value != null &&
            IPAddress.TryParse(value, out var address) &&
            address.AddressFamily == AddressFamily.InterNetwork)
        {
            errorMessage = string.Empty;
            return address;
        }

        errorMessage = $"Invalid IPv4 address: {value}.";
        return null;
    }

    private static bool IsWildcard(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || value.Equals("*", StringComparison.OrdinalIgnoreCase)
            || value.Equals("any", StringComparison.OrdinalIgnoreCase)
            || value.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidPort(int port)
    {
        return port is >= 1 and <= 65535;
    }

    private static ConnectionValidationResult Valid(NormalizedConnectionSettings settings)
    {
        return new ConnectionValidationResult
        {
            IsValid = true,
            Settings = settings
        };
    }

    private static ConnectionValidationResult Invalid(string errorMessage)
    {
        return new ConnectionValidationResult
        {
            IsValid = false,
            ErrorMessage = errorMessage
        };
    }
}
