namespace Vktun.IoT.Connector.Client.Models;

public class DeviceTestResult
{
    public string Address { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string DataType { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? Request { get; set; }
    public string? Response { get; set; }
    public double Duration { get; set; }
}

public class BatchTestResult
{
    public List<DeviceTestResult> Results { get; set; } = new();
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public double TotalDuration { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}
