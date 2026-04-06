using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using Vktun.IoT.Connector.Client.Models;
using Vktun.IoT.Connector.Client.Services;
using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Client.ViewModels;

public class SiemensViewModel : BindableBase, INavigationAware
{
    private readonly IProtocolTestService _testService;
    private readonly StringBuilder _logBuilder = new();
    
    private string _ipAddress = "192.168.0.1";
    public string IpAddress
    {
        get => _ipAddress;
        set => SetProperty(ref _ipAddress, value);
    }
    
    private int _port = 102;
    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }
    
    private int _rack = 0;
    public int Rack
    {
        get => _rack;
        set => SetProperty(ref _rack, value);
    }
    
    private int _slot = 1;
    public int Slot
    {
        get => _slot;
        set => SetProperty(ref _slot, value);
    }
    
    private string _address = "DB1.DBW0";
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
            ConnectCommand.RaiseCanExecuteChanged();
            DisconnectCommand.RaiseCanExecuteChanged();
        }
    }
    
    public ObservableCollection<string> CpuTypes { get; }
    public ObservableCollection<string> AreaTypes { get; }
    public ObservableCollection<string> DataTypes { get; }
    public ObservableCollection<DeviceTestResult> TestResults { get; }
    
    private string _selectedCpuType = "S71200";
    public string SelectedCpuType
    {
        get => _selectedCpuType;
        set => SetProperty(ref _selectedCpuType, value);
    }
    
    private string _selectedAreaType = "DB";
    public string SelectedAreaType
    {
        get => _selectedAreaType;
        set => SetProperty(ref _selectedAreaType, value);
    }
    
    private string _selectedDataType = "Int16";
    public string SelectedDataType
    {
        get => _selectedDataType;
        set => SetProperty(ref _selectedDataType, value);
    }
    
    public DelegateCommand ConnectCommand { get; }
    public DelegateCommand DisconnectCommand { get; }
    public DelegateCommand ReadCommand { get; }
    public DelegateCommand WriteCommand { get; }
    
    public SiemensViewModel(IProtocolTestService testService)
    {
        _testService = testService;
        _testService.LogMessage += OnLogMessage;
        
        CpuTypes = new ObservableCollection<string>
        {
            "S7200", "S7300", "S7400", "S71200", "S71500"
        };
        
        AreaTypes = new ObservableCollection<string>
        {
            "DB", "I", "Q", "M", "C", "T"
        };
        
        DataTypes = new ObservableCollection<string>
        {
            "Bool", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Float", "Double"
        };
        
        TestResults = new ObservableCollection<DeviceTestResult>();
        
        ConnectCommand = new DelegateCommand(OnConnect, () => !IsConnected);
        DisconnectCommand = new DelegateCommand(OnDisconnect, () => IsConnected);
        ReadCommand = new DelegateCommand(OnRead);
        WriteCommand = new DelegateCommand(OnWrite);
    }
    
    private async void OnConnect()
    {
        try
        {
            var config = new ConnectionConfig
            {
                ProtocolType = ProtocolType.S7,
                IpAddress = IpAddress,
                Port = Port,
                Rack = Rack,
                Slot = Slot,
                CpuType = SelectedCpuType
            };
            
            IsConnected = await _testService.ConnectAsync(config);
            AddLog(IsConnected ? "连接成功" : "连接失败");
        }
        catch (Exception ex)
        {
            AddLog($"连接异常: {ex.Message}");
        }
    }
    
    private async void OnDisconnect()
    {
        await _testService.DisconnectAsync(ProtocolType.S7);
        IsConnected = false;
        AddLog("已断开连接");
    }
    
    private async void OnRead()
    {
        try
        {
            if (string.IsNullOrEmpty(Address))
            {
                MessageBox.Show("请输入有效的地址");
                return;
            }
            
            var dataType = Enum.Parse<DataType>(SelectedDataType);
            var parameters = new Dictionary<string, object>
            {
                { "CpuType", SelectedCpuType },
                { "AreaType", SelectedAreaType }
            };
            
            var result = await _testService.ReadAsync(ProtocolType.S7, Address, dataType, parameters);
            
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
                { "CpuType", SelectedCpuType },
                { "AreaType", SelectedAreaType }
            };
            
            var result = await _testService.WriteAsync(ProtocolType.S7, Address, value, dataType, parameters);
            
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
