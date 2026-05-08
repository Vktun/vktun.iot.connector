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
        private long _totalBytesSent;
        private long _totalBytesReceived;
        private long _totalPacketsSent;
        private long _totalPacketsReceived;
        private long _totalErrors;
        private long _totalConnections;
        private long _totalDisconnections;

        public long TotalBytesSent
        {
            get => Interlocked.Read(ref _totalBytesSent);
            set => Interlocked.Exchange(ref _totalBytesSent, value);
        }
        public long TotalBytesReceived
        {
            get => Interlocked.Read(ref _totalBytesReceived);
            set => Interlocked.Exchange(ref _totalBytesReceived, value);
        }
        public long TotalPacketsSent
        {
            get => Interlocked.Read(ref _totalPacketsSent);
            set => Interlocked.Exchange(ref _totalPacketsSent, value);
        }
        public long TotalPacketsReceived
        {
            get => Interlocked.Read(ref _totalPacketsReceived);
            set => Interlocked.Exchange(ref _totalPacketsReceived, value);
        }
        public long TotalErrors
        {
            get => Interlocked.Read(ref _totalErrors);
            set => Interlocked.Exchange(ref _totalErrors, value);
        }
        public long TotalConnections
        {
            get => Interlocked.Read(ref _totalConnections);
            set => Interlocked.Exchange(ref _totalConnections, value);
        }
        public long TotalDisconnections
        {
            get => Interlocked.Read(ref _totalDisconnections);
            set => Interlocked.Exchange(ref _totalDisconnections, value);
        }
        public DateTime? LastSendTime { get; set; }
        public DateTime? LastReceiveTime { get; set; }
        public DateTime? LastErrorTime { get; set; }
        public DateTime StartTime { get; set; } = DateTime.Now;

        public TimeSpan Uptime => DateTime.Now - StartTime;
        public double AveragePacketSizeSent => TotalPacketsSent > 0 ? (double)TotalBytesSent / TotalPacketsSent : 0;
        public double AveragePacketSizeReceived => TotalPacketsReceived > 0 ? (double)TotalBytesReceived / TotalPacketsReceived : 0;

        public void IncrementTotalBytesSent(long value) => Interlocked.Add(ref _totalBytesSent, value);
        public void IncrementTotalBytesReceived(long value) => Interlocked.Add(ref _totalBytesReceived, value);
        public void IncrementTotalPacketsSent() => Interlocked.Increment(ref _totalPacketsSent);
        public void IncrementTotalPacketsReceived() => Interlocked.Increment(ref _totalPacketsReceived);
        public void IncrementTotalErrors() => Interlocked.Increment(ref _totalErrors);
        public void IncrementTotalConnections() => Interlocked.Increment(ref _totalConnections);
        public void IncrementTotalDisconnections() => Interlocked.Increment(ref _totalDisconnections);

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
