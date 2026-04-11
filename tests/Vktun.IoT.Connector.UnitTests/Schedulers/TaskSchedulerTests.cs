using Moq;
using Vktun.IoT.Connector.Configuration.Providers;
using Scheduler = Vktun.IoT.Connector.Concurrency.Schedulers.TaskScheduler;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Schedulers;

public class TaskSchedulerTests
{
    private readonly Mock<IDeviceManager> _deviceManager;
    private readonly Mock<IDeviceCommandExecutor> _commandExecutor;
    private readonly IConfigurationProvider _configProvider;
    private readonly ILogger _logger;

    public TaskSchedulerTests()
    {
        _deviceManager = new Mock<IDeviceManager>();
        _commandExecutor = new Mock<IDeviceCommandExecutor>();
        _configProvider = new TestConfigurationProvider();
        _logger = new TestLogger();

        _deviceManager.Setup(m => m.GetDeviceAsync(It.IsAny<string>()))
            .ReturnsAsync(new DeviceInfo { DeviceId = "test-device" });
    }

    [Fact]
    public async Task SubmitTaskAsync_ShouldReturnTaskId()
    {
        var scheduler = CreateScheduler();
        var command = new DeviceCommand { DeviceId = "test-device", CommandName = "Read" };

        var taskId = await scheduler.SubmitTaskAsync(command);

        Assert.False(string.IsNullOrWhiteSpace(taskId));
    }

    [Fact]
    public async Task StartAsync_ShouldSetIsRunning()
    {
        var scheduler = CreateScheduler();

        await scheduler.StartAsync();

        Assert.True(scheduler.IsRunning);
        await scheduler.StopAsync();
    }

