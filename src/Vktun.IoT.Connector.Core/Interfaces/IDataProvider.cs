using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces
{
    public interface IDataProvider
    {
        Task<bool> WriteDataAsync(DeviceData data);
        Task<bool> WriteDataBatchAsync(IEnumerable<DeviceData> dataList);
        Task<DeviceData?> ReadDataAsync(string deviceId, string pointName);
        Task<IEnumerable<DeviceData>> ReadDataHistoryAsync(string deviceId, DateTime startTime, DateTime endTime);
        Task<bool> ClearCacheAsync(string deviceId);
        Task<bool> ClearAllCacheAsync();
    
        event EventHandler<DataWrittenEventArgs>? DataWritten;
    }

    public class DataWrittenEventArgs : EventArgs
    {
        public string DeviceId { get; set; } = string.Empty;
        public DeviceData Data { get; set; } = new DeviceData();
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public interface IDataCache
    {
        int Count { get; }
        long MaxSize { get; set; }
    
        void Add(DeviceData data);
        void AddRange(IEnumerable<DeviceData> dataList);
        DeviceData? Get(string deviceId);
        IEnumerable<DeviceData> GetAll();
        IEnumerable<DeviceData> GetByDevice(string deviceId);
        bool Remove(string deviceId);
        void Clear();
        bool IsFull { get; }
    }
}
