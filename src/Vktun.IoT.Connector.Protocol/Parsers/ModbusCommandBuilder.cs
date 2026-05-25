using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Protocol.Parsers;

internal static class ModbusCommandBuilder
{
    public static ModbusFunctionCode GetFunctionCode(string commandName)
    {
        return commandName switch
        {
            "ReadCoils" => ModbusFunctionCode.ReadCoils,
            "ReadDiscreteInputs" => ModbusFunctionCode.ReadDiscreteInputs,
            "ReadHoldingRegisters" => ModbusFunctionCode.ReadHoldingRegisters,
            "ReadInputRegisters" => ModbusFunctionCode.ReadInputRegisters,
            "WriteSingleCoil" => ModbusFunctionCode.WriteSingleCoil,
            "WriteSingleRegister" => ModbusFunctionCode.WriteSingleRegister,
            "WriteMultipleCoils" => ModbusFunctionCode.WriteMultipleCoils,
            "WriteMultipleRegisters" => ModbusFunctionCode.WriteMultipleRegisters,
            _ => throw new NotSupportedException($"Unsupported Modbus command: {commandName}")
        };
    }

    public static byte[] BuildPdu(DeviceCommand command)
    {
        var functionCode = GetFunctionCode(command.CommandName);
        var address = GetAddress(command.Parameters);

        return functionCode switch
        {
            ModbusFunctionCode.ReadCoils
                or ModbusFunctionCode.ReadDiscreteInputs
                or ModbusFunctionCode.ReadHoldingRegisters
                or ModbusFunctionCode.ReadInputRegisters => BuildReadPdu(functionCode, address, GetQuantity(command.Parameters)),
            ModbusFunctionCode.WriteSingleCoil => BuildWriteSingleCoilPdu(address, GetBool(command.Parameters, "Value")),
            ModbusFunctionCode.WriteSingleRegister => BuildWriteSingleRegisterPdu(address, GetUInt16(command.Parameters, "Value")),
            ModbusFunctionCode.WriteMultipleCoils => BuildWriteMultipleCoilsPdu(address, GetBoolValues(command.Parameters)),
            ModbusFunctionCode.WriteMultipleRegisters => BuildWriteMultipleRegistersPdu(address, GetRegisterValues(command.Parameters)),
            _ => throw new NotSupportedException($"Unsupported Modbus function: {functionCode}")
        };
    }

    private static byte[] BuildReadPdu(ModbusFunctionCode functionCode, ushort address, ushort quantity)
    {
        ValidateQuantity(quantity, 1, functionCode is ModbusFunctionCode.ReadCoils or ModbusFunctionCode.ReadDiscreteInputs ? (ushort)2000 : (ushort)125);

        return new[]
        {
            (byte)functionCode,
            (byte)(address >> 8),
            (byte)(address & 0xFF),
            (byte)(quantity >> 8),
            (byte)(quantity & 0xFF)
        };
    }

    private static byte[] BuildWriteSingleCoilPdu(ushort address, bool value)
    {
        return new[]
        {
            (byte)ModbusFunctionCode.WriteSingleCoil,
            (byte)(address >> 8),
            (byte)(address & 0xFF),
            value ? (byte)0xFF : (byte)0x00,
            (byte)0x00
        };
    }

    private static byte[] BuildWriteSingleRegisterPdu(ushort address, ushort value)
    {
        return new[]
        {
            (byte)ModbusFunctionCode.WriteSingleRegister,
            (byte)(address >> 8),
            (byte)(address & 0xFF),
            (byte)(value >> 8),
            (byte)(value & 0xFF)
        };
    }

