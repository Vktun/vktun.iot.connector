using Vktun.IoT.Connector.Business.Services;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Protocol.Factories;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Services;

public class ModbusClientTests
{
    [Fact]
    public async Task ConnectAsync_ModbusTcp_ShouldBuildDeviceAndProtocolConfig()
    {
        var executor = new FakeCommandExecutor();
        var client = CreateClient(executor);

        var result = await client.ConnectAsync(new ModbusConnectionOptions
        {
            ConnectionId = "tcp-1",
            ProtocolType = ProtocolType.ModbusTcp,
            IpAddress = "127.0.0.1",
            Port = 1502,
            SlaveId = 17
        });

        Assert.True(result.Success);
        Assert.NotNull(executor.ConnectedDevice);
        Assert.Equal("tcp-1", executor.ConnectedDevice.DeviceId);
        Assert.Equal(CommunicationType.Tcp, executor.ConnectedDevice.CommunicationType);
        Assert.Equal(ProtocolType.ModbusTcp, executor.ConnectedDevice.ProtocolType);
        Assert.True(executor.ConnectedDevice.ExtendedProperties.TryGetValue("ProtocolConfig", out var configObject));
        var protocolConfig = Assert.IsType<ProtocolConfig>(configObject);
        var modbusConfig = protocolConfig.GetDefinition<ModbusConfig>();
        Assert.NotNull(modbusConfig);
        Assert.Equal(17, modbusConfig.SlaveId);
    }

    [Fact]
    public async Task ReadAsync_HoldingRegister_ShouldReturnTypedValueAndRawFrames()
    {
        var executor = new FakeCommandExecutor
        {
            ExecuteResult = new CommandResult
            {
                Success = true,
                RequestData = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x11, 0x03, 0x00, 0x00, 0x00, 0x01 },
                ResponseData = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x05, 0x11, 0x03, 0x02, 0x12, 0x34 }
            }
        };
        var client = CreateClient(executor);
        await client.ConnectAsync(new ModbusConnectionOptions
        {
            ConnectionId = "tcp-1",
            ProtocolType = ProtocolType.ModbusTcp,
            IpAddress = "127.0.0.1",
            Port = 1502,
            SlaveId = 17
        });

        var result = await client.ReadAsync(new ModbusReadRequest
        {
            ConnectionId = "tcp-1",
            RegisterType = ModbusRegisterType.HoldingRegister,
            Address = 0,
            Quantity = 1,
            DataType = DataType.UInt16
        });

        Assert.True(result.Success);
        Assert.Equal((ushort)0x1234, Assert.IsType<ushort>(result.Value));
        Assert.Equal("ReadHoldingRegisters", executor.ExecutedCommand?.CommandName);
        Assert.Equal(executor.ExecuteResult.RequestData, result.RequestFrame);
        Assert.Equal(executor.ExecuteResult.ResponseData, result.ResponseFrame);
    }

    [Fact]
    public async Task WriteAsync_DiscreteInput_ShouldReturnFailureWithoutExecutorCall()
    {
        var executor = new FakeCommandExecutor();
        var client = CreateClient(executor);
        await client.ConnectAsync(new ModbusConnectionOptions
        {
            ConnectionId = "tcp-1",
            ProtocolType = ProtocolType.ModbusTcp,
            IpAddress = "127.0.0.1",
            Port = 1502,
            SlaveId = 17
        });

        var result = await client.WriteAsync(new ModbusWriteRequest
        {
            ConnectionId = "tcp-1",
            RegisterType = ModbusRegisterType.DiscreteInput,
            Address = 0,
            Value = true,
            DataType = DataType.Bool
        });

        Assert.False(result.Success);
        Assert.Contains("read-only", result.ErrorMessage);
        Assert.Null(executor.ExecutedCommand);
    }

    [Fact]
    public async Task PollAsync_CancelledToken_ShouldStopEnumeration()
    {
        var executor = new FakeCommandExecutor
        {
            ExecuteResult = new CommandResult
            {
                Success = true,
                ResponseData = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x05, 0x11, 0x03, 0x02, 0x00, 0x01 }
            }
        };
        var client = CreateClient(executor);
        await client.ConnectAsync(new ModbusConnectionOptions
        {
            ConnectionId = "tcp-1",
            ProtocolType = ProtocolType.ModbusTcp,
            IpAddress = "127.0.0.1",
            Port = 1502,
            SlaveId = 17
        });

        using var cts = new CancellationTokenSource();
        var results = new List<ModbusReadResult>();

        await foreach (var item in client.PollAsync(new ModbusPollRequest
        {
            ReadRequest = new ModbusReadRequest
            {
                ConnectionId = "tcp-1",
                RegisterType = ModbusRegisterType.HoldingRegister,
                Address = 0,
                Quantity = 1,
                DataType = DataType.UInt16
            },
            IntervalMs = 1
        }, cts.Token))
        {
            results.Add(item);
            cts.Cancel();
        }

        Assert.Single(results);
    }

    private static ModbusClient CreateClient(FakeCommandExecutor executor)
    {
        var logger = new TestLogger();
        return new ModbusClient(executor, new ProtocolParserFactory(logger), logger);
    }

    private sealed class FakeCommandExecutor : IDeviceCommandExecutor
    {
        public DeviceInfo? ConnectedDevice { get; private set; }
        public DeviceCommand? ExecutedCommand { get; private set; }
        public CommandResult ExecuteResult { get; set; } = new() { Success = true };

        public event EventHandler<DataReceivedEventArgs>? DataReceived;

        public Task<bool> ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default)
        {
            ConnectedDevice = device;
            return Task.FromResult(true);
        }

        public Task DisconnectAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<CommandResult> ExecuteAsync(DeviceCommand command, DeviceInfo device, CancellationToken cancellationToken = default)
        {
            ExecutedCommand = command;
            ExecuteResult.CommandId = command.CommandId;
            return Task.FromResult(ExecuteResult);
        }

        public Task<ProtocolConfig?> GetProtocolConfigAsync(DeviceInfo device, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ProtocolConfig?>(null);
        }
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
