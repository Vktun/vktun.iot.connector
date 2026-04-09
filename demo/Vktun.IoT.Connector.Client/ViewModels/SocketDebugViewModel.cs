using System.Collections.ObjectModel;
using System.Text;
using System.Timers;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using Vktun.IoT.Connector.Client.Models;
using Vktun.IoT.Connector.Client.Services;
using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Client.ViewModels;

public class SocketDebugViewModel : BindableBase, INavigationAware
{
    private readonly ISocketTestService _socketTestService;
    private readonly StringBuilder _logBuilder = new();
    private System.Timers.Timer? _autoSendTimer;

    private CommunicationType _selectedCommunicationType = CommunicationType.Tcp;
    private ConnectionMode _selectedConnectionMode = ConnectionMode.Client;
    private string _ipAddress = "127.0.0.1";
    private int _port = 502;
    private string _localIpAddress = string.Empty;
    private int _localPort = 0;
    private int _timeout = 3000;
    private int _sendInterval = 1000;
    private bool _autoSend;
    private string _sendPayload = "Hello";
    private bool _isHexMode;
    private bool _isConnected;
    private string _logMessages = string.Empty;

    public ObservableCollection<CommunicationType> CommunicationTypes { get; } = new()
    {
        CommunicationType.Tcp,
        CommunicationType.Udp
    };

    public ObservableCollection<ConnectionMode> ConnectionModes { get; } = new()
    {
        ConnectionMode.Client,
        ConnectionMode.Server
    };

    public CommunicationType SelectedCommunicationType
    {
        get => _selectedCommunicationType;
        set => SetProperty(ref _selectedCommunicationType, value);
    }

    public ConnectionMode SelectedConnectionMode
    {
        get => _selectedConnectionMode;
        set => SetProperty(ref _selectedConnectionMode, value);
    }

    public string IpAddress
    {
        get => _ipAddress;
        set => SetProperty(ref _ipAddress, value);
    }

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public string LocalIpAddress
    {
        get => _localIpAddress;
        set => SetProperty(ref _localIpAddress, value);
    }

    public int LocalPort
    {
        get => _localPort;
        set => SetProperty(ref _localPort, value);
    }

    public int Timeout
    {
        get => _timeout;
        set => SetProperty(ref _timeout, value);
    }

    public int SendInterval
    {
        get => _sendInterval;
        set => SetProperty(ref _sendInterval, value);
    }

    public bool AutoSend
    {
        get => _autoSend;
        set
        {
            if (SetProperty(ref _autoSend, value))
            {
                if (value && IsConnected)
                {
                    StartAutoSend();
                }
                else
                {
                    StopAutoSend();
                }
            }
        }
    }

    public string SendPayload
    {
        get => _sendPayload;
        set => SetProperty(ref _sendPayload, value);
    }

    public bool IsHexMode
    {
        get => _isHexMode;
        set => SetProperty(ref _isHexMode, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            SetProperty(ref _isConnected, value);
            ConnectCommand.RaiseCanExecuteChanged();
            DisconnectCommand.RaiseCanExecuteChanged();
            SendCommand.RaiseCanExecuteChanged();
            
            if (value && AutoSend)
            {
                StartAutoSend();
            }
            else if (!value)
            {
                StopAutoSend();
            }
        }
    }

    public string LogMessages
    {
        get => _logMessages;
        set => SetProperty(ref _logMessages, value);
    }

    public DelegateCommand ConnectCommand { get; }
    public DelegateCommand DisconnectCommand { get; }
    public DelegateCommand SendCommand { get; }
    public DelegateCommand ClearLogCommand { get; }

    public SocketDebugViewModel(ISocketTestService socketTestService)
    {
        _socketTestService = socketTestService;
        _socketTestService.LogMessage += OnServiceLogMessage;
        _socketTestService.DataReceived += OnServiceDataReceived;

        ConnectCommand = new DelegateCommand(OnConnect, () => !IsConnected);
        DisconnectCommand = new DelegateCommand(OnDisconnect, () => IsConnected);
        SendCommand = new DelegateCommand(OnSend, () => IsConnected);
        ClearLogCommand = new DelegateCommand(OnClearLog);
    }

