using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.Business.Services;

/// <summary>
/// 重试策略配置
/// </summary>
public class RetryPolicyConfig
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 10;

    /// <summary>
    /// 初始重试间隔（毫秒）
    /// </summary>
    public int InitialIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 最大重试间隔（毫秒）
    /// </summary>
    public int MaxIntervalMs { get; set; } = 30000;

    /// <summary>
    /// 退避因子（指数退避的基数）
    /// </summary>
    public double BackoffFactor { get; set; } = 2.0;

    /// <summary>
    /// 是否启用抖动（避免雪崩效应）
    /// </summary>
    public bool EnableJitter { get; set; } = true;

    /// <summary>
    /// 哪些异常类型应该重试
    /// </summary>
    public List<Type> RetryableExceptions { get; set; } = new()
    {
        typeof(TimeoutException),
        typeof(System.Net.Sockets.SocketException),
        typeof(System.IO.IOException),
        typeof(OperationCanceledException)
    };
}

/// <summary>
/// 重试策略 - 实现指数退避和抖动机制
/// </summary>
public class RetryPolicy
{
    private readonly RetryPolicyConfig _config;
    private readonly ILogger _logger;

    public RetryPolicy(RetryPolicyConfig config, ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行带重试的操作
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var lastException = default(Exception);

        for (int attempt = 0; attempt <= _config.MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delay = CalculateDelay(attempt);
                    _logger.Info($"Retry attempt {attempt}/{_config.MaxRetries}, waiting {delay.TotalMilliseconds}ms");

                    await Task.Delay(delay, cancellationToken);
                }

                return await operation(cancellationToken);
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < _config.MaxRetries)
            {
                lastException = ex;
                _logger.Warning($"Operation failed (attempt {attempt + 1}/{_config.MaxRetries + 1}): {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            $"Operation failed after {_config.MaxRetries + 1} attempts",
            lastException);
    }

    /// <summary>
    /// 执行带重试的操作（无返回值）
    /// </summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async (ct) =>
        {
            await operation(ct);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// 计算重试延迟时间（指数退避 + 抖动）
    /// </summary>
    private TimeSpan CalculateDelay(int attempt)
    {
        // 指数退避：initialInterval * backoffFactor^attempt
        var exponentialDelay = _config.InitialIntervalMs * Math.Pow(_config.BackoffFactor, attempt);

        // 限制最大延迟
        var cappedDelay = Math.Min(exponentialDelay, _config.MaxIntervalMs);

        // 添加抖动（±25%）
        if (_config.EnableJitter)
        {
            var jitterRange = cappedDelay * 0.25;
            var jitter = Random.Shared.NextDouble() * jitterRange * 2 - jitterRange;
            cappedDelay += jitter;
        }

        return TimeSpan.FromMilliseconds(Math.Max(0, cappedDelay));
    }

    /// <summary>
    /// 判断异常是否可重试
    /// </summary>
    private bool IsRetryable(Exception exception)
    {
        var exceptionType = exception.GetType();

        // 检查是否在可重试列表中
        if (_config.RetryableExceptions.Any(t => t.IsAssignableFrom(exceptionType)))
        {
            return true;
        }

        // 特殊处理OperationCanceledException
        if (exception is OperationCanceledException canceledEx)
        {
            // 如果是超时导致的取消，可以重试
            return canceledEx.CancellationToken.IsCancellationRequested == false;
        }

        return false;
    }

    /// <summary>
    /// 创建默认重试策略
    /// </summary>
    public static RetryPolicy CreateDefault(ILogger logger)
    {
        return new RetryPolicy(new RetryPolicyConfig(), logger);
    }

    /// <summary>
    /// 创建激进重试策略（更短的间隔，更多重试次数）
    /// </summary>
    public static RetryPolicy CreateAggressive(ILogger logger)
    {
        return new RetryPolicy(new RetryPolicyConfig
        {
            MaxRetries = 20,
            InitialIntervalMs = 500,
            MaxIntervalMs = 10000,
            BackoffFactor = 1.5
        }, logger);
    }

    /// <summary>
    /// 创建保守重试策略（更长的间隔，更少重试次数）
    /// </summary>
    public static RetryPolicy CreateConservative(ILogger logger)
    {
        return new RetryPolicy(new RetryPolicyConfig
        {
            MaxRetries = 5,
            InitialIntervalMs = 5000,
            MaxIntervalMs = 60000,
            BackoffFactor = 3.0
        }, logger);
    }
}
