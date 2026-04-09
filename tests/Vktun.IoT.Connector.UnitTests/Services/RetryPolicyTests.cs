using Vktun.IoT.Connector.Business.Services;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Services;

public class RetryPolicyTests
{
    private readonly MockLogger _logger;
    private readonly RetryPolicyConfig _config;

    public RetryPolicyTests()
    {
        _logger = new MockLogger();
        _config = new RetryPolicyConfig
        {
            MaxRetries = 3,
            InitialIntervalMs = 100,
            MaxIntervalMs = 1000,
            EnableJitter = false // 禁用抖动以便测试
        };
    }

    [Fact]
    public async Task ExecuteAsync_SuccessOnFirstAttempt_ReturnsResult()
    {
        // Arrange
        var policy = new RetryPolicy(_config, _logger);
        int callCount = 0;

        // Act
        var result = await policy.ExecuteAsync(async (ct) =>
        {
            callCount++;
            return "success";
        });

        // Assert
        Assert.Equal("success", result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_FailsThenSucceeds_ReturnsResult()
    {
        // Arrange
        var policy = new RetryPolicy(_config, _logger);
        int callCount = 0;

        // Act
        var result = await policy.ExecuteAsync(async (ct) =>
        {
            callCount++;
            if (callCount < 3)
            {
                throw new TimeoutException("Simulated timeout");
            }
            return "success";
        });

        // Assert
        Assert.Equal("success", result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_AlwaysFails_ThrowsException()
    {
        // Arrange
        var policy = new RetryPolicy(_config, _logger);
        int callCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await policy.ExecuteAsync(async (ct) =>
            {
                callCount++;
                throw new TimeoutException("Simulated timeout");
            });
        });

        // 应该调用 MaxRetries + 1 次（初次 + 重试）
        Assert.Equal(4, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_NonRetryableException_ThrowsImmediately()
    {
        // Arrange
        var policy = new RetryPolicy(_config, _logger);
        int callCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await policy.ExecuteAsync(async (ct) =>
            {
                callCount++;
                throw new ArgumentException("Invalid argument");
            });
        });

        // 不应该重试
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void CalculateDelay_ExponentialBackoff_IncreasesCorrectly()
    {
        // Arrange
        var policy = new RetryPolicy(_config, _logger);
        var method = typeof(RetryPolicy).GetMethod("CalculateDelay",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var delay1 = (TimeSpan)method!.Invoke(policy, new object[] { 0 })!;
        var delay2 = (TimeSpan)method!.Invoke(policy, new object[] { 1 })!;
        var delay3 = (TimeSpan)method!.Invoke(policy, new object[] { 2 })!;

        // Assert
        Assert.Equal(100, delay1.TotalMilliseconds);
        Assert.Equal(200, delay2.TotalMilliseconds);
        Assert.Equal(400, delay3.TotalMilliseconds);
    }

    [Fact]
    public void CalculateDelay_ExceedsMaxInterval_CapsAtMax()
    {
        // Arrange
        var policy = new RetryPolicy(_config, _logger);
        var method = typeof(RetryPolicy).GetMethod("CalculateDelay",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act: 尝试第10次重试，应该被限制在MaxIntervalMs
        var delay = (TimeSpan)method!.Invoke(policy, new object[] { 10 })!;

        // Assert
        Assert.Equal(1000, delay.TotalMilliseconds);
    }

    // 模拟日志器
    private class MockLogger : global::Vktun.IoT.Connector.Core.Interfaces.ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Fatal(string message, Exception? exception = null) { }
        public void Log(global::Vktun.IoT.Connector.Core.Enums.LogLevel level, string message, Exception? exception = null) { }
    }
}
