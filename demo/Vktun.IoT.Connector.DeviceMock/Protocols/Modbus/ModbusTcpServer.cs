using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.DeviceMock.Communication;
using Vktun.IoT.Connector.DeviceMock.Services;

namespace Vktun.IoT.Connector.DeviceMock.Protocols.Modbus;

public class ModbusTcpServer : TcpServerBase, IDeviceSimulator
{
    private readonly ModbusDataStore _dataStore;
    private readonly byte _slaveId;
    private readonly int _port;
    
    public string DeviceId { get; }
    public Core.Enums.ProtocolType ProtocolType => Core.Enums.ProtocolType.ModbusTcp;
    
    public ModbusTcpServer(string deviceId, byte slaveId, int port, ModbusDataStore dataStore, ILogger logger)
        : base(logger)
    {
        DeviceId = deviceId;
        _slaveId = slaveId;
        _port = port;
        _dataStore = dataStore;
    }
    
    Task IDeviceSimulator.StartAsync(CancellationToken cancellationToken)
    {
        return StartAsync(_port, cancellationToken);
    }
    
    protected override async Task HandleClientAsync(TcpClient client)
    {
        var stream = client.GetStream();
        var buffer = new byte[260];
        
        _logger.Info($"Modbus TCP客户端已连接: {client.Client.RemoteEndPoint}");
        
        try
        {
            while (IsRunning && client.Connected)
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    break;
                }
                
                var request = new byte[bytesRead];
                Array.Copy(buffer, 0, request, 0, bytesRead);
                
                var response = ProcessRequest(request);
                if (response != null)
                {
                    await stream.WriteAsync(response, 0, response.Length);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"处理Modbus TCP客户端请求失败: {ex.Message}", ex);
        }
        finally
        {
            RemoveClient(client);
            _logger.Info($"Modbus TCP客户端已断开: {client.Client.RemoteEndPoint}");
        }
    }
    
    private byte[]? ProcessRequest(byte[] request)
    {
        if (request.Length < 8)
        {
            return null;
        }
        
        var transactionId = (ushort)((request[0] << 8) | request[1]);
        var protocolId = (ushort)((request[2] << 8) | request[3]);
        var length = (ushort)((request[4] << 8) | request[5]);
        var unitId = request[6];
        var functionCode = request[7];
        
        if (unitId != _slaveId && unitId != 0xFF)
        {
            return null;
        }
        
        byte[]? responseData = functionCode switch
        {
            0x01 => ReadCoils(request),
            0x02 => ReadDiscreteInputs(request),
            0x03 => ReadHoldingRegisters(request),
            0x04 => ReadInputRegisters(request),
            0x05 => WriteSingleCoil(request),
            0x06 => WriteSingleRegister(request),
            0x0F => WriteMultipleCoils(request),
            0x10 => WriteMultipleRegisters(request),
            _ => BuildExceptionResponse(functionCode, 0x01)
        };
        
        if (responseData == null)
        {
            return null;
        }
        
        var response = new byte[7 + responseData.Length];
        response[0] = (byte)(transactionId >> 8);
        response[1] = (byte)(transactionId & 0xFF);
        response[2] = (byte)(protocolId >> 8);
        response[3] = (byte)(protocolId & 0xFF);
        response[4] = (byte)((responseData.Length + 1) >> 8);
        response[5] = (byte)((responseData.Length + 1) & 0xFF);
        response[6] = unitId;
        Array.Copy(responseData, 0, response, 7, responseData.Length);
        
        return response;
    }
    
    private byte[] ReadCoils(byte[] request)
    {
        var startAddress = (ushort)((request[8] << 8) | request[9]);
        var quantity = (ushort)((request[10] << 8) | request[11]);
        
        if (quantity < 1 || quantity > 2000)
        {
            return BuildExceptionResponse(0x01, 0x03);
        }
        
        var coils = _dataStore.GetCoils(startAddress, quantity);
        var byteCount = (byte)((quantity + 7) / 8);
        
        var response = new byte[2 + byteCount];
        response[0] = 0x01;
        response[1] = byteCount;
        
        for (int i = 0; i < quantity; i++)
        {
            if (coils[i])
            {
                response[2 + i / 8] |= (byte)(1 << (i % 8));
            }
        }
        
        return response;
    }
    
    private byte[] ReadDiscreteInputs(byte[] request)
    {
        var startAddress = (ushort)((request[8] << 8) | request[9]);
        var quantity = (ushort)((request[10] << 8) | request[11]);
        
        if (quantity < 1 || quantity > 2000)
        {
            return BuildExceptionResponse(0x02, 0x03);
        }
        
        var inputs = _dataStore.GetDiscreteInputs(startAddress, quantity);
        var byteCount = (byte)((quantity + 7) / 8);
        
        var response = new byte[2 + byteCount];
        response[0] = 0x02;
        response[1] = byteCount;
        
        for (int i = 0; i < quantity; i++)
        {
            if (inputs[i])
            {
                response[2 + i / 8] |= (byte)(1 << (i % 8));
            }
        }
        
        return response;
    }
    
    private byte[] ReadHoldingRegisters(byte[] request)
    {
        var startAddress = (ushort)((request[8] << 8) | request[9]);
        var quantity = (ushort)((request[10] << 8) | request[11]);
        
        if (quantity < 1 || quantity > 125)
        {
            return BuildExceptionResponse(0x03, 0x03);
        }
        
        var registers = _dataStore.GetHoldingRegisters(startAddress, quantity);
        var byteCount = (byte)(quantity * 2);
        
        var response = new byte[2 + byteCount];
        response[0] = 0x03;
        response[1] = byteCount;
        
        for (int i = 0; i < quantity; i++)
        {
            response[2 + i * 2] = (byte)(registers[i] >> 8);
            response[2 + i * 2 + 1] = (byte)(registers[i] & 0xFF);
        }
        
        return response;
    }
    
    private byte[] ReadInputRegisters(byte[] request)
    {
        var startAddress = (ushort)((request[8] << 8) | request[9]);
        var quantity = (ushort)((request[10] << 8) | request[11]);
        
        if (quantity < 1 || quantity > 125)
        {
            return BuildExceptionResponse(0x04, 0x03);
        }
        
        var registers = _dataStore.GetInputRegisters(startAddress, quantity);
        var byteCount = (byte)(quantity * 2);
        
        var response = new byte[2 + byteCount];
        response[0] = 0x04;
        response[1] = byteCount;
        
        for (int i = 0; i < quantity; i++)
        {
            response[2 + i * 2] = (byte)(registers[i] >> 8);
            response[2 + i * 2 + 1] = (byte)(registers[i] & 0xFF);
        }
        
        return response;
    }
    
    private byte[] WriteSingleCoil(byte[] request)
    {
        var address = (ushort)((request[8] << 8) | request[9]);
        var value = (ushort)((request[10] << 8) | request[11]);
        
        _dataStore.SetCoil(address, value == 0xFF00);
        
        var response = new byte[6];
        response[0] = 0x05;
        response[1] = (byte)(address >> 8);
        response[2] = (byte)(address & 0xFF);
        response[3] = (byte)(value >> 8);
        response[4] = (byte)(value & 0xFF);
        
        return response;
    }
    
    private byte[] WriteSingleRegister(byte[] request)
    {
        var address = (ushort)((request[8] << 8) | request[9]);
        var value = (ushort)((request[10] << 8) | request[11]);
        
        _dataStore.SetHoldingRegister(address, value);
        
        var response = new byte[6];
        response[0] = 0x06;
        response[1] = (byte)(address >> 8);
        response[2] = (byte)(address & 0xFF);
        response[3] = (byte)(value >> 8);
        response[4] = (byte)(value & 0xFF);
        
        return response;
    }
    
    private byte[] WriteMultipleCoils(byte[] request)
    {
        var startAddress = (ushort)((request[8] << 8) | request[9]);
        var quantity = (ushort)((request[10] << 8) | request[11]);
        var byteCount = request[12];
        
        var values = new bool[quantity];
        for (int i = 0; i < quantity; i++)
        {
            values[i] = (request[13 + i / 8] & (1 << (i % 8))) != 0;
        }
        
        _dataStore.SetCoils(startAddress, values);
        
        var response = new byte[6];
        response[0] = 0x0F;
        response[1] = (byte)(startAddress >> 8);
        response[2] = (byte)(startAddress & 0xFF);
        response[3] = (byte)(quantity >> 8);
        response[4] = (byte)(quantity & 0xFF);
        
        return response;
    }
    
    private byte[] WriteMultipleRegisters(byte[] request)
    {
        var startAddress = (ushort)((request[8] << 8) | request[9]);
        var quantity = (ushort)((request[10] << 8) | request[11]);
        var byteCount = request[12];
        
        var values = new ushort[quantity];
        for (int i = 0; i < quantity; i++)
        {
            values[i] = (ushort)((request[13 + i * 2] << 8) | request[13 + i * 2 + 1]);
        }
        
        _dataStore.SetHoldingRegisters(startAddress, values);
        
        var response = new byte[6];
        response[0] = 0x10;
        response[1] = (byte)(startAddress >> 8);
        response[2] = (byte)(startAddress & 0xFF);
        response[3] = (byte)(quantity >> 8);
        response[4] = (byte)(quantity & 0xFF);
        
        return response;
    }
    
    private byte[] BuildExceptionResponse(byte functionCode, byte errorCode)
    {
        return new byte[] { (byte)(functionCode | 0x80), errorCode };
    }
    
    public object GetDataPoint(string address)
    {
        if (!ushort.TryParse(address, out var addr))
        {
            return 0;
        }
        
        if (addr < 10000)
        {
            return _dataStore.GetCoil(addr);
        }
        else if (addr < 20000)
        {
            return _dataStore.GetDiscreteInput((ushort)(addr - 10000));
        }
        else if (addr < 30000)
        {
            return _dataStore.GetInputRegister((ushort)(addr - 30000));
        }
        else if (addr < 40000)
        {
            return _dataStore.GetInputRegister((ushort)(addr - 30000));
        }
        else
        {
            return _dataStore.GetHoldingRegister((ushort)(addr - 40000));
        }
    }
    
    public void SetDataPoint(string address, object value)
    {
        if (!ushort.TryParse(address, out var addr))
        {
            return;
        }
        
        if (addr < 10000)
        {
            _dataStore.SetCoil(addr, Convert.ToBoolean(value));
        }
        else if (addr < 20000)
        {
            _dataStore.SetCoil((ushort)(addr - 10000), Convert.ToBoolean(value));
        }
        else if (addr < 30000)
        {
            _dataStore.SetHoldingRegister((ushort)(addr - 30000), Convert.ToUInt16(value));
        }
        else if (addr < 40000)
        {
            _dataStore.SetHoldingRegister((ushort)(addr - 30000), Convert.ToUInt16(value));
        }
        else
        {
            _dataStore.SetHoldingRegister((ushort)(addr - 40000), Convert.ToUInt16(value));
        }
    }
}
