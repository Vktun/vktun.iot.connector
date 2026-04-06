using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Interfaces;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Core;

public class DeviceStateMachineTests
{
    private readonly MockLogger _logger;
    private readonly DeviceStateMachine _stateMachine;

    public DeviceStateMachineTests()
    {
        _logger = new MockLogger();
        _stateMachine = new DeviceStateMachine("test_device_001", _logger);
    }

    [Fact]
    public void InitialState_ShouldBeOffline()
    {
        // Assert
        Assert.Equal(DeviceStatus.Offline, _stateMachine.CurrentState);
    }

    [Fact]
    public void Transition_OfflineToConnecting_ShouldSucceed()
    {
        // Act
        var result = _stateMachine.TransitionTo(DeviceStatus.Connecting);

        // Assert
        Assert.True(result);
        Assert.Equal(DeviceStatus.Connecting, _stateMachine.CurrentState);
    }

    [Fact]
    public void Transition_ConnectingToOnline_ShouldSucceed()
    {
        // Arrange
        _stateMachine.TransitionTo(DeviceStatus.Connecting);

        // Act
        var result = _stateMachine.TransitionTo(DeviceStatus.Online);

        // Assert
        Assert.True(result);
        Assert.Equal(DeviceStatus.Online, _stateMachine.CurrentState);
    }

    [Fact]
    public void Transition_OfflineToOnline_ShouldFail()
    {
        // Act
        var result = _stateMachine.TransitionTo(DeviceStatus.Online);

        // Assert
        Assert.False(result);
        Assert.Equal(DeviceStatus.Offline, _stateMachine.CurrentState);
    }

    [Fact]
    public void RecordError_ErrorCountIncreases()
    {
        // Act
        _stateMachine.RecordError(new InvalidOperationException("Test error"));
        _stateMachine.RecordError(new TimeoutException("Timeout"));

        // Assert
        Assert.Equal(2, _stateMachine.ErrorCount);
    }

    [Fact]
    public void RecordError_TooManyErrors_AutoTransitionToError()
    {
        // Act: 记录5次错误
        for (int i = 0; i < 5; i++)
        {
            _stateMachine.RecordError(new Exception($"Error {i + 1}"));
        }

        // Assert
        Assert.Equal(DeviceStatus.Error, _stateMachine.CurrentState);
        Assert.Equal(5, _stateMachine.ErrorCount);
    }

    [Fact]
    public void ResetErrors_ClearsErrorCount()
    {
        // Arrange
        _stateMachine.RecordError(new Exception("Error 1"));
        _stateMachine.RecordError(new Exception("Error 2"));

        // Act
        _stateMachine.ResetErrors();

        // Assert
        Assert.Equal(0, _stateMachine.ErrorCount);
    }

    [Fact]
    public void CanRetry_WithinLimit_ReturnsTrue()
    {
        // Act
        var canRetry = _stateMachine.CanRetry(10, TimeSpan.FromMinutes(5));

        // Assert
        Assert.True(canRetry);
    }

    [Fact]
    public void CanRetry_ExceedsLimit_ReturnsFalse()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            _stateMachine.RecordError(new Exception($"Error {i + 1}"));
        }

        // Act
        var canRetry = _stateMachine.CanRetry(10, TimeSpan.FromMinutes(5));

        // Assert
        Assert.False(canRetry);
    }

    [Fact]
    public void StatusChanged_EventIsRaised()
    {
        // Arrange
        DeviceStatusChangedEventArgs? eventArgs = null;
        _stateMachine.StatusChanged += (sender, e) => eventArgs = e;

        // Act
        _stateMachine.TransitionTo(DeviceStatus.Connecting);

        // Assert
        Assert.NotNull(eventArgs);
        Assert.Equal("test_device_001", eventArgs.DeviceId);
        Assert.Equal(DeviceStatus.Offline, eventArgs.OldStatus);
        Assert.Equal(DeviceStatus.Connecting, eventArgs.NewStatus);
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
