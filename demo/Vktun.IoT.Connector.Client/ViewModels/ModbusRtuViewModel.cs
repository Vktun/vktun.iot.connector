using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text;
using System.Windows;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using Vktun.IoT.Connector.Client.Models;
using Vktun.IoT.Connector.Client.Services;
using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Client.ViewModels;

public class ModbusRtuViewModel : BindableBase, INavigationAware
{
    private readonly IProtocolTestService _testService;
    private readonly StringBuilder _logBuilder = new();
    
    private byte _slaveId = 1;
    public byte SlaveId
    {
        get => _slaveId;
        set => SetProperty(ref _slaveId, value);
    }
    
    private string _address = "0";
    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }
    
    private string _writeValue = string.Empty;
    public string WriteValue
    {
        get => _writeValue;
        set => SetProperty(ref _writeValue, value);
    }
    
    private string _logMessages = string.Empty;
    public string LogMessages
    {
        get => _logMessages;
        set => SetProperty(ref _logMessages, value);
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
        }
    }
    
    public ObservableCollection<string> PortNames { get; }
    public ObservableCollection<int> BaudRates { get; }
    public ObservableCollection<int> DataBitsList { get; }
    public ObservableCollection<string> StopBitsList { get; }
    public ObservableCollection<string> ParityList { get; }
    public ObservableCollection<string> RegisterTypes { get; }
    public ObservableCollection<string> DataTypes { get; }
    public ObservableCollection<DeviceTestResult> TestResults { get; }
    
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
    
    private string _selectedRegisterType = "HoldingRegister";
    public string SelectedRegisterType
    {
        get => _selectedRegisterType;
        set => SetProperty(ref _selectedRegisterType, value);
    }
    
    private string _selectedDataType = "Int16";
    public string SelectedDataType
    {
        get => _selectedDataType;
        set => SetProperty(ref _selectedDataType, value);
    }
    
    public DelegateCommand OpenCommand { get; }
    public DelegateCommand CloseCommand { get; }
    public DelegateCommand ReadCommand { get; }
    public DelegateCommand WriteCommand { get; }
    
    public ModbusRtuViewModel(IProtocolTestService testService)
    {
        _testService = testService;
        _testService.LogMessage += OnLogMessage;
        
        PortNames = new ObservableCollection<string>(SerialPort.GetPortNames());
        if (PortNames.Count == 0)
        {
            PortNames.Add("COM1");
        }
        
        BaudRates = new ObservableCollection<int> { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };
        DataBitsList = new ObservableCollection<int> { 7, 8 };
        StopBitsList = new ObservableCollection<string> { "One", "OnePointFive", "Two" };
        ParityList = new ObservableCollection<string> { "None", "Odd", "Even", "Mark", "Space" };
        
        RegisterTypes = new ObservableCollection<string>
        {
            "Coil", "DiscreteInput", "InputRegister", "HoldingRegister"
        };
        
        DataTypes = new ObservableCollection<string>
        {
            "Bool", "Int8", "UInt8", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Float", "Double"
        };
        
        TestResults = new ObservableCollection<DeviceTestResult>();
        
        OpenCommand = new DelegateCommand(OnOpen, () => !IsConnected);
        CloseCommand = new DelegateCommand(OnClose, () => IsConnected);
        ReadCommand = new DelegateCommand(OnRead);
        WriteCommand = new DelegateCommand(OnWrite);
    }
    
    private async void OnOpen()
    {
        try
        {
            var config = new ConnectionConfig
            {
                ProtocolType = ProtocolType.ModbusRtu,
                PortName = SelectedPortName,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                StopBits = Enum.Parse<Models.StopBits>(SelectedStopBits),
                Parity = Enum.Parse<Models.Parity>(SelectedParity),
                SlaveId = SlaveId
            };
            
            IsConnected = await _testService.ConnectAsync(config);
            AddLog(IsConnected ? "串口打开成功" : "串口打开失败");
        }
        catch (Exception ex)
        {
            AddLog($"打开串口异常: {ex.Message}");
        }
    }
    
    private async void OnClose()
    {
        await _testService.DisconnectAsync(ProtocolType.ModbusRtu);
        IsConnected = false;
        AddLog("串口已关闭");
    }
    
    private async void OnRead()
    {
        try
        {
            if (!int.TryParse(Address, out var address))
            {
                MessageBox.Show("请输入有效的地址");
                return;
            }
            
            var dataType = Enum.Parse<DataType>(SelectedDataType);
            var parameters = new Dictionary<string, object>
            {
                { "SlaveId", SlaveId },
                { "RegisterType", SelectedRegisterType }
            };
            
            var result = await _testService.ReadAsync(ProtocolType.ModbusRtu, Address, dataType, parameters);
            
            TestResults.Insert(0, new DeviceTestResult
            {
                Address = result.Address,
                Value = result.Value,
                DataType = result.DataType,
                IsSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage,
                Duration = result.Duration,
                Timestamp = result.Timestamp
            });
        }
        catch (Exception ex)
        {
            AddLog($"读取失败: {ex.Message}");
        }
    }
    
    private async void OnWrite()
    {
        try
        {
            if (string.IsNullOrEmpty(WriteValue))
            {
                MessageBox.Show("请输入写入值");
                return;
            }
            
            var dataType = Enum.Parse<DataType>(SelectedDataType);
            var value = ConvertValue(WriteValue, dataType);
            
            var parameters = new Dictionary<string, object>
            {
                { "SlaveId", SlaveId },
                { "RegisterType", SelectedRegisterType }
            };
            
            var result = await _testService.WriteAsync(ProtocolType.ModbusRtu, Address, value, dataType, parameters);
            
            AddLog(result.IsSuccess ? $"写入成功: {Address} = {value}" : $"写入失败: {result.ErrorMessage}");
        }
        catch (Exception ex)
        {
            AddLog($"写入异常: {ex.Message}");
        }
    }
    
    private object ConvertValue(string valueStr, DataType dataType)
    {
        return dataType switch
        {
            DataType.Bit => bool.Parse(valueStr),
            DataType.Int8 => sbyte.Parse(valueStr),
            DataType.UInt8 => byte.Parse(valueStr),
            DataType.Int16 => short.Parse(valueStr),
            DataType.UInt16 => ushort.Parse(valueStr),
            DataType.Int32 => int.Parse(valueStr),
            DataType.UInt32 => uint.Parse(valueStr),
            DataType.Int64 => long.Parse(valueStr),
            DataType.UInt64 => ulong.Parse(valueStr),
            DataType.Float => float.Parse(valueStr),
            DataType.Double => double.Parse(valueStr),
            _ => valueStr
        };
    }
    
    private void OnLogMessage(object? sender, string message)
    {
        AddLog(message);
    }
    
    private void AddLog(string message)
    {
        _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        LogMessages = _logBuilder.ToString();
    }
    
    public void OnNavigatedTo(NavigationContext navigationContext)
    {
    }
    
    public bool IsNavigationTarget(NavigationContext navigationContext) => true;
    
    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }
}
