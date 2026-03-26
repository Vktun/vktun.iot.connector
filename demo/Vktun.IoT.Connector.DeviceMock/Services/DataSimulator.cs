using Vktun.IoT.Connector.DeviceMock.Models;

namespace Vktun.IoT.Connector.DeviceMock.Services;

public class DataSimulator
{
    private readonly Random _random = new();
    private readonly Dictionary<string, double> _currentValues = new();
    private readonly Dictionary<string, DateTime> _lastUpdateTimes = new();
    
    public double GenerateValue(MockDataPoint dataPoint)
    {
        var now = DateTime.Now;
        var timeSinceStart = (now - DateTime.Today).TotalSeconds;
        var period = dataPoint.UpdateInterval / 1000.0;
        
        var value = dataPoint.SimulationType switch
        {
            DataSimulationType.Static => dataPoint.InitialValue as double? ?? dataPoint.MinValue,
            DataSimulationType.Random => _random.NextDouble() * (dataPoint.MaxValue - dataPoint.MinValue) + dataPoint.MinValue,
            DataSimulationType.Sine => (Math.Sin(timeSinceStart * 2 * Math.PI / period) + 1) / 2 * (dataPoint.MaxValue - dataPoint.MinValue) + dataPoint.MinValue,
            DataSimulationType.Linear => (timeSinceStart % period) / period * (dataPoint.MaxValue - dataPoint.MinValue) + dataPoint.MinValue,
            DataSimulationType.Step => timeSinceStart % (period * 2) < period ? dataPoint.MinValue : dataPoint.MaxValue,
            _ => dataPoint.MinValue
        };
        
        _currentValues[dataPoint.Address] = value;
        _lastUpdateTimes[dataPoint.Address] = now;
        
        return value;
    }
    
    public object ConvertToDataType(double value, Core.Enums.DataType dataType)
    {
        return dataType switch
        {
            Core.Enums.DataType.UInt8 => (byte)value,
            Core.Enums.DataType.Int8 => (sbyte)value,
            Core.Enums.DataType.UInt16 => (ushort)value,
            Core.Enums.DataType.Int16 => (short)value,
            Core.Enums.DataType.UInt32 => (uint)value,
            Core.Enums.DataType.Int32 => (int)value,
            Core.Enums.DataType.UInt64 => (ulong)value,
            Core.Enums.DataType.Int64 => (long)value,
            Core.Enums.DataType.Float => (float)value,
            Core.Enums.DataType.Double => value,
            _ => value
        };
    }
    
    public bool ShouldUpdate(MockDataPoint dataPoint)
    {
        if (!_lastUpdateTimes.TryGetValue(dataPoint.Address, out var lastUpdate))
        {
            return true;
        }
        
        var elapsed = (DateTime.Now - lastUpdate).TotalMilliseconds;
        return elapsed >= dataPoint.UpdateInterval;
    }
}
