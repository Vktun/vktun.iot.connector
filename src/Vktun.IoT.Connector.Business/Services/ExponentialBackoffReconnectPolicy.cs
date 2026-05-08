using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.Business.Services;

public class ExponentialBackoffReconnectPolicy : IReconnectPolicy
{
    private readonly ReconnectPolicyConfig _config;

    public int? MaxAttempts => _config.MaxAttempts;

    public ExponentialBackoffReconnectPolicy(ReconnectPolicyConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public TimeSpan? GetNextDelay(int attempt, Exception? lastException)
    {
        if (attempt > _config.MaxAttempts)
        {
            return null;
        }

        if (lastException != null && !ShouldReconnect(lastException))
        {
            return null;
        }

        var exponentialDelay = _config.BaseIntervalMs * Math.Pow(_config.BackoffFactor, attempt - 1);
        var cappedDelay = Math.Min(exponentialDelay, _config.MaxIntervalMs);

        if (_config.EnableJitter)
        {
            var jitterRange = cappedDelay * 0.25;
            var jitter = Random.Shared.NextDouble() * jitterRange * 2 - jitterRange;
            cappedDelay += jitter;
        }

        return TimeSpan.FromMilliseconds(Math.Max(100, cappedDelay));
    }

    public bool ShouldReconnect(Exception exception)
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

    public void Reset()
    {
    }

    public static ExponentialBackoffReconnectPolicy CreateDefault()
    {
        return new ExponentialBackoffReconnectPolicy(new ReconnectPolicyConfig());
    }

    public static ExponentialBackoffReconnectPolicy CreateAggressive()
    {
        return new ExponentialBackoffReconnectPolicy(new ReconnectPolicyConfig
        {
            MaxAttempts = 200,
            BaseIntervalMs = 500,
            MaxIntervalMs = 10000,
            BackoffFactor = 1.5
        });
    }

    public static ExponentialBackoffReconnectPolicy CreateConservative()
    {
        return new ExponentialBackoffReconnectPolicy(new ReconnectPolicyConfig
        {
            MaxAttempts = 20,
            BaseIntervalMs = 5000,
            MaxIntervalMs = 60000,
            BackoffFactor = 3.0
        });
    }

    public static ExponentialBackoffReconnectPolicy FromGlobalConfig(int maxAttempts, int baseIntervalMs, int maxIntervalMs)
    {
        return new ExponentialBackoffReconnectPolicy(new ReconnectPolicyConfig
        {
            MaxAttempts = maxAttempts,
            BaseIntervalMs = baseIntervalMs,
            MaxIntervalMs = maxIntervalMs
        });
    }
}
