using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.Configuration.Logging;

public class ConsoleLogger : ILogger
{
    private readonly LogLevel _minLevel;

    public ConsoleLogger(LogLevel minLevel = LogLevel.Debug)
    {
        _minLevel = minLevel;
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (level < _minLevel)
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelStr = level.ToString().ToUpper().PadRight(5);
        var logMessage = $"[{timestamp}] [{levelStr}] {message}";
        
        if (exception != null)
        {
            logMessage += $" | Exception: {exception.Message}";
            if (level >= LogLevel.Error)
            {
                logMessage += $"\n{exception.StackTrace}";
            }
        }

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = GetColor(level);
        Console.WriteLine(logMessage);
        Console.ForegroundColor = originalColor;
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
    public void Fatal(string message, Exception? exception = null) => Log(LogLevel.Fatal, message, exception);

    private static ConsoleColor GetColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Fatal => ConsoleColor.DarkRed,
            _ => ConsoleColor.White
        };
    }
}

public class FileLogger : ILogger
{
    private readonly string _logDirectory;
    private readonly LogLevel _minLevel;
    private readonly object _lockObject = new();

    public FileLogger(string logDirectory, LogLevel minLevel = LogLevel.Debug)
    {
        _logDirectory = logDirectory;
        _minLevel = minLevel;
        
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (level < _minLevel)
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelStr = level.ToString().ToUpper().PadRight(5);
        var logMessage = $"[{timestamp}] [{levelStr}] {message}";
        
        if (exception != null)
        {
            logMessage += $" | Exception: {exception.Message}";
            if (level >= LogLevel.Error)
            {
                logMessage += $"\n{exception.StackTrace}";
            }
        }

        var logFile = Path.Combine(_logDirectory, $"log_{DateTime.Now:yyyyMMdd}.log");
        
        lock (_lockObject)
        {
            File.AppendAllText(logFile, logMessage + "\n");
        }
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
    public void Fatal(string message, Exception? exception = null) => Log(LogLevel.Fatal, message, exception);
}

public class CompositeLogger : ILogger
{
    private readonly List<ILogger> _loggers;

    public CompositeLogger(params ILogger[] loggers)
    {
        _loggers = loggers.ToList();
    }

    public void AddLogger(ILogger logger)
    {
        _loggers.Add(logger);
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        foreach (var logger in _loggers)
        {
            logger.Log(level, message, exception);
        }
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
    public void Fatal(string message, Exception? exception = null) => Log(LogLevel.Fatal, message, exception);
}
