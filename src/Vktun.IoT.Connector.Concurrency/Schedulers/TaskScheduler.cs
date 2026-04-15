using System.Collections.Concurrent;
using System.Diagnostics;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Concurrency.Queues;

namespace Vktun.IoT.Connector.Concurrency.Schedulers;

public class TaskScheduler : ITaskScheduler
{
    private readonly PriorityAsyncQueue<PriorityTaskItem> _taskQueue;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandResult>> _taskResults;
    private readonly IConfigurationProvider _configProvider;
    private readonly IDeviceManager _deviceManager;
    private readonly IDeviceCommandExecutor _commandExecutor;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _workerSemaphore;
    private readonly int _maxRetryCount;
    private readonly int _retryBaseIntervalMs;
    private readonly int _retryMaxIntervalMs;

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
        ILogger logger,
        int maxRetryCount = 3,
        int retryBaseIntervalMs = 1000,
        int retryMaxIntervalMs = 30000)
    {
        _configProvider = configProvider;
        _deviceManager = deviceManager;
        _commandExecutor = commandExecutor;
        _logger = logger;
        _maxRetryCount = maxRetryCount;
        _retryBaseIntervalMs = retryBaseIntervalMs;
        _retryMaxIntervalMs = retryMaxIntervalMs;

        var config = configProvider.GetConfig();
        _taskQueue = new PriorityAsyncQueue<PriorityTaskItem>(config.ThreadPool.TaskQueueCapacity);
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

        if (_processorTask != null)
        {
            try
            {
                await _processorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
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
        var taskItem = new PriorityTaskItem
        {
            TaskId = Guid.NewGuid().ToString("N"),
            Command = command,
            Priority = priority,
            CreateTime = DateTime.Now
        };

        var completionSource = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _taskResults[taskItem.TaskId] = completionSource;

        if (!_taskQueue.TryEnqueue(taskItem))
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
        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            try
            {
                var taskItem = await _taskQueue.DequeueAsync(cancellationToken).ConfigureAwait(false);
                if (taskItem == null)
                {
                    continue;
                }

                await _workerSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                _ = ProcessSingleTaskAsync(taskItem, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in task processing loop: {ex.Message}", ex);
            }
        }
    }

    private async Task ProcessSingleTaskAsync(PriorityTaskItem taskItem, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _runningTaskCount);
        try
        {
            var result = await ExecuteWithRetryAsync(taskItem, cancellationToken).ConfigureAwait(false);
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

    private async Task<CommandResult> ExecuteWithRetryAsync(PriorityTaskItem taskItem, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        CommandResult? lastResult = null;
        Exception? lastException = null;
        taskItem.Command.CommandId = taskItem.TaskId;

        for (int attempt = 0; attempt <= _maxRetryCount; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new CommandResult
                {
                    CommandId = taskItem.TaskId,
                    Success = false,
                    ErrorMessage = "Task was cancelled.",
                    ElapsedTime = stopwatch.Elapsed
                };
            }

            try
            {
                if (attempt > 0)
                {
                    var delay = CalculateRetryDelay(attempt);
                    _logger.Info($"Retrying task. taskId={taskItem.TaskId} deviceId={taskItem.Command.DeviceId} attempt={attempt} maxAttempts={_maxRetryCount} delayMs={delay}");
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                var device = await ResolveDeviceAsync(taskItem.Command.DeviceId, cancellationToken).ConfigureAwait(false);
                _logger.Debug($"Executing task. taskId={taskItem.TaskId} deviceId={device.DeviceId} protocolId={device.ProtocolId} commandName={taskItem.Command.CommandName}");
                var result = await _commandExecutor.ExecuteAsync(taskItem.Command, device, cancellationToken).ConfigureAwait(false);
                result.CommandId = taskItem.TaskId;
                result.ElapsedTime = stopwatch.Elapsed;

                if (result.Success)
                {
                    _logger.Debug($"Task completed. taskId={taskItem.TaskId} deviceId={device.DeviceId} elapsedMs={stopwatch.ElapsedMilliseconds}");
                    return result;
                }

                lastResult = result;
                lastException = null;

                if (!IsRetryableFailure(result))
                {
                    _logger.Warning($"Task failed without retry. taskId={taskItem.TaskId} deviceId={device.DeviceId} elapsedMs={stopwatch.ElapsedMilliseconds} error={result.ErrorMessage}");
                    return result;
                }
            }
            catch (OperationCanceledException)
            {
                return new CommandResult
                {
                    CommandId = taskItem.TaskId,
                    Success = false,
                    ErrorMessage = "Task was cancelled.",
                    ElapsedTime = stopwatch.Elapsed
                };
            }
            catch (Exception ex)
            {
                lastException = ex;
                lastResult = null;

                if (!IsRetryableException(ex) || attempt >= _maxRetryCount)
                {
                    break;
                }
            }
        }

        if (lastResult != null)
        {
            lastResult.ErrorMessage = $"Task failed after {_maxRetryCount + 1} attempts. Last error: {lastResult.ErrorMessage}";
            return lastResult;
        }

        return new CommandResult
        {
            CommandId = taskItem.TaskId,
            Success = false,
            ErrorMessage = $"Task failed after {_maxRetryCount + 1} attempts: {lastException?.Message ?? "Unknown error"}",
            ElapsedTime = stopwatch.Elapsed
        };
    }

    private int CalculateRetryDelay(int attempt)
    {
        var delay = _retryBaseIntervalMs * Math.Pow(2, Math.Min(attempt - 1, 8));
        delay = Math.Min(delay, _retryMaxIntervalMs);
        var jitter = delay * 0.25 * (Random.Shared.NextDouble() * 2 - 1);
        return (int)Math.Max(100, delay + jitter);
    }

    private static bool IsRetryableFailure(CommandResult result)
    {
        if (result.Success)
        {
            return false;
        }

        if (result.ErrorMessage != null &&
            (result.ErrorMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
             result.ErrorMessage.Contains("not connected", StringComparison.OrdinalIgnoreCase) ||
             result.ErrorMessage.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static bool IsRetryableException(Exception ex)
    {
        return ex is TimeoutException ||
               ex is System.Net.Sockets.SocketException ||
               ex is System.IO.IOException ||
               ex is InvalidOperationException;
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

    private sealed class PriorityTaskItem : IComparable<PriorityTaskItem>
    {
        public string TaskId { get; set; } = string.Empty;
        public DeviceCommand Command { get; set; } = new();
        public TaskPriority Priority { get; set; }
        public DateTime CreateTime { get; set; }

        public int CompareTo(PriorityTaskItem? other)
        {
            if (other == null)
            {
                return -1;
            }

            var priorityCompare = other.Priority.CompareTo(Priority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            return CreateTime.CompareTo(other.CreateTime);
        }
    }
}
