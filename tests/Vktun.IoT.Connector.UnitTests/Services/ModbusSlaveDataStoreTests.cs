using Vktun.IoT.Connector.Core.Models;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Services;

public class ModbusSlaveDataStoreTests
{
    [Fact]
    public void ReadCoils_ShouldPackAndReturnConfiguredValues()
    {
        var store = new ModbusSlaveDataStore(coilCount: 16, discreteInputCount: 8, inputRegisterCount: 8, holdingRegisterCount: 8);
        store.WriteCoils(0, new[] { true, false, true, true, false, false, false, true, false, true });

        var values = store.ReadCoils(0, 10);

        Assert.Equal(new[] { true, false, true, true, false, false, false, true, false, true }, values);
    }

    [Fact]
    public void ReadHoldingRegisters_OutOfRange_ShouldThrow()
    {
        var store = new ModbusSlaveDataStore(coilCount: 8, discreteInputCount: 8, inputRegisterCount: 8, holdingRegisterCount: 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.ReadHoldingRegisters(1, 2));
    }

    [Fact]
    public void WriteHoldingRegisters_ShouldBeThreadSafeForConcurrentWrites()
    {
        var store = new ModbusSlaveDataStore(coilCount: 8, discreteInputCount: 8, inputRegisterCount: 8, holdingRegisterCount: 32);

        Parallel.For(0, 32, i => store.SetHoldingRegister((ushort)i, (ushort)(i + 100)));

        Assert.Equal(100, store.GetHoldingRegister(0));
        Assert.Equal(131, store.GetHoldingRegister(31));
    }

    [Fact]
    public void WriteInputRegisters_ShouldSeedReadOnlyInputArea()
    {
        var store = new ModbusSlaveDataStore(coilCount: 8, discreteInputCount: 8, inputRegisterCount: 2, holdingRegisterCount: 8);

        store.WriteInputRegisters(0, new ushort[] { 0x1234, 0x5678 });

        Assert.Equal(new ushort[] { 0x1234, 0x5678 }, store.ReadInputRegisters(0, 2));
    }
}
