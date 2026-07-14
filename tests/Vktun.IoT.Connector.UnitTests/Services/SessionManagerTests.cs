using Vktun.IoT.Connector.Business.Managers;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Services;

public class SessionManagerTests
{
    [Fact]
    public async Task CreateSessionAsync_ForExistingDevice_ShouldReplacePreviousSessionWithoutLeakingIt()
    {
        var manager = new SessionManager(new TestLogger());
        var device = new DeviceInfo { DeviceId = "device-1" };

        var first = await manager.CreateSessionAsync(device);
        var second = await manager.CreateSessionAsync(device);

        Assert.Equal(1, manager.ActiveSessionCount);
        Assert.Equal(1, manager.TotalSessionCount);
        Assert.Null(await manager.GetSessionByIdAsync(first.SessionId));
        Assert.Equal(second.SessionId, (await manager.GetSessionAsync(device.DeviceId))?.SessionId);
    }

    private sealed class TestLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Fatal(string message, Exception? exception = null) { }
        public void Log(LogLevel level, string message, Exception? exception = null) { }
    }
}
