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
    private Task? _acceptLoopTask;

    public bool IsRunning { get; protected set; }

    protected TcpServerBase(ILogger logger)
    {
        _logger = logger;
    }

    public Task StartAsync(int port, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Any, port);

        try
        {
            _listener.Start();
            IsRunning = true;

            _logger.Info($"TCP服务器已启动，监听端口: {port}");

            _acceptLoopTask = AcceptLoopAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _logger.Error($"启动TCP服务器失败: {ex.Message}", ex);
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        _cts?.Cancel();

        if (_acceptLoopTask != null)
        {
            try
            {
                await _acceptLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

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

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                lock (_lock)
                {
                    _clients.Add(client);
                }

                _ = Task.Run(() => HandleClientAsync(client), cancellationToken);
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
}
