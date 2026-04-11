using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.DeviceMock.Communication;
using Vktun.IoT.Connector.DeviceMock.Services;

namespace Vktun.IoT.Connector.DeviceMock.Protocols.Siemens;

public class S7Server : TcpServerBase, IDeviceSimulator
{
    private readonly S7DataBlockManager _dataManager;
    private readonly int _port;
    
    public string DeviceId { get; }
    public Core.Enums.ProtocolType ProtocolType => Core.Enums.ProtocolType.S7;
    
    public S7Server(string deviceId, int port, S7DataBlockManager dataManager, ILogger logger)
        : base(logger)
    {
        DeviceId = deviceId;
        _port = port;
        _dataManager = dataManager;
    }
    
    Task IDeviceSimulator.StartAsync(CancellationToken cancellationToken)
    {
        return StartAsync(_port, cancellationToken);
    }
    
    protected override async Task HandleClientAsync(TcpClient client)
    {
        var stream = client.GetStream();
        
        _logger.Info($"S7客户端已连接: {client.Client.RemoteEndPoint}");
        
        try
        {
            while (IsRunning && client.Connected)
            {
                var tpktHeader = await ReadExactAsync(stream, 4);
                if (tpktHeader == null)
                {
                    break;
                }
                
                var tpktLength = (tpktHeader[2] << 8) | tpktHeader[3];
                if (tpktLength < 4 || tpktLength > 2048)
                {
                    break;
                }
                
                var remaining = tpktLength - 4;
                byte[] request;
                if (remaining > 0)
                {
                    var rest = await ReadExactAsync(stream, remaining);
                    if (rest == null)
                    {
                        break;
                    }
                    
                    request = new byte[tpktLength];
                    Array.Copy(tpktHeader, 0, request, 0, 4);
                    Array.Copy(rest, 0, request, 4, remaining);
                }
                else
                {
                    request = tpktHeader;
                }
                
                var response = ProcessRequest(request);
                if (response != null && response.Length > 0)
                {
                    await stream.WriteAsync(response, 0, response.Length);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"处理S7客户端请求失败: {ex.Message}", ex);
        }
        finally
        {
            RemoveClient(client);
            _logger.Info($"S7客户端已断开: {client.Client.RemoteEndPoint}");
        }
    }
    
    private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var totalRead = 0;
        
        while (totalRead < count)
        {
            var bytesRead = await stream.ReadAsync(buffer, totalRead, count - totalRead);
            if (bytesRead == 0)
            {
                return null;
            }
            
            totalRead += bytesRead;
        }
        
        return buffer;
    }
    
    private byte[]? ProcessRequest(byte[] request)
    {
        if (request.Length < 4)
        {
            return null;
        }
        
        var tpktVersion = request[0];
        var tpktReserved = request[1];
        var tpktLength = (request[2] << 8) | request[3];
        
        if (request.Length < tpktLength)
        {
            return null;
        }
        
        if (request.Length < 8)
        {
            return null;
        }
        
        var isoLength = request[4];
        var isoPduType = request[5];
        
        if (isoPduType == 0xE0)
        {
            return HandleConnectionRequest(request);
        }
        
        if (request.Length < 10)
        {
            return null;
        }
        
        var s7ProtocolId = request[7];
        var s7PduType = request[8];
        
        if (s7ProtocolId == 0x32)
        {
            return HandleS7Request(request);
        }
        
        return null;
    }
    
    private byte[] HandleConnectionRequest(byte[] request)
    {
        var isoHeader = S7ProtocolHandler.BuildIsoHeader();
        var tpktHeader = S7ProtocolHandler.BuildTpktHeader(isoHeader.Length + 4);
        
        var response = new byte[tpktHeader.Length + isoHeader.Length];
        Array.Copy(tpktHeader, 0, response, 0, tpktHeader.Length);
        Array.Copy(isoHeader, 0, response, tpktHeader.Length, isoHeader.Length);
        
        return response;
    }
    