    private static byte[] BuildWriteMultipleCoilsPdu(ushort address, bool[] values)
    {
        ValidateQuantity((ushort)values.Length, 1, 1968);

        var byteCount = (byte)((values.Length + 7) / 8);
        var pdu = new byte[6 + byteCount];
        pdu[0] = (byte)ModbusFunctionCode.WriteMultipleCoils;
        pdu[1] = (byte)(address >> 8);
        pdu[2] = (byte)(address & 0xFF);
        pdu[3] = (byte)(values.Length >> 8);
        pdu[4] = (byte)(values.Length & 0xFF);
        pdu[5] = byteCount;

        for (var i = 0; i < values.Length; i++)
        {
            if (values[i])
            {
                pdu[6 + i / 8] |= (byte)(1 << (i % 8));
            }
        }

        return pdu;
    }

    private static byte[] BuildWriteMultipleRegistersPdu(ushort address, ushort[] values)
    {
        ValidateQuantity((ushort)values.Length, 1, 123);

        var byteCount = checked((byte)(values.Length * 2));
        var pdu = new byte[6 + byteCount];
        pdu[0] = (byte)ModbusFunctionCode.WriteMultipleRegisters;
        pdu[1] = (byte)(address >> 8);
        pdu[2] = (byte)(address & 0xFF);
        pdu[3] = (byte)(values.Length >> 8);
        pdu[4] = (byte)(values.Length & 0xFF);
        pdu[5] = byteCount;

        for (var i = 0; i < values.Length; i++)
        {
            pdu[6 + i * 2] = (byte)(values[i] >> 8);
            pdu[6 + i * 2 + 1] = (byte)(values[i] & 0xFF);
        }

        return pdu;
    }

    private static ushort GetAddress(Dictionary<string, object> parameters)
    {
        if (parameters.TryGetValue("Address", out var address))
        {
            return Convert.ToUInt16(address);
        }

        if (parameters.TryGetValue("StartAddress", out var startAddress))
        {
            return Convert.ToUInt16(startAddress);
        }

        return 0;
    }

    private static ushort GetQuantity(Dictionary<string, object> parameters)
    {
        return parameters.TryGetValue("Quantity", out var quantity)
            ? Convert.ToUInt16(quantity)
            : (ushort)1;
    }

    private static bool GetBool(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value))
        {
            throw new ArgumentException($"Missing Modbus parameter '{key}'.");
        }

        return Convert.ToBoolean(value);
    }

    private static ushort GetUInt16(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value))
        {
            throw new ArgumentException($"Missing Modbus parameter '{key}'.");
        }

        return Convert.ToUInt16(value);
    }

    private static bool[] GetBoolValues(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("Values", out var value))
        {
            if (parameters.TryGetValue("Value", out var single))
            {
                return new[] { Convert.ToBoolean(single) };
            }

            throw new ArgumentException("Missing Modbus parameter 'Values'.");
        }

        return value switch
        {
            bool[] values => values,
            IEnumerable<bool> values => values.ToArray(),
            IEnumerable<object> values => values.Select(Convert.ToBoolean).ToArray(),
            _ => throw new ArgumentException("Modbus coil Values must be a boolean sequence.")
        };
    }

    private static ushort[] GetRegisterValues(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("Values", out var value))
        {
            if (parameters.TryGetValue("Value", out var single))
            {
                return new[] { Convert.ToUInt16(single) };
            }

            throw new ArgumentException("Missing Modbus parameter 'Values'.");
        }

        return value switch
        {
            ushort[] values => values,
            short[] values => values.Select(Convert.ToUInt16).ToArray(),
            int[] values => values.Select(Convert.ToUInt16).ToArray(),
            uint[] values => values.Select(Convert.ToUInt16).ToArray(),
            IEnumerable<ushort> values => values.ToArray(),
            IEnumerable<object> values => values.Select(Convert.ToUInt16).ToArray(),
            _ => throw new ArgumentException("Modbus register Values must be an unsigned 16-bit sequence.")
        };
    }

    private static void ValidateQuantity(ushort quantity, ushort min, ushort max)
    {
        if (quantity < min || quantity > max)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, $"Modbus quantity must be between {min} and {max}.");
        }
    }
}
