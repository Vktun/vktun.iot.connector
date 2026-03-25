using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using Vktun.IoT.Connector.Client.Models;
using Vktun.IoT.Connector.Client.Services;

namespace Vktun.IoT.Connector.Client.ViewModels;

public class SerialPortViewModel : BindableBase, INavigationAware
{
    private readonly IConnectionService _connectionService;
    private readonly DispatcherTimer? _autoSendTimer;
    
    private string _receivedData = string.Empty;
    public string ReceivedData
    {
        get => _receivedData;
        set => SetProperty(ref _receivedData, value);
    }
    
    private string _sendData = string.Empty;
    public string SendData
    {
        get => _sendData;
        set => SetProperty(ref _sendData, value);
    }
    
    private bool _isHexDisplay;
    public bool IsHexDisplay
    {
        get => _isHexDisplay;
        set => SetProperty(ref _isHexDisplay, value);
    }
    
    private bool _isHexSend;
    public bool IsHexSend
    {
        get => _isHexSend;
        set => SetProperty(ref _isHexSend, value);
    }
    
    private bool _isAutoSend;
    public bool IsAutoSend
    {
        get => _isAutoSend;
        set
        {
            SetProperty(ref _isAutoSend, value);
            if (_autoSendTimer != null)
            {
                _autoSendTimer.IsEnabled = value;
            }
        }
    }
    
    private int _autoSendInterval = 1000;
    public int AutoSendInterval
    {
        get => _autoSendInterval;
        set
        {
            SetProperty(ref _autoSendInterval, value);
            if (_autoSendTimer != null)
            {
                _autoSendTimer.Interval = TimeSpan.FromMilliseconds(value);
            }
        }
    }
    
    private long _receivedCount;
    public long ReceivedCount
    {
        get => _receivedCount;
        set => SetProperty(ref _receivedCount, value);
    }
    
    private long _sentCount;
    public long SentCount
    {
        get => _sentCount;
        set => SetProperty(ref _sentCount, value);
    }
    
    private string _status = "未连接";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
    
    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            SetProperty(ref _isConnected, value);
            OpenCommand.RaiseCanExecuteChanged();
            CloseCommand.RaiseCanExecuteChanged();
            Status = value ? "已连接" : "未连接";
        }
    }
    
    public ObservableCollection<string> PortNames { get; }
    public ObservableCollection<int> BaudRates { get; }
    public ObservableCollection<int> DataBitsList { get; }
    public ObservableCollection<string> StopBitsList { get; }
    public ObservableCollection<string> ParityList { get; }
    
    private string _selectedPortName = "COM1";
    public string SelectedPortName
    {
        get => _selectedPortName;
        set => SetProperty(ref _selectedPortName, value);
    }
    
    private int _selectedBaudRate = 9600;
    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set => SetProperty(ref _selectedBaudRate, value);
    }
    
    private int _selectedDataBits = 8;
    public int SelectedDataBits
    {
        get => _selectedDataBits;
        set => SetProperty(ref _selectedDataBits, value);
    }
    
    private string _selectedStopBits = "One";
    public string SelectedStopBits
    {
        get => _selectedStopBits;
        set => SetProperty(ref _selectedStopBits, value);
    }
    
    private string _selectedParity = "None";
    public string SelectedParity
    {
        get => _selectedParity;
        set => SetProperty(ref _selectedParity, value);
    }
    
    public DelegateCommand OpenCommand { get; }
    public DelegateCommand CloseCommand { get; }
    public DelegateCommand SendCommand { get; }
    public DelegateCommand ClearReceivedCommand { get; }
    
    public SerialPortViewModel(IConnectionService connectionService)
    {
        _connectionService = connectionService;
        
        PortNames = new ObservableCollection<string>(SerialPort.GetPortNames());
        if (PortNames.Count == 0)
        {
            PortNames.Add("COM1");
        }
        
        BaudRates = new ObservableCollection<int> { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };
        DataBitsList = new ObservableCollection<int> { 7, 8 };
        StopBitsList = new ObservableCollection<string> { "One", "OnePointFive", "Two" };
        ParityList = new ObservableCollection<string> { "None", "Odd", "Even", "Mark", "Space" };
        
        OpenCommand = new DelegateCommand(OnOpen, () => !IsConnected);
        CloseCommand = new DelegateCommand(OnClose, () => IsConnected);
        SendCommand = new DelegateCommand(OnSend);
        ClearReceivedCommand = new DelegateCommand(OnClearReceived);
        
        _autoSendTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AutoSendInterval)
        };
        _autoSendTimer.Tick += (s, e) => OnSend();
    }
    
    private async void OnOpen()
    {
        try
        {
            var config = new ConnectionConfig
            {
                PortName = SelectedPortName,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                StopBits = Enum.Parse<Models.StopBits>(SelectedStopBits),
                Parity = Enum.Parse<Models.Parity>(SelectedParity)
            };
            
            IsConnected = await _connectionService.ConnectAsync(config);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开串口失败: {ex.Message}");
        }
    }
    
    private async void OnClose()
    {
        await _connectionService.DisconnectAsync($"{SelectedPortName}");
        IsConnected = false;
    }
    
    private void OnSend()
    {
        if (!IsConnected || string.IsNullOrEmpty(SendData))
            return;
        
        try
        {
            var data = IsHexSend ? HexStringToBytes(SendData) : Encoding.UTF8.GetBytes(SendData);
            SentCount += data.Length;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"发送失败: {ex.Message}");
        }
    }
    
    private void OnClearReceived()
    {
        ReceivedData = string.Empty;
        ReceivedCount = 0;
    }
    
    private byte[] HexStringToBytes(string hex)
    {
        hex = hex.Replace(" ", "").Replace("\r", "").Replace("\n", "");
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }
    
    private string BytesToHexString(byte[] bytes)
    {
        var sb = new StringBuilder();
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("X2")).Append(" ");
        }
        return sb.ToString().Trim();
    }
    
    public void OnNavigatedTo(NavigationContext navigationContext)
    {
    }
    
    public bool IsNavigationTarget(NavigationContext navigationContext) => true;
    
    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }
}
