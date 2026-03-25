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

public class OmronViewModel : BindableBase, INavigationAware
{
    private readonly IProtocolTestService _testService;
    private readonly StringBuilder _logBuilder = new();
    
    private string _ipAddress = "192.168.0.20";
    public string IpAddress
    {
        get => _ipAddress;
        set => SetProperty(ref _ipAddress, value);
    }
    
    private int _port = 9600;
    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }
    
    private string _address = "D100";
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
    
    public ObservableCollection<string> AreaTypes { get; }
    public ObservableCollection<string> DataTypes { get; }
    public ObservableCollection<DeviceTestResult> TestResults { get; }
    
    private string _selectedAreaType = "DM";
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
    
    public OmronViewModel(IProtocolTestService testService)
    {
        _testService = testService;
        _testService.LogMessage += OnLogMessage;
        
        AreaTypes = new ObservableCollection<string>
        {
            "CIO", "DM", "WR", "HR", "AR", "TIM", "CNT", "IR"
        };
        
        DataTypes = new ObservableCollection<string>
        {
            "Bool", "Int16", "UInt16", "Int32", "UInt32", "Float", "Double"
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
                ProtocolType = ProtocolType.Custom,
                IpAddress = IpAddress,
                Port = Port
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
        await _testService.DisconnectAsync(ProtocolType.Custom);
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
                { "AreaType", SelectedAreaType }
            };
            
            var result = await _testService.ReadAsync(ProtocolType.Custom, Address, dataType, parameters);
            
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
                { "AreaType", SelectedAreaType }
            };
            
            var result = await _testService.WriteAsync(ProtocolType.Custom, Address, value, dataType, parameters);
            
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
            DataType.bit => bool.Parse(valueStr),
            DataType.Int16 => short.Parse(valueStr),
            DataType.UInt16 => ushort.Parse(valueStr),
            DataType.Int32 => int.Parse(valueStr),
            DataType.UInt32 => uint.Parse(valueStr),
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
