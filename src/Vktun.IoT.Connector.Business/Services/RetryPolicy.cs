using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.Business.Services;

public class RetryPolicyConfig
{
    public int MaxRetries { get; set; } = 10;

    public int InitialIntervalMs { get; set; } = 1000;

    public int MaxIntervalMs { get; set; } = 30000;

    public double BackoffFactor { get; set; } = 2.0;

    public bool EnableJitter { get; set; } = true;

    public List<Type> RetryableExceptions { get; set; } = new()
    {
        typeof(TimeoutException),
        typeof(System.Net.Sockets.SocketException),
        typeof(System.IO.IOException),
        typeof(OperationCanceledException)
    };
}

public class RetryPolicy : IReconnectPolicy
{
    private readonly RetryPolicyConfig _config;
    private readonly ILogger _logger;

    public int? MaxAttempts => _config.MaxRetries;

    public RetryPolicy(RetryPolicyConfig config, ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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

    public TimeSpan? GetNextDelay(int attempt, Exception? lastException)
    {
        if (attempt > _config.MaxRetries)
        {
            return null;
        }

        if (lastException != null && !IsRetryable(lastException))
        {
            return null;
        }

        var delay = CalculateDelay(attempt);
        return delay;
    }

    public bool ShouldReconnect(Exception exception)
    {
        return IsRetryable(exception);
    }

    public void Reset()
    {
    }

    private TimeSpan CalculateDelay(int attempt)
    {
        var exponentialDelay = _config.InitialIntervalMs * Math.Pow(_config.BackoffFactor, attempt);

        var cappedDelay = Math.Min(exponentialDelay, _config.MaxIntervalMs);

        if (_config.EnableJitter)
        {
            var jitterRange = cappedDelay * 0.25;
            var jitter = Random.Shared.NextDouble() * jitterRange * 2 - jitterRange;
            cappedDelay += jitter;
        }

        return TimeSpan.FromMilliseconds(Math.Max(0, cappedDelay));
    }

    private bool IsRetryable(Exception exception)
    {
        var exceptionType = exception.GetType();

        if (_config.RetryableExceptions.Any(t => t.IsAssignableFrom(exceptionType)))
        {
            return true;
        }

        if (exception is OperationCanceledException canceledEx)
        {
            return canceledEx.CancellationToken.IsCancellationRequested == false;
        }

        return false;
    }

    public static RetryPolicy CreateDefault(ILogger logger)
    {
        return new RetryPolicy(new RetryPolicyConfig(), logger);
    }

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
