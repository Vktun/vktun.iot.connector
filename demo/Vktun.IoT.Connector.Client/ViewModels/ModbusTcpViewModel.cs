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

public class ModbusTcpViewModel : BindableBase, INavigationAware
{
    private readonly IProtocolTestService _testService;
    private readonly StringBuilder _logBuilder = new();
    
    private string _ipAddress = "127.0.0.1";
    public string IpAddress
    {
        get => _ipAddress;
        set => SetProperty(ref _ipAddress, value);
    }
    
    private int _port = 502;
    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }
    
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
            ConnectCommand.RaiseCanExecuteChanged();
            DisconnectCommand.RaiseCanExecuteChanged();
        }
    }
    
    private ProtocolTestData? _selectedTestData;
    public ProtocolTestData? SelectedTestData
    {
        get => _selectedTestData;
        set => SetProperty(ref _selectedTestData, value);
    }
    
    public ObservableCollection<string> RegisterTypes { get; }
    public ObservableCollection<string> DataTypes { get; }
    public ObservableCollection<ProtocolTestData> TestDataList { get; }
    public ObservableCollection<DeviceTestResult> TestResults { get; }
    
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
    
    public DelegateCommand ConnectCommand { get; }
    public DelegateCommand DisconnectCommand { get; }
    public DelegateCommand ReadCommand { get; }
    public DelegateCommand WriteCommand { get; }
    public DelegateCommand AddTestDataCommand { get; }
    public DelegateCommand RemoveTestDataCommand { get; }
    public DelegateCommand BatchReadCommand { get; }
    public DelegateCommand BatchWriteCommand { get; }
    
    public ModbusTcpViewModel(IProtocolTestService testService)
    {
        _testService = testService;
        _testService.LogMessage += OnLogMessage;
        
        RegisterTypes = new ObservableCollection<string>
        {
            "Coil", "DiscreteInput", "InputRegister", "HoldingRegister"
        };
        
        DataTypes = new ObservableCollection<string>
        {
            "Bool", "Int8", "UInt8", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Float", "Double"
        };
        
        TestDataList = new ObservableCollection<ProtocolTestData>();
        TestResults = new ObservableCollection<DeviceTestResult>();
        
        ConnectCommand = new DelegateCommand(OnConnect, () => !IsConnected);
        DisconnectCommand = new DelegateCommand(OnDisconnect, () => IsConnected);
        ReadCommand = new DelegateCommand(OnRead);
        WriteCommand = new DelegateCommand(OnWrite);
        AddTestDataCommand = new DelegateCommand(OnAddTestData);
        RemoveTestDataCommand = new DelegateCommand(OnRemoveTestData, () => SelectedTestData != null);
        BatchReadCommand = new DelegateCommand(OnBatchRead);
        BatchWriteCommand = new DelegateCommand(OnBatchWrite);
        
        RemoveTestDataCommand.RaiseCanExecuteChanged();
    }
    
    private async void OnConnect()
    {
        try
        {
            var config = new ConnectionConfig
            {
                ProtocolType = ProtocolType.ModbusTcp,
                IpAddress = IpAddress,
                Port = Port,
                SlaveId = SlaveId
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
        await _testService.DisconnectAsync(ProtocolType.ModbusTcp);
        IsConnected = false;
        AddLog("已断开连接");
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
            
            var result = await _testService.ReadAsync(ProtocolType.ModbusTcp, Address, dataType, parameters);
            
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
            
            var result = await _testService.WriteAsync(ProtocolType.ModbusTcp, Address, value, dataType, parameters);
            
            AddLog(result.IsSuccess ? $"写入成功: {Address} = {value}" : $"写入失败: {result.ErrorMessage}");
        }
        catch (Exception ex)
        {
            AddLog($"写入异常: {ex.Message}");
        }
    }
    
    private void OnAddTestData()
    {
        TestDataList.Add(new ProtocolTestData
        {
            Address = Address,
            DataType = Enum.Parse<DataType>(SelectedDataType),
            Description = $"测试点位 {TestDataList.Count + 1}"
        });
    }
    
    private void OnRemoveTestData()
    {
        if (SelectedTestData != null)
        {
            TestDataList.Remove(SelectedTestData);
        }
    }
    
    private async void OnBatchRead()
    {
        if (TestDataList.Count == 0)
        {
            MessageBox.Show("请先添加测试数据");
            return;
        }
        
        var result = await _testService.BatchReadAsync(ProtocolType.ModbusTcp, TestDataList.ToList());
        
        foreach (var testResult in result.Results)
        {
            TestResults.Insert(0, testResult);
        }
        
        AddLog($"批量读取完成: 成功 {result.SuccessCount}, 失败 {result.FailCount}, 耗时 {result.TotalDuration}ms");
    }
    
    private async void OnBatchWrite()
    {
        if (TestDataList.Count == 0)
        {
            MessageBox.Show("请先添加测试数据");
            return;
        }
        
        var result = await _testService.BatchWriteAsync(ProtocolType.ModbusTcp, TestDataList.ToList());
        
        foreach (var testResult in result.Results)
        {
            TestResults.Insert(0, testResult);
        }
        
        AddLog($"批量写入完成: 成功 {result.SuccessCount}, 失败 {result.FailCount}, 耗时 {result.TotalDuration}ms");
    }
    
    private object ConvertValue(string valueStr, DataType dataType)
    {
        return dataType switch
        {
            DataType.bit => bool.Parse(valueStr),
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
