namespace Vktun.IoT.Connector.Configuration.Logging;

/// <summary>
/// Serilog日志配置
/// </summary>
public class LogConfiguration
{
    /// <summary>
    /// 最小日志级别
    /// </summary>
    public SerilogLogLevel MinimumLevel { get; set; } = SerilogLogLevel.Information;

    /// <summary>
    /// 是否启用结构化日志（JSON格式）
    /// </summary>
    public bool EnableStructuredLogging { get; set; } = true;

    /// <summary>
    /// 控制台日志配置
    /// </summary>
    public ConsoleLogConfig? Console { get; set; } = new();

    /// <summary>
    /// 文件日志配置
    /// </summary>
    public FileLogConfig? File { get; set; } = new();

    /// <summary>
    /// Elasticsearch日志配置
    /// </summary>
    public ElasticsearchLogConfig? Elasticsearch { get; set; }
}

/// <summary>
/// 控制台日志配置
/// </summary>
public class ConsoleLogConfig
{
    /// <summary>
    /// 是否启用控制台日志
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 输出模板
    /// </summary>
    public string OutputTemplate { get; set; } = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}";
}

/// <summary>
/// 文件日志配置
/// </summary>
public class FileLogConfig
{
    /// <summary>
    /// 是否启用文件日志
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 日志文件路径（支持相对路径）
    /// </summary>
    public string Path { get; set; } = "logs/log-.txt";

    /// <summary>
    /// 文件大小限制（字节），0表示不限制
    /// </summary>
    public long FileSizeLimitBytes { get; set; } = 100 * 1024 * 1024; // 100MB

    /// <summary>
    /// 保留的最多文件数，0表示不限制
    /// </summary>
    public int RetainedFileCountLimit { get; set; } = 30;

    /// <summary>
    /// 是否按天滚动
    /// </summary>
    public bool RollingInterval { get; set; } = true;

    /// <summary>
    /// 输出模板
    /// </summary>
    public string OutputTemplate { get; set; } = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
}

/// <summary>
/// Elasticsearch日志配置
/// </summary>
public class ElasticsearchLogConfig
{
    /// <summary>
    /// 是否启用Elasticsearch日志
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Elasticsearch节点URL
    /// </summary>
    public string NodeUri { get; set; } = "http://localhost:9200";

    /// <summary>
    /// 索引名称
    /// </summary>
    public string IndexFormat { get; set; } = "vktun-logs-{0:yyyy.MM.dd}";

    /// <summary>
    /// 批量发布大小
    /// </summary>
    public int BatchPostingLimit { get; set; } = 50;

    /// <summary>
    /// 批量发布间隔（秒）
    /// </summary>
    public int PeriodSeconds { get; set; } = 2;
}

/// <summary>
/// Serilog日志级别
/// </summary>
public enum SerilogLogLevel
{
    Verbose = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5
}
