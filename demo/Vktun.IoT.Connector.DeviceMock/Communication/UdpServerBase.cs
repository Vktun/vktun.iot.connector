using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.DeviceMock.Communication;

public abstract class UdpServerBase
{
    protected UdpClient? _udpServer;
    protected CancellationTokenSource? _cts;
    protected readonly ILogger _logger;
    
    public bool IsRunning { get; protected set; }
    
    protected UdpServerBase(ILogger logger)
    {
        _logger = logger;
    }
    
    public async Task StartAsync(int port, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }
        
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _udpServer = new UdpClient(port);
        
        try
        {
            IsRunning = true;
            
            _logger.Info($"UDP服务器已启动，监听端口: {port}");
            
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpServer.ReceiveAsync(_cts.Token);
                    _ = Task.Run(() => HandleMessageAsync(result.Buffer, result.RemoteEndPoint), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error($"接收UDP消息失败: {ex.Message}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"启动UDP服务器失败: {ex.Message}", ex);
            throw;
        }
    }
    
    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }
        
        _cts?.Cancel();
        _udpServer?.Close();
        IsRunning = false;
        
        _logger.Info("UDP服务器已停止");
        
        await Task.CompletedTask;
    }
    
    protected abstract Task HandleMessageAsync(byte[] data, IPEndPoint remoteEP);
    
    protected async Task SendAsync(byte[] data, IPEndPoint remoteEP)
    {
        if (_udpServer == null || !IsRunning)
        {
            return;
        }
        
        try
        {
            await _udpServer.SendAsync(data, data.Length, remoteEP);
        }
        catch (Exception ex)
        {
            _logger.Error($"发送UDP消息失败: {ex.Message}", ex);
        }
    }
}
