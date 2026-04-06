using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace Vktun.IoT.Connector.Configuration.Logging;

/// <summary>
/// 基于Serilog的日志器实现
/// </summary>
public class SerilogLogger : Core.Interfaces.ILogger, IDisposable
{
    private readonly Serilog.ILogger _serilogLogger;
    private readonly LogConfiguration _config;
    private bool _disposed = false;

    public SerilogLogger(LogConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(ConvertToSerilogLevel(config.MinimumLevel));

        // 配置控制台输出
        if (config.Console?.Enabled == true)
        {
            loggerConfig.WriteTo.Console(
                outputTemplate: config.Console.OutputTemplate,
                theme: AnsiConsoleTheme.Code);
        }

        // 配置文件输出
        if (config.File?.Enabled == true)
        {
            loggerConfig.WriteTo.File(
                path: config.File.Path,
                outputTemplate: config.File.OutputTemplate,
                rollingInterval: config.File.RollingInterval ? RollingInterval.Day : RollingInterval.Infinite,
                fileSizeLimitBytes: config.File.FileSizeLimitBytes > 0 ? config.File.FileSizeLimitBytes : null,
                retainedFileCountLimit: config.File.RetainedFileCountLimit > 0 ? config.File.RetainedFileCountLimit : null,
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1));
        }

        _serilogLogger = loggerConfig.CreateLogger();
    }

    /// <summary>
    /// 使用默认配置创建SerilogLogger
    /// </summary>
    public SerilogLogger() : this(new LogConfiguration())
    {
    }

    /// <summary>
    /// 记录日志
    /// </summary>
    public void Log(Core.Enums.LogLevel level, string message, Exception? exception = null)
    {
        if (_disposed)
            return;

        var logLevel = ConvertToSerilogLevel(level);

        if (exception != null)
        {
            _serilogLogger.Write(logLevel, exception, message);
        }
        else
        {
            _serilogLogger.Write(logLevel, message);
        }
    }

    /// <summary>
    /// 调试日志
    /// </summary>
    public void Debug(string message)
    {
        Log(Core.Enums.LogLevel.Debug, message);
    }

    /// <summary>
    /// 信息日志
    /// </summary>
    public void Info(string message)
    {
        Log(Core.Enums.LogLevel.Info, message);
    }

    /// <summary>
    /// 警告日志
    /// </summary>
    public void Warning(string message)
    {
        Log(Core.Enums.LogLevel.Warning, message);
    }

    /// <summary>
    /// 错误日志
    /// </summary>
    public void Error(string message, Exception? exception = null)
    {
        Log(Core.Enums.LogLevel.Error, message, exception);
    }

    /// <summary>
    /// 致命错误日志
    /// </summary>
    public void Fatal(string message, Exception? exception = null)
    {
        Log(Core.Enums.LogLevel.Fatal, message, exception);
    }

    /// <summary>
    /// 转换日志级别（从Core.LogLevel到Serilog.LogEventLevel）
    /// </summary>
    private static LogEventLevel ConvertToSerilogLevel(Core.Enums.LogLevel level)
    {
        return level switch
        {
            Core.Enums.LogLevel.Debug => LogEventLevel.Debug,
            Core.Enums.LogLevel.Info => LogEventLevel.Information,
            Core.Enums.LogLevel.Warning => LogEventLevel.Warning,
            Core.Enums.LogLevel.Error => LogEventLevel.Error,
            Core.Enums.LogLevel.Fatal => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }

    /// <summary>
    /// 转换日志级别（从配置枚举到Serilog.LogEventLevel）
    /// </summary>
    private static LogEventLevel ConvertToSerilogLevel(SerilogLogLevel level)
    {
        return level switch
        {
            SerilogLogLevel.Verbose => LogEventLevel.Verbose,
            SerilogLogLevel.Debug => LogEventLevel.Debug,
            SerilogLogLevel.Information => LogEventLevel.Information,
            SerilogLogLevel.Warning => LogEventLevel.Warning,
            SerilogLogLevel.Error => LogEventLevel.Error,
            SerilogLogLevel.Fatal => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            (_serilogLogger as IDisposable)?.Dispose();
            _disposed = true;
        }
    }
}
