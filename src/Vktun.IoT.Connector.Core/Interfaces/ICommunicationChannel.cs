using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces
{
    public interface ICommunicationChannel : IAsyncDisposable, IDisposable
    {
        string ChannelId { get; }
        CommunicationType CommunicationType { get; }
        ConnectionMode ConnectionMode { get; }
        bool IsConnected { get; }
        int ActiveConnections { get; }
        ChannelStatistics Statistics { get; }

        Task<bool> OpenAsync(CancellationToken cancellationToken = default);
        Task CloseAsync();
        Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default);
        Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
        IAsyncEnumerable<ReceivedData> ReceiveAsync(CancellationToken cancellationToken = default);
        Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default);
        Task DisconnectDeviceAsync(string deviceId);
        ChannelStatistics ResetStatistics();

        event EventHandler<ChannelErrorEventArgs>? ErrorOccurred;
        event EventHandler<DeviceConnectedEventArgs>? DeviceConnected;
        event EventHandler<DeviceDisconnectedEventArgs>? DeviceDisconnected;
        event EventHandler<DataReceivedEventArgs>? DataReceived;
        event EventHandler<DataSentEventArgs>? DataSent;
    }

    public class ReceivedData
    {
        public string DeviceId { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class ChannelErrorEventArgs : EventArgs
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class DeviceConnectedEventArgs : EventArgs
    {
        public string DeviceId { get; set; } = string.Empty;
        public DeviceInfo Device { get; set; } = new DeviceInfo();
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class DeviceDisconnectedEventArgs : EventArgs
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class DataReceivedEventArgs : EventArgs
    {
        public string DeviceId { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class DataSentEventArgs : EventArgs
    {
        public string DeviceId { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public int BytesSent { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class ChannelStatistics
    {
        public long TotalBytesSent { get; set; }
        public long TotalBytesReceived { get; set; }
        public long TotalPacketsSent { get; set; }
        public long TotalPacketsReceived { get; set; }
        public long TotalErrors { get; set; }
        public long TotalConnections { get; set; }
        public long TotalDisconnections { get; set; }
        public DateTime? LastSendTime { get; set; }
        public DateTime? LastReceiveTime { get; set; }
        public DateTime? LastErrorTime { get; set; }
        public DateTime StartTime { get; set; } = DateTime.Now;

        public TimeSpan Uptime => DateTime.Now - StartTime;
        public double AveragePacketSizeSent => TotalPacketsSent > 0 ? (double)TotalBytesSent / TotalPacketsSent : 0;
        public double AveragePacketSizeReceived => TotalPacketsReceived > 0 ? (double)TotalBytesReceived / TotalPacketsReceived : 0;

        public ChannelStatistics Snapshot()
        {
            return new ChannelStatistics
            {
                TotalBytesSent = TotalBytesSent,
                TotalBytesReceived = TotalBytesReceived,
                TotalPacketsSent = TotalPacketsSent,
                TotalPacketsReceived = TotalPacketsReceived,
                TotalErrors = TotalErrors,
                TotalConnections = TotalConnections,
                TotalDisconnections = TotalDisconnections,
                LastSendTime = LastSendTime,
                LastReceiveTime = LastReceiveTime,
                LastErrorTime = LastErrorTime,
                StartTime = StartTime
            };
        }
    }
}