    private byte[]? HandleS7Request(byte[] request)
    {
        if (request.Length < 10)
        {
            return null;
        }
        
        var pduType = request[8];
        var requestId = (ushort)((request[9] << 8) | request[10]);
        var parameterLength = (ushort)((request[11] << 8) | request[12]);
        var dataLength = (ushort)((request[13] << 8) | request[14]);
        
        if (pduType == 0x01)
        {
            if (parameterLength == 0)
            {
                return null;
            }
            
            var functionCode = request[15];
            
            return functionCode switch
            {
                0x04 => HandleReadRequest(request, requestId),
                0x05 => HandleWriteRequest(request, requestId),
                _ => BuildErrorResponse(requestId, 0x05)
            };
        }
        
        return null;
    }
    
    private byte[] HandleReadRequest(byte[] request, ushort requestId)
    {
        if (request.Length < 28)
        {
            return BuildErrorResponse(requestId, 0x05);
        }
        
        var itemCount = request[16];
        var responses = new List<byte[]>();
        
        for (int i = 0; i < itemCount; i++)
        {
            var offset = 17 + i * 12;
            var (dbNumber, area, address, length, bitLength) = S7ProtocolHandler.ParseReadRequest(request, offset);
            
            byte[] data;
            
            if (area == 0x84)
            {
                var bytesToRead = bitLength == 1 ? (length + 7) / 8 : length * (bitLength / 8);
                data = _dataManager.GetBytes($"DB{dbNumber}.{address}", bytesToRead);
            }
            else
            {
                var areaName = area switch
                {
                    0x81 => "I",
                    0x82 => "Q",
                    0x83 => "M",
                    _ => "M"
                };
                
                var bytesToRead = bitLength == 1 ? (length + 7) / 8 : length * (bitLength / 8);
                data = _dataManager.GetBytes($"{areaName}.{address}", bytesToRead);
            }
            
            responses.Add(data);
        }
        
        var totalDataLength = responses.Sum(r => r.Length + 4);
        var s7Header = S7ProtocolHandler.BuildS7Header(0x03, requestId, 0, (ushort)totalDataLength);
        
        var result = new byte[s7Header.Length + totalDataLength];
        Array.Copy(s7Header, 0, result, 0, s7Header.Length);
        
        var dataOffset = s7Header.Length;
        foreach (var data in responses)
        {
            var dataHeader = new byte[] { 0xFF, 0x04, (byte)((data.Length >> 8) & 0xFF), (byte)(data.Length & 0xFF) };
            Array.Copy(dataHeader, 0, result, dataOffset, dataHeader.Length);
            dataOffset += dataHeader.Length;
            Array.Copy(data, 0, result, dataOffset, data.Length);
            dataOffset += data.Length;
        }
        
        var tpktHeader = S7ProtocolHandler.BuildTpktHeader(result.Length + 4);
        var finalResponse = new byte[tpktHeader.Length + result.Length];
        Array.Copy(tpktHeader, 0, finalResponse, 0, tpktHeader.Length);
        Array.Copy(result, 0, finalResponse, tpktHeader.Length, result.Length);
        
        return finalResponse;
    }
    
    private byte[] HandleWriteRequest(byte[] request, ushort requestId)
    {
        if (request.Length < 28)
        {
            return BuildErrorResponse(requestId, 0x05);
        }
        
        var itemCount = request[16];
        
        for (int i = 0; i < itemCount; i++)
        {
            var offset = 17 + i * 12;
            var (dbNumber, area, address, length, bitLength, data) = S7ProtocolHandler.ParseWriteRequest(request, offset);
            
            if (area == 0x84)
            {
                _dataManager.SetBytes($"DB{dbNumber}.{address}", data);
            }
            else
            {
                var areaName = area switch
                {
                    0x81 => "I",
                    0x82 => "Q",
                    0x83 => "M",
                    _ => "M"
                };
                
                _dataManager.SetBytes($"{areaName}.{address}", data);
            }
        }
        
        var s7Header = S7ProtocolHandler.BuildS7Header(0x03, requestId, 0, (ushort)(itemCount * 4));
        var dataHeader = new byte[] { 0xFF, 0x04, 0x00, 0x01 };
        
        var result = new byte[s7Header.Length + itemCount * dataHeader.Length];
        Array.Copy(s7Header, 0, result, 0, s7Header.Length);
        
        var dataOffset = s7Header.Length;
        for (int i = 0; i < itemCount; i++)
        {
            Array.Copy(dataHeader, 0, result, dataOffset, dataHeader.Length);
            dataOffset += dataHeader.Length;
        }
        
        var tpktHeader = S7ProtocolHandler.BuildTpktHeader(result.Length + 4);
        var finalResponse = new byte[tpktHeader.Length + result.Length];
        Array.Copy(tpktHeader, 0, finalResponse, 0, tpktHeader.Length);
        Array.Copy(result, 0, finalResponse, tpktHeader.Length, result.Length);
        
        return finalResponse;
    }
    