    private async void OnConnect()
    {
        var config = new ConnectionConfig
        {
            ProtocolType = ProtocolType.Custom,
            CommunicationType = SelectedCommunicationType,
            ConnectionMode = SelectedConnectionMode,
            IpAddress = IpAddress,
            Port = Port,
            LocalIpAddress = LocalIpAddress,
            LocalPort = LocalPort,
            Timeout = Timeout > 0 ? Timeout : 3000,
            SendInterval = SendInterval > 0 ? SendInterval : 1000,
            AutoSend = AutoSend
        };

        IsConnected = await _socketTestService.ConnectAsync(config).ConfigureAwait(true);
    }

    private async void OnDisconnect()
    {
        await _socketTestService.DisconnectAsync().ConfigureAwait(true);
        IsConnected = false;
    }

    private async void OnSend()
    {
        if (string.IsNullOrWhiteSpace(SendPayload))
        {
            AppendLog("Payload is empty.");
            return;
        }

        try
        {
            var payload = IsHexMode ? ParseHexPayload(SendPayload) : Encoding.UTF8.GetBytes(SendPayload);
            var sent = await _socketTestService.SendAsync(payload).ConfigureAwait(true);
            AppendLog($"Send completed, bytes={sent}, mode={(IsHexMode ? "HEX" : "TEXT")}.");
        }
        catch (Exception ex)
        {
            AppendLog($"Send failed: {ex.Message}");
        }
    }

    private void OnClearLog()
    {
        _logBuilder.Clear();
        LogMessages = string.Empty;
    }

    private void OnServiceLogMessage(object? sender, string message)
    {
        AppendLog(message);
    }

    private void OnServiceDataReceived(object? sender, byte[] data)
    {
        var hex = BitConverter.ToString(data).Replace("-", " ");
        var text = Encoding.UTF8.GetString(data);
        AppendLog($"RX HEX: {hex}");
        AppendLog($"RX TEXT: {text}");
    }

    private void AppendLog(string message)
    {
        _logBuilder.AppendLine(message);
        LogMessages = _logBuilder.ToString();
    }

    private static byte[] ParseHexPayload(string payload)
    {
        var tokens = payload
            .Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => Convert.ToByte(token, 16))
            .ToArray();

        if (tokens.Length == 0)
        {
            throw new FormatException("No valid HEX bytes were provided.");
        }

        return tokens;
    }

    private void StartAutoSend()
    {
        StopAutoSend();
        
        if (SendInterval < 100)
        {
            SendInterval = 100;
        }

        _autoSendTimer = new System.Timers.Timer(SendInterval);
        _autoSendTimer.Elapsed += OnAutoSendTimerElapsed;
        _autoSendTimer.AutoReset = true;
        _autoSendTimer.Enabled = true;
        AppendLog($"Auto send started with interval {SendInterval}ms.");
    }

    private void StopAutoSend()
    {
        if (_autoSendTimer != null)
        {
            _autoSendTimer.Enabled = false;
            _autoSendTimer.Elapsed -= OnAutoSendTimerElapsed;
            _autoSendTimer.Dispose();
            _autoSendTimer = null;
            AppendLog("Auto send stopped.");
        }
    }

    private void OnAutoSendTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(SendPayload))
        {
            return;
        }

        try
        {
            var payload = IsHexMode ? ParseHexPayload(SendPayload) : Encoding.UTF8.GetBytes(SendPayload);
            var sent = _socketTestService.SendAsync(payload).GetAwaiter().GetResult();
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AppendLog($"Auto sent {sent} bytes, mode={(IsHexMode ? "HEX" : "TEXT")}.");
            });
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AppendLog($"Auto send failed: {ex.Message}");
            });
        }
    }

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
    }

    public bool IsNavigationTarget(NavigationContext navigationContext) => true;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }
}

