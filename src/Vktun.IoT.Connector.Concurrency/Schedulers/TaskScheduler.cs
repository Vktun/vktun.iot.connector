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
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphore;
    
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _processorTask;
    private int _completedTaskCount;
    private bool _isRunning;

    public int PendingTaskCount => _taskQueue.Count;
    public int RunningTaskCount => _semaphore.CurrentCount;
    public int CompletedTaskCount => _completedTaskCount;
    public bool IsRunning => _isRunning;

    public event EventHandler<TaskCompletedEventArgs>? TaskCompleted;
    public event EventHandler<TaskFailedEventArgs>? TaskFailed;

    public TaskScheduler(IConfigurationProvider configProvider, ILogger logger)
    {
        _configProvider = configProvider;
        _logger = logger;
        
        var config = configProvider.GetConfig();
        _taskQueue = new BlockingCollection<TaskItem>(config.ThreadPool.TaskQueueCapacity);
        _taskResults = new ConcurrentDictionary<string, TaskCompletionSource<CommandResult>>();
        _semaphore = new SemaphoreSlim(config.ThreadPool.MaxWorkerThreads);
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
        
        _logger.Info("任务调度器启动");
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
            await _processorTask;
        }
        
        _cancellationTokenSource?.Dispose();
        _logger.Info("任务调度器停止");
    }

    public Task<string> SubmitTaskAsync(DeviceCommand command, CancellationToken cancellationToken = default)
    {
        return SubmitTaskAsync(command, command.Priority, cancellationToken);
    }

    public Task<string> SubmitTaskAsync(DeviceCommand command, TaskPriority priority, CancellationToken cancellationToken = default)
    {
        var taskItem = new TaskItem
        {
            TaskId = Guid.NewGuid().ToString(),
            Command = command,
            Priority = priority,
            CreateTime = DateTime.Now
        };

        var tcs = new TaskCompletionSource<CommandResult>();
        _taskResults[taskItem.TaskId] = tcs;

        if (!_taskQueue.TryAdd(taskItem))
        {
            tcs.SetException(new InvalidOperationException("任务队列已满"));
            _logger.Warning($"任务提交失败，队列已满: {taskItem.TaskId}");
        }
        else
        {
            _logger.Debug($"任务提交成功: {taskItem.TaskId}, 设备: {command.DeviceId}");
        }

        return Task.FromResult(taskItem.TaskId);
    }

    public async Task<CommandResult> GetTaskResultAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (_taskResults.TryGetValue(taskId, out var tcs))
        {
            return await tcs.Task.WaitAsync(cancellationToken);
        }
        
        return new CommandResult
        {
            CommandId = taskId,
            Success = false,
            ErrorMessage = "任务不存在"
        };
    }

    public Task<bool> CancelTaskAsync(string taskId)
    {
        if (_taskResults.TryGetValue(taskId, out var tcs))
        {
            tcs.TrySetCanceled();
            _taskResults.TryRemove(taskId, out _);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task CancelAllTasksAsync()
    {
        foreach (var kvp in _taskResults)
        {
            kvp.Value.TrySetCanceled();
        }
        _taskResults.Clear();
        return Task.CompletedTask;
    }

    private async Task ProcessTasksAsync(CancellationToken cancellationToken)
    {
        var config = _configProvider.GetConfig();
        
        foreach (var taskItem in _taskQueue.GetConsumingEnumerable(cancellationToken))
        {
            await _semaphore.WaitAsync(cancellationToken);
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await ExecuteTaskAsync(taskItem, cancellationToken);
                    
                    if (_taskResults.TryGetValue(taskItem.TaskId, out var tcs))
                    {
                        tcs.SetResult(result);
                        _taskResults.TryRemove(taskItem.TaskId, out _);
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
                    if (_taskResults.TryGetValue(taskItem.TaskId, out var tcs))
                    {
                        tcs.SetException(ex);
                        _taskResults.TryRemove(taskItem.TaskId, out _);
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
                    _semaphore.Release();
                }
            }, cancellationToken);
        }
    }

    private async Task<CommandResult> ExecuteTaskAsync(TaskItem taskItem, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            await Task.Delay(100, cancellationToken);
            
            return new CommandResult
            {
                CommandId = taskItem.TaskId,
                Success = true,
                ElapsedTime = stopwatch.Elapsed
            };
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

    private class TaskItem
    {
        public string TaskId { get; set; } = string.Empty;
        public DeviceCommand Command { get; set; } = new();
        public TaskPriority Priority { get; set; }
        public DateTime CreateTime { get; set; }
    }
}
