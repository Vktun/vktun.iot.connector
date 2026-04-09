using System.Collections.Concurrent;
using System.Diagnostics;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Concurrency.Schedulers;

public class TaskScheduler : ITaskScheduler
{
    private readonly BlockingCollection<TaskItem> _taskQueue;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandResult>> _taskResults;
    private readonly IConfigurationProvider _configProvider;
    private readonly IDeviceCommandExecutor _commandExecutor;
    private readonly IDeviceManager _deviceManager;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _workerSemaphore;

    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _processorTask;
    private int _runningTaskCount;
    private int _completedTaskCount;
    private bool _isRunning;

    public int PendingTaskCount => _taskQueue.Count;
    public int RunningTaskCount => _runningTaskCount;
    public int CompletedTaskCount => _completedTaskCount;
    public bool IsRunning => _isRunning;

    public event EventHandler<TaskCompletedEventArgs>? TaskCompleted;
    public event EventHandler<TaskFailedEventArgs>? TaskFailed;

    public TaskScheduler(
        IConfigurationProvider configProvider,
        IDeviceManager deviceManager,
        IDeviceCommandExecutor commandExecutor,
        ILogger logger)
    {
        _configProvider = configProvider;
        _deviceManager = deviceManager;
        _commandExecutor = commandExecutor;
        _logger = logger;

        var config = configProvider.GetConfig();
        _taskQueue = new BlockingCollection<TaskItem>(config.ThreadPool.TaskQueueCapacity);
        _taskResults = new ConcurrentDictionary<string, TaskCompletionSource<CommandResult>>();
        _workerSemaphore = new SemaphoreSlim(config.ThreadPool.MaxWorkerThreads);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return Task.CompletedTask;
        }

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isRunning = true;
        _processorTask = ProcessTasksAsync(_cancellationTokenSource.Token);
        _logger.Info("Task scheduler started.");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _cancellationTokenSource?.Cancel();
        _taskQueue.CompleteAdding();

        if (_processorTask != null)
        {
            await _processorTask.ConfigureAwait(false);
        }

        _cancellationTokenSource?.Dispose();
        _logger.Info("Task scheduler stopped.");
    }

    public Task<string> SubmitTaskAsync(DeviceCommand command, CancellationToken cancellationToken = default)
    {
        return SubmitTaskAsync(command, command.Priority, cancellationToken);
    }

    public Task<string> SubmitTaskAsync(DeviceCommand command, TaskPriority priority, CancellationToken cancellationToken = default)
    {
        var taskItem = new TaskItem
        {
            TaskId = Guid.NewGuid().ToString("N"),
            Command = command,
            Priority = priority,
            CreateTime = DateTime.Now
        };

        var completionSource = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _taskResults[taskItem.TaskId] = completionSource;

        if (!_taskQueue.TryAdd(taskItem, Timeout.Infinite, cancellationToken))
        {
            _taskResults.TryRemove(taskItem.TaskId, out _);
            completionSource.TrySetResult(new CommandResult
            {
                CommandId = taskItem.TaskId,
                Success = false,
                ErrorMessage = "Task queue is full."
            });
        }

        return Task.FromResult(taskItem.TaskId);
    }

    public async Task<CommandResult> GetTaskResultAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (_taskResults.TryGetValue(taskId, out var completionSource))
        {
            return await completionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return new CommandResult
        {
            CommandId = taskId,
            Success = false,
            ErrorMessage = "Task result was not found."
        };
    }

    public Task<bool> CancelTaskAsync(string taskId)
    {
        if (_taskResults.TryRemove(taskId, out var completionSource))
        {
            completionSource.TrySetCanceled();
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task CancelAllTasksAsync()
    {
        foreach (var taskResult in _taskResults.Values)
        {
            taskResult.TrySetCanceled();
        }

        _taskResults.Clear();
        return Task.CompletedTask;
    }

    private async Task ProcessTasksAsync(CancellationToken cancellationToken)
    {
        foreach (var taskItem in _taskQueue.GetConsumingEnumerable(cancellationToken))
        {
            await _workerSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            _ = ProcessSingleTaskAsync(taskItem, cancellationToken);
        }
    }

    private async Task ProcessSingleTaskAsync(TaskItem taskItem, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _runningTaskCount);
        try
        {
            var result = await ExecuteTaskAsync(taskItem, cancellationToken).ConfigureAwait(false);
            if (_taskResults.TryRemove(taskItem.TaskId, out var completionSource))
            {
                completionSource.TrySetResult(result);
            }

            Interlocked.Increment(ref _completedTaskCount);
            TaskCompleted?.Invoke(this, new TaskCompletedEventArgs
            {
                TaskId = taskItem.TaskId,
                DeviceId = taskItem.Command.DeviceId,
                Result = result,
                Timestamp = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            if (_taskResults.TryRemove(taskItem.TaskId, out var completionSource))
            {
                completionSource.TrySetResult(new CommandResult
                {
                    CommandId = taskItem.TaskId,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }

            TaskFailed?.Invoke(this, new TaskFailedEventArgs
            {
                TaskId = taskItem.TaskId,
                DeviceId = taskItem.Command.DeviceId,
                ErrorMessage = ex.Message,
                Exception = ex,
                Timestamp = DateTime.Now
            });
        }
        finally
        {
            Interlocked.Decrement(ref _runningTaskCount);
            _workerSemaphore.Release();
        }
    }

    private async Task<CommandResult> ExecuteTaskAsync(TaskItem taskItem, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var device = await ResolveDeviceAsync(taskItem.Command.DeviceId, cancellationToken).ConfigureAwait(false);
            var result = await _commandExecutor.ExecuteAsync(taskItem.Command, device, cancellationToken).ConfigureAwait(false);
            result.CommandId = taskItem.TaskId;
            result.ElapsedTime = stopwatch.Elapsed;
            return result;
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                CommandId = taskItem.TaskId,
                Success = false,
                ErrorMessage = ex.Message,
                ElapsedTime = stopwatch.Elapsed
            };
        }
    }

    private async Task<DeviceInfo> ResolveDeviceAsync(string deviceId, CancellationToken cancellationToken)
    {
        var device = await _deviceManager.GetDeviceAsync(deviceId).ConfigureAwait(false);
        if (device == null)
        {
            throw new InvalidOperationException($"Device {deviceId} was not found.");
        }

        return device;
    }

    private sealed class TaskItem
    {
        public string TaskId { get; set; } = string.Empty;
        public DeviceCommand Command { get; set; } = new();
        public TaskPriority Priority { get; set; }
        public DateTime CreateTime { get; set; }
    }
}
