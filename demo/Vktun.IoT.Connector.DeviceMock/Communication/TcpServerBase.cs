using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.DeviceMock.Communication;

public abstract class TcpServerBase
{
    protected TcpListener? _listener;
    protected CancellationTokenSource? _cts;
    protected readonly List<TcpClient> _clients = new();
    protected readonly ILogger _logger;
    protected readonly object _lock = new();
    
    public bool IsRunning { get; protected set; }
    
    protected TcpServerBase(ILogger logger)
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
        _listener = new TcpListener(IPAddress.Any, port);
        
        try
        {
            _listener.Start();
            IsRunning = true;
            
            _logger.Info($"TCP服务器已启动，监听端口: {port}");
            
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    lock (_lock)
                    {
                        _clients.Add(client);
                    }
                    
                    _ = Task.Run(() => HandleClientAsync(client), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error($"接受客户端连接失败: {ex.Message}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"启动TCP服务器失败: {ex.Message}", ex);
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
        
        lock (_lock)
        {
            foreach (var client in _clients)
            {
                try
                {
                    client.Close();
                }
                catch { }
            }
            _clients.Clear();
        }
        
        _listener?.Stop();
        IsRunning = false;
        
        _logger.Info("TCP服务器已停止");
        
        await Task.CompletedTask;
    }
    
    protected abstract Task HandleClientAsync(TcpClient client);
    
    protected void RemoveClient(TcpClient client)
    {
        lock (_lock)
        {
            _clients.Remove(client);
        }
        
        try
        {
            client.Close();
        }
        catch { }
    }
}
