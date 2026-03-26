using System.Text.Json;
using System.Text.Json.Serialization;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.DeviceMock.Models;

namespace Vktun.IoT.Connector.DeviceMock.Services;

public class DeviceManager
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, IDeviceSimulator> _simulators = new();
    private readonly DataSimulator _dataSimulator = new();
    private readonly Dictionary<string, List<MockDataPoint>> _dataPoints = new();
    private readonly Dictionary<string, System.Threading.Timer> _timers = new();
    
    public DeviceManager(ILogger logger)
    {
        _logger = logger;
    }
    
    public async Task LoadConfigAsync(string configPath)
    {
        if (!File.Exists(configPath))
        {
            _logger.Warning($"配置文件不存在: {configPath}");
            return;
        }
        
        var json = await File.ReadAllTextAsync(configPath);
        var config = JsonSerializer.Deserialize<DeviceMockConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        });
        
        if (config == null || config.Devices.Count == 0)
        {
            _logger.Warning("未找到设备配置");
            return;
        }
        
        foreach (var deviceConfig in config.Devices.Where(d => d.Enabled))
        {
            _dataPoints[deviceConfig.DeviceId] = deviceConfig.DataPoints;
            _logger.Info($"加载设备配置: {deviceConfig.DeviceName} ({deviceConfig.ProtocolType})");
        }
    }
    
    public void RegisterSimulator(IDeviceSimulator simulator)
    {
        _simulators[simulator.DeviceId] = simulator;
        _logger.Info($"注册设备模拟器: {simulator.DeviceId}");
    }
    
    public async Task StartAllAsync(CancellationToken cancellationToken)
    {
        foreach (var simulator in _simulators.Values)
        {
            try
            {
                await simulator.StartAsync(cancellationToken);
                StartDataSimulation(simulator.DeviceId);
            }
            catch (Exception ex)
            {
                _logger.Error($"启动设备模拟器失败: {simulator.DeviceId}", ex);
            }
        }
    }
    
    public async Task StopAllAsync()
    {
        foreach (var timer in _timers.Values)
        {
            timer.Dispose();
        }
        _timers.Clear();
        
        foreach (var simulator in _simulators.Values)
        {
            try
            {
                await simulator.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"停止设备模拟器失败: {simulator.DeviceId}", ex);
            }
        }
    }
    
    private void StartDataSimulation(string deviceId)
    {
        if (!_dataPoints.TryGetValue(deviceId, out var dataPoints) || dataPoints.Count == 0)
        {
            return;
        }
        
        var timer = new System.Threading.Timer(_ =>
        {
            UpdateDataPoints(deviceId);
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        
        _timers[deviceId] = timer;
    }
    
    private void UpdateDataPoints(string deviceId)
    {
        if (!_simulators.TryGetValue(deviceId, out var simulator) || !simulator.IsRunning)
        {
            return;
        }
        
        if (!_dataPoints.TryGetValue(deviceId, out var dataPoints))
        {
            return;
        }
        
        foreach (var dataPoint in dataPoints)
        {
            if (_dataSimulator.ShouldUpdate(dataPoint))
            {
                var value = _dataSimulator.GenerateValue(dataPoint);
                var convertedValue = _dataSimulator.ConvertToDataType(value, dataPoint.DataType);
                simulator.SetDataPoint(dataPoint.Address, convertedValue);
            }
        }
    }
    
    public IDeviceSimulator? GetSimulator(string deviceId)
    {
        return _simulators.TryGetValue(deviceId, out var simulator) ? simulator : null;
    }
    
    public IEnumerable<IDeviceSimulator> GetAllSimulators()
    {
        return _simulators.Values;
    }
}