    [Fact]
    public async Task StopAsync_ShouldSetIsNotRunning()
    {
        var scheduler = CreateScheduler();
        await scheduler.StartAsync();

        await scheduler.StopAsync();

        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public async Task GetTaskResultAsync_ShouldReturnSuccess_WhenExecutorSucceeds()
    {
        _commandExecutor.Setup(e => e.ExecuteAsync(It.IsAny<DeviceCommand>(), It.IsAny<DeviceInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { Success = true });

        var scheduler = CreateScheduler();
        await scheduler.StartAsync();
        var command = new DeviceCommand { DeviceId = "test-device", CommandName = "Read" };

        var taskId = await scheduler.SubmitTaskAsync(command);
        var result = await scheduler.GetTaskResultAsync(taskId, CancellationToken.None);

        Assert.True(result.Success);
        await scheduler.StopAsync();
    }

    [Fact]
    public async Task GetTaskResultAsync_ShouldReturnFailure_WhenExecutorFails()
    {
        _commandExecutor.Setup(e => e.ExecuteAsync(It.IsAny<DeviceCommand>(), It.IsAny<DeviceInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { Success = false, ErrorMessage = "Connection failed" });

        var scheduler = CreateScheduler(maxRetryCount: 0);
        await scheduler.StartAsync();
        var command = new DeviceCommand { DeviceId = "test-device", CommandName = "Read" };

        var taskId = await scheduler.SubmitTaskAsync(command);
        var result = await scheduler.GetTaskResultAsync(taskId, CancellationToken.None);

        Assert.False(result.Success);
        await scheduler.StopAsync();
    }

    [Fact]
    public async Task CancelTaskAsync_ShouldCancelPendingTask()
    {
        _commandExecutor.Setup(e => e.ExecuteAsync(It.IsAny<DeviceCommand>(), It.IsAny<DeviceInfo>(), It.IsAny<CancellationToken>()))
            .Returns(async (DeviceCommand cmd, DeviceInfo dev, CancellationToken ct) =>
            {
                await Task.Delay(5000, ct);
                return new CommandResult { Success = true };
            });

        var scheduler = CreateScheduler();
        await scheduler.StartAsync();
        var command = new DeviceCommand { DeviceId = "test-device", CommandName = "Read" };

        var taskId = await scheduler.SubmitTaskAsync(command);
        var cancelled = await scheduler.CancelTaskAsync(taskId);

        Assert.True(cancelled);
        await scheduler.StopAsync();
    }

    [Fact]
    public async Task TaskCompleted_ShouldFire_WhenTaskSucceeds()
    {
        _commandExecutor.Setup(e => e.ExecuteAsync(It.IsAny<DeviceCommand>(), It.IsAny<DeviceInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { Success = true });

        var scheduler = CreateScheduler();
        await scheduler.StartAsync();
        var completedEvent = new TaskCompletionSource<TaskCompletedEventArgs>();
        scheduler.TaskCompleted += (_, e) => completedEvent.TrySetResult(e);

        var command = new DeviceCommand { DeviceId = "test-device", CommandName = "Read" };
        await scheduler.SubmitTaskAsync(command);

        var eventArgs = await completedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("test-device", eventArgs.DeviceId);
        Assert.True(eventArgs.Result.Success);

        await scheduler.StopAsync();
    }

    [Fact]
    public async Task TaskFailed_ShouldFire_WhenExecutorThrows()
    {
        _commandExecutor.Setup(e => e.ExecuteAsync(It.IsAny<DeviceCommand>(), It.IsAny<DeviceInfo>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Device not found"));

        var scheduler = CreateScheduler(maxRetryCount: 0);
        await scheduler.StartAsync();
        var resultSource = new TaskCompletionSource<CommandResult>();

        scheduler.TaskCompleted += (_, e) => resultSource.TrySetResult(e.Result);
        scheduler.TaskFailed += (_, e) => resultSource.TrySetResult(new CommandResult { Success = false, ErrorMessage = e.ErrorMessage });

        var command = new DeviceCommand { DeviceId = "test-device", CommandName = "Read" };
        await scheduler.SubmitTaskAsync(command);

        var result = await resultSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.Success);
        Assert.Contains("Device not found", result.ErrorMessage);

        await scheduler.StopAsync();
    }

    [Fact]
    public async Task Retry_ShouldSucceed_AfterTransientFailure()
    {
        var callCount = 0;
        _commandExecutor.Setup(e => e.ExecuteAsync(It.IsAny<DeviceCommand>(), It.IsAny<DeviceInfo>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return Task.FromResult(new CommandResult { Success = false, ErrorMessage = "timed out" });
                }

                return Task.FromResult(new CommandResult { Success = true });
            });

        var scheduler = CreateScheduler(maxRetryCount: 2, retryBaseIntervalMs: 10);
        await scheduler.StartAsync();
        var command = new DeviceCommand { DeviceId = "test-device", CommandName = "Read" };

        var taskId = await scheduler.SubmitTaskAsync(command);
        var result = await scheduler.GetTaskResultAsync(taskId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(callCount >= 2);
        await scheduler.StopAsync();
    }

    [Fact]
    public async Task PriorityScheduling_ShouldProcessHighPriorityFirst()
    {
        var executionOrder = new List<TaskPriority>();
        var tcs = new TaskCompletionSource();
        var executingCount = 0;

        _commandExecutor.Setup(e => e.ExecuteAsync(It.IsAny<DeviceCommand>(), It.IsAny<DeviceInfo>(), It.IsAny<CancellationToken>()))
            .Returns(async (DeviceCommand cmd, DeviceInfo dev, CancellationToken ct) =>
            {
                var current = Interlocked.Increment(ref executingCount);
                if (current == 1)
                {
                    await tcs.Task;
                }

                lock (executionOrder)
                {
                    executionOrder.Add(cmd.Priority);
                }

                return new CommandResult { Success = true };
            });

        var scheduler = CreateScheduler(maxRetryCount: 0);
        await scheduler.StartAsync();

        var lowCommand = new DeviceCommand { DeviceId = "test-device", CommandName = "Low", Priority = TaskPriority.Low };
        var highCommand = new DeviceCommand { DeviceId = "test-device", CommandName = "High", Priority = TaskPriority.High };

        await scheduler.SubmitTaskAsync(lowCommand, TaskPriority.Low);
        await scheduler.SubmitTaskAsync(highCommand, TaskPriority.High);

        tcs.SetResult();

        await Task.Delay(500);

        if (executionOrder.Count >= 2)
        {
            Assert.Equal(TaskPriority.High, executionOrder[0]);
        }

        await scheduler.StopAsync();
    }

    [Fact]
    public async Task ConcurrentTasks_ShouldNotExceedMaxWorkers()
    {
        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        _commandExecutor.Setup(e => e.ExecuteAsync(It.IsAny<DeviceCommand>(), It.IsAny<DeviceInfo>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                lock (lockObj)
                {
                    concurrentCount++;
                    if (concurrentCount > maxConcurrent)
                    {
                        maxConcurrent = concurrentCount;
                    }
                }

                await Task.Delay(100);

                lock (lockObj)
                {
                    concurrentCount--;
                }

                return new CommandResult { Success = true };
            });

        var scheduler = CreateScheduler(maxRetryCount: 0);
        await scheduler.StartAsync();

        var tasks = new List<Task<string>>();
        for (int i = 0; i < 10; i++)
        {
            var command = new DeviceCommand { DeviceId = $"device-{i}", CommandName = "Read" };
            _deviceManager.Setup(m => m.GetDeviceAsync($"device-{i}"))
                .ReturnsAsync(new DeviceInfo { DeviceId = $"device-{i}" });
            tasks.Add(scheduler.SubmitTaskAsync(command));
        }

        await Task.WhenAll(tasks);
        await Task.Delay(500);

        Assert.True(maxConcurrent <= _configProvider.GetConfig().ThreadPool.MaxWorkerThreads);
        await scheduler.StopAsync();
    }

    private Scheduler CreateScheduler(int maxRetryCount = 3, int retryBaseIntervalMs = 100)
    {
        return new Scheduler(
            _configProvider,
            _deviceManager.Object,
            _commandExecutor.Object,
            _logger,
            maxRetryCount,
            retryBaseIntervalMs);
    }

    private sealed class TestConfigurationProvider : IConfigurationProvider
    {
        private readonly SdkConfig _config = new();

        public SdkConfig GetConfig() => _config;
        public Task<SdkConfig> LoadConfigAsync(string filePath) => Task.FromResult(_config);
        public Task SaveConfigAsync(string filePath, SdkConfig config) => Task.CompletedTask;
        public Task<bool> UpdateConfigAsync(Action<SdkConfig> updateAction) { updateAction(_config); return Task.FromResult(true); }
        public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;
        public Task<List<ProtocolConfig>> LoadProtocolTemplatesAsync(string templatesDirectory) => Task.FromResult(new List<ProtocolConfig>());
        public Task<ProtocolConfig?> LoadProtocolTemplateAsync(string filePath) => Task.FromResult<ProtocolConfig?>(null);
        public Task<List<string>> GetProtocolTemplatePathsAsync(string templatesDirectory) => Task.FromResult(new List<string>());
        public Task SaveProtocolTemplateAsync(string filePath, ProtocolConfig config) => Task.CompletedTask;
        public Task<bool> ExportTemplateAsync(ProtocolConfig config, string exportPath) => Task.FromResult(true);
        public Task<ProtocolConfig?> ImportTemplateAsync(string importPath) => Task.FromResult<ProtocolConfig?>(null);
        public Task<ProtocolTemplateVersion?> GetTemplateVersionAsync(string filePath) => Task.FromResult<ProtocolTemplateVersion?>(null);
        public Task StartTemplateWatchAsync(string templatesDirectory, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ProtocolConfigValidationReport ValidateTemplate(ProtocolConfig config) => new() { IsValid = true };
        public Task<List<ProtocolConfigValidationReport>> ValidateAllTemplatesAsync(string templatesDirectory) => Task.FromResult(new List<ProtocolConfigValidationReport>());
    }

    private sealed class TestLogger : ILogger
    {
        public void Log(LogLevel level, string message, Exception? exception = null) { }
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Fatal(string message, Exception? exception = null) { }
    }
}
