using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces
{
    public interface ITaskScheduler
    {
        int PendingTaskCount { get; }
        int RunningTaskCount { get; }
        int CompletedTaskCount { get; }
        bool IsRunning { get; }
    
        Task StartAsync(CancellationToken cancellationToken = default);
        Task StopAsync();
        Task<string> SubmitTaskAsync(DeviceCommand command, CancellationToken cancellationToken = default);
        Task<string> SubmitTaskAsync(DeviceCommand command, TaskPriority priority, CancellationToken cancellationToken = default);
        Task<CommandResult> GetTaskResultAsync(string taskId, CancellationToken cancellationToken = default);
        Task<bool> CancelTaskAsync(string taskId);
        Task CancelAllTasksAsync();
    
        event EventHandler<TaskCompletedEventArgs>? TaskCompleted;
        event EventHandler<TaskFailedEventArgs>? TaskFailed;
    }

    public class TaskCompletedEventArgs : EventArgs
    {
        public string TaskId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public CommandResult Result { get; set; } = new CommandResult();
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class TaskFailedEventArgs : EventArgs
    {
        public string TaskId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