    private byte[] BuildErrorResponse(ushort requestId, byte errorCode)
    {
        var s7Header = S7ProtocolHandler.BuildS7Header(0x03, requestId, 0, 2);
        var errorData = new byte[] { 0xFF, errorCode };
        
        var result = new byte[s7Header.Length + errorData.Length];
        Array.Copy(s7Header, 0, result, 0, s7Header.Length);
        Array.Copy(errorData, 0, result, s7Header.Length, errorData.Length);
        
        var tpktHeader = S7ProtocolHandler.BuildTpktHeader(result.Length + 4);
        var finalResponse = new byte[tpktHeader.Length + result.Length];
        Array.Copy(tpktHeader, 0, finalResponse, 0, tpktHeader.Length);
        Array.Copy(result, 0, finalResponse, tpktHeader.Length, result.Length);
        
        return finalResponse;
    }
    
    public void SetDataPoint(string address, object value)
    {
        var parts = address.Split('.');
        if (parts.Length < 2)
        {
            return;
        }
        
        var area = parts[0].ToUpper();
        
        if (area == "DB")
        {
            if (parts.Length >= 3)
            {
                var dbNumber = int.Parse(parts[1]);
                var dbAddress = parts[2];
                
                if (dbAddress.Contains('.'))
                {
                    var dbParts = dbAddress.Split('.');
                    if (dbParts.Length == 2 && int.TryParse(dbParts[1], out var bitOffset))
                    {
                        _dataManager.SetBit($"DB{dbNumber}.{dbParts[0]}.{bitOffset}", Convert.ToBoolean(value));
                    }
                }
                else
                {
                    _dataManager.SetWord($"DB{dbNumber}.{dbAddress}", Convert.ToUInt16(value));
                }
            }
        }
        else
        {
            if (address.Contains('.'))
            {
                var bitParts = address.Split('.');
                if (bitParts.Length == 3 && int.TryParse(bitParts[2], out var bitOffset))
                {
                    _dataManager.SetBit(address, Convert.ToBoolean(value));
                }
                else
                {
                    _dataManager.SetWord(address, Convert.ToUInt16(value));
                }
            }
            else
            {
                _dataManager.SetWord(address, Convert.ToUInt16(value));
            }
        }
    }
    
    public object GetDataPoint(string address)
    {
        var parts = address.Split('.');
        if (parts.Length < 2)
        {
            return 0;
        }
        
        var area = parts[0].ToUpper();
        
        if (area == "DB")
        {
            if (parts.Length >= 3)
            {
                var dbNumber = int.Parse(parts[1]);
                var dbAddress = parts[2];
                
                if (dbAddress.Contains('.'))
                {
                    var dbParts = dbAddress.Split('.');
                    if (dbParts.Length == 2 && int.TryParse(dbParts[1], out var bitOffset))
                    {
                        return _dataManager.GetBit($"DB{dbNumber}.{dbParts[0]}.{bitOffset}");
                    }
                }
                else
                {
                    return _dataManager.GetWord($"DB{dbNumber}.{dbAddress}");
                }
            }
        }
        else
        {
            if (address.Contains('.'))
            {
                var bitParts = address.Split('.');
                if (bitParts.Length == 3 && int.TryParse(bitParts[2], out var bitOffset))
                {
                    return _dataManager.GetBit(address);
                }
                else
                {
                    return _dataManager.GetWord(address);
                }
            }
            else
            {
                return _dataManager.GetWord(address);
            }
        }
        
        return 0;
    }
}
