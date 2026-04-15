using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces
{
    /// <summary>
    /// Provides the high-level data access API for recent, historical, and replay device values.
    /// </summary>
    public interface IDataProvider
    {
        /// <summary>
        /// Writes one device data snapshot to the recent-value cache and optional persistent store.
        /// </summary>
        /// <param name="data">The data snapshot to write.</param>
        /// <returns><c>true</c> when the snapshot was accepted according to the configured failure strategy.</returns>
        Task<bool> WriteDataAsync(DeviceData data);

        /// <summary>
        /// Writes multiple device data snapshots to the recent-value cache and optional persistent store.
        /// </summary>
        /// <param name="dataList">The data snapshots to write.</param>
        /// <returns><c>true</c> when the snapshots were accepted according to the configured failure strategy.</returns>
        Task<bool> WriteDataBatchAsync(IEnumerable<DeviceData> dataList);

        /// <summary>
        /// Reads the latest cached value for a device point.
        /// </summary>
        /// <param name="deviceId">The device identifier.</param>
        /// <param name="pointName">The point name within the latest device data snapshot.</param>
        /// <returns>The latest matching device data snapshot, or <c>null</c> when no matching point exists.</returns>
        Task<DeviceData?> ReadDataAsync(string deviceId, string pointName);

        /// <summary>
        /// Reads historical values for a device from persistent storage when enabled, otherwise from the recent-value cache.
        /// </summary>
        /// <param name="deviceId">The device identifier.</param>
        /// <param name="startTime">The inclusive collection time lower bound.</param>
        /// <param name="endTime">The inclusive collection time upper bound.</param>
        /// <returns>The matching data snapshots ordered by collection time.</returns>
        Task<IEnumerable<DeviceData>> ReadDataHistoryAsync(string deviceId, DateTime startTime, DateTime endTime);

        /// <summary>
        /// Reads data snapshots prepared for replay in collection-time order.
        /// </summary>
        /// <param name="deviceId">The device identifier.</param>
        /// <param name="startTime">The inclusive collection time lower bound.</param>
        /// <param name="endTime">The inclusive collection time upper bound.</param>
        /// <param name="maxCount">The maximum number of replay snapshots to return.</param>
        /// <returns>The replay snapshots ordered by collection time.</returns>
        Task<IEnumerable<DeviceData>> ReadReplayDataAsync(string deviceId, DateTime startTime, DateTime endTime, int maxCount = 1000);

        /// <summary>
        /// Clears the recent-value cache for one device.
        /// </summary>
        /// <param name="deviceId">The device identifier.</param>
        /// <returns><c>true</c> when a cached value was removed.</returns>
        Task<bool> ClearCacheAsync(string deviceId);

        /// <summary>
        /// Clears all recent-value cache entries.
        /// </summary>
        /// <returns><c>true</c> when the cache was cleared.</returns>
        Task<bool> ClearAllCacheAsync();
    
        /// <summary>
        /// Occurs after a data snapshot is accepted by the provider.
        /// </summary>
        event EventHandler<DataWrittenEventArgs>? DataWritten;
    }

    /// <summary>
    /// Provides event data for accepted device data writes.
    /// </summary>
    public class DataWrittenEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the device identifier.
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the written data snapshot.
        /// </summary>
        public DeviceData Data { get; set; } = new DeviceData();

        /// <summary>
        /// Gets or sets the time when the provider accepted the write.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Stores recent values in memory. Historical and replay values belong in <see cref="IPersistentDataStore"/>.
    /// </summary>
    public interface IDataCache
    {
        /// <summary>
        /// Gets the number of cached device snapshots.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Gets or sets the maximum number of devices retained in the recent-value cache.
        /// </summary>
        long MaxSize { get; set; }
    
        /// <summary>
        /// Adds or replaces the latest snapshot for a device.
        /// </summary>
        /// <param name="data">The data snapshot to cache.</param>
        void Add(DeviceData data);

        /// <summary>
        /// Adds or replaces the latest snapshots for multiple devices.
        /// </summary>
        /// <param name="dataList">The data snapshots to cache.</param>
        void AddRange(IEnumerable<DeviceData> dataList);

        /// <summary>
        /// Gets the latest snapshot for a device.
        /// </summary>
        /// <param name="deviceId">The device identifier.</param>
        /// <returns>The latest cached snapshot, or <c>null</c>.</returns>
        DeviceData? Get(string deviceId);

        /// <summary>
        /// Gets all latest device snapshots currently in memory.
        /// </summary>
        /// <returns>The latest snapshots.</returns>
        IEnumerable<DeviceData> GetAll();

        /// <summary>
        /// Gets the latest snapshot for a device as a sequence.
        /// </summary>
        /// <param name="deviceId">The device identifier.</param>
        /// <returns>The matching latest snapshot sequence.</returns>
        IEnumerable<DeviceData> GetByDevice(string deviceId);

        /// <summary>
        /// Removes the latest snapshot for a device.
        /// </summary>
        /// <param name="deviceId">The device identifier.</param>
        /// <returns><c>true</c> when a cached value was removed.</returns>
        bool Remove(string deviceId);

        /// <summary>
        /// Clears all latest snapshots from memory.
        /// </summary>
        void Clear();

        /// <summary>
        /// Gets a value indicating whether the cache has reached its maximum size.
        /// </summary>
        bool IsFull { get; }
    }

    /// <summary>
    /// Stores historical and replay values independently from the in-memory recent-value cache.
    /// </summary>
    public interface IPersistentDataStore
    {
        /// <summary>
        /// Gets the storage backend type implemented by this store.
        /// </summary>
        DataPersistenceBackend Backend { get; }

        /// <summary>
        /// Writes one data snapshot to persistent storage.
        /// </summary>
        /// <param name="data">The data snapshot to write.</param>
        /// <param name="cancellationToken">A token that can cancel the write.</param>
        /// <returns><c>true</c> when the snapshot was accepted for storage.</returns>
        Task<bool> WriteAsync(DeviceData data, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes multiple data snapshots to persistent storage.
        /// </summary>
        /// <param name="dataList">The data snapshots to write.</param>
        /// <param name="cancellationToken">A token that can cancel the write.</param>
        /// <returns><c>true</c> when the snapshots were accepted for storage.</returns>
        Task<bool> WriteBatchAsync(IEnumerable<DeviceData> dataList, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads stored snapshots for history or replay.
        /// </summary>
        /// <param name="deviceId">The device identifier.</param>
        /// <param name="startTime">The inclusive collection time lower bound.</param>
        /// <param name="endTime">The inclusive collection time upper bound.</param>
        /// <param name="purpose">The read purpose, either history or replay.</param>
        /// <param name="maxCount">The maximum number of snapshots to return.</param>
        /// <param name="cancellationToken">A token that can cancel the read.</param>
        /// <returns>The matching snapshots ordered by collection time.</returns>
        Task<IReadOnlyList<DeviceData>> ReadAsync(
            string deviceId,
            DateTime startTime,
            DateTime endTime,
            DataCachePurpose purpose,
            int maxCount,
            CancellationToken cancellationToken = default);
    }
}
