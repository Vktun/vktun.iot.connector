using System.Net.Sockets;
using System.IO;

namespace Vktun.IoT.Connector.Core.Interfaces
{
    public interface IReconnectPolicy
    {
        int? MaxAttempts { get; }

        TimeSpan? GetNextDelay(int attempt, Exception? lastException);

        bool ShouldReconnect(Exception exception);

        void Reset();
    }

    public class ReconnectPolicyConfig
    {
        public int MaxAttempts { get; set; } = 100;
        public int BaseIntervalMs { get; set; } = 1000;
        public int MaxIntervalMs { get; set; } = 30000;
        public double BackoffFactor { get; set; } = 2.0;
        public bool EnableJitter { get; set; } = true;
        public List<Type> RetryableExceptions { get; set; } = new()
        {
            typeof(TimeoutException),
            typeof(SocketException),
            typeof(IOException)
        };
    }
}
