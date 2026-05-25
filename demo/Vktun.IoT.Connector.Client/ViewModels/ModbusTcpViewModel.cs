using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using Vktun.IoT.Connector.Client.Models;
using Vktun.IoT.Connector.Client.Services;
using Vktun.IoT.Connector.Core.Enums;
using ClientModbusRegisterType = Vktun.IoT.Connector.Client.Models.ModbusRegisterType;

namespace Vktun.IoT.Connector.Client.ViewModels;

public class ModbusTcpViewModel : BindableBase, INavigationAware
{
    private readonly IProtocolTestService _testService;
    private readonly StringBuilder _logBuilder = new();
    private CancellationTokenSource? _pollingCts;

    private string _ipAddress = "127.0.0.1";
    private int _port = 502;
    private byte _slaveId = 1;
    private string _address = "0";
    private string _quantity = "1";
    private string _writeValue = string.Empty;
    private string _logMessages = string.Empty;
    private bool _isConnected;
    private bool _isPolling;
    private int _pollInterval = 1000;
    private ProtocolTestData? _selectedTestData;
    private string _selectedRegisterType = "HoldingRegister";
    private string _selectedDataType = "UInt16";

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
        ReadCommand = new DelegateCommand(OnRead, () => IsConnected);
        WriteCommand = new DelegateCommand(OnWrite, () => IsConnected);
        StartPollingCommand = new DelegateCommand(OnStartPolling, () => IsConnected && !IsPolling);
        StopPollingCommand = new DelegateCommand(OnStopPolling, () => IsPolling);
        AddTestDataCommand = new DelegateCommand(OnAddTestData);
        RemoveTestDataCommand = new DelegateCommand(OnRemoveTestData, () => SelectedTestData != null);
        BatchReadCommand = new DelegateCommand(OnBatchRead, () => IsConnected);
        BatchWriteCommand = new DelegateCommand(OnBatchWrite, () => IsConnected);
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

    public byte SlaveId
    {
        get => _slaveId;
        set => SetProperty(ref _slaveId, value);
    }

    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public string Quantity
    {
        get => _quantity;
        set => SetProperty(ref _quantity, value);
    }

    public string WriteValue
    {
        get => _writeValue;
        set => SetProperty(ref _writeValue, value);
    }

    public string LogMessages
    {
        get => _logMessages;
        set => SetProperty(ref _logMessages, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (!SetProperty(ref _isConnected, value))
            {
                return;
            }

            ConnectCommand.RaiseCanExecuteChanged();
            DisconnectCommand.RaiseCanExecuteChanged();
            ReadCommand.RaiseCanExecuteChanged();
            WriteCommand.RaiseCanExecuteChanged();
            StartPollingCommand.RaiseCanExecuteChanged();
            BatchReadCommand.RaiseCanExecuteChanged();
            BatchWriteCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsPolling
    {
        get => _isPolling;
        set
        {
            if (!SetProperty(ref _isPolling, value))
            {
                return;
            }

            StartPollingCommand.RaiseCanExecuteChanged();
            StopPollingCommand.RaiseCanExecuteChanged();
        }
    }

    public int PollInterval
    {
        get => _pollInterval;
        set => SetProperty(ref _pollInterval, value);
    }

    public ProtocolTestData? SelectedTestData
    {
        get => _selectedTestData;
        set
        {
            SetProperty(ref _selectedTestData, value);
            RemoveTestDataCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<string> RegisterTypes { get; }
    public ObservableCollection<string> DataTypes { get; }
    public ObservableCollection<ProtocolTestData> TestDataList { get; }
    public ObservableCollection<DeviceTestResult> TestResults { get; }

    public string SelectedRegisterType
    {
        get => _selectedRegisterType;
        set => SetProperty(ref _selectedRegisterType, value);
    }

    public string SelectedDataType
    {
        get => _selectedDataType;
        set => SetProperty(ref _selectedDataType, value);
    }

    public DelegateCommand ConnectCommand { get; }
    public DelegateCommand DisconnectCommand { get; }
    public DelegateCommand ReadCommand { get; }
    public DelegateCommand WriteCommand { get; }
    public DelegateCommand StartPollingCommand { get; }
    public DelegateCommand StopPollingCommand { get; }
    public DelegateCommand AddTestDataCommand { get; }
    public DelegateCommand RemoveTestDataCommand { get; }
    public DelegateCommand BatchReadCommand { get; }
    public DelegateCommand BatchWriteCommand { get; }

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
            AddLog(IsConnected ? "Connected" : "Connect failed");
        }
        catch (Exception ex)
        {
            AddLog($"Connect error: {ex.Message}");
        }
    }

    private async void OnDisconnect()
    {
        OnStopPolling();
        await _testService.DisconnectAsync(ProtocolType.ModbusTcp);
        IsConnected = false;
        AddLog("Disconnected");
    }

    private async void OnRead()
    {
        try
        {
            var result = await _testService.ReadAsync(
                ProtocolType.ModbusTcp,
                Address,
                Enum.Parse<DataType>(SelectedDataType),
                CreateParameters());

            AddResult(result);
        }
        catch (Exception ex)
        {
            AddLog($"Read error: {ex.Message}");
        }
    }

    private async void OnWrite()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(WriteValue))
            {
                MessageBox.Show("Enter a write value.");
                return;
            }

            var dataType = Enum.Parse<DataType>(SelectedDataType);
            var value = ConvertValue(WriteValue, dataType);
            var result = await _testService.WriteAsync(ProtocolType.ModbusTcp, Address, value, dataType, CreateParameters());

            AddResult(result);
        }
        catch (Exception ex)
        {
            AddLog($"Write error: {ex.Message}");
        }
    }

    private async void OnStartPolling()
    {
        if (IsPolling)
        {
            return;
        }

        _pollingCts = new CancellationTokenSource();
        IsPolling = true;
        AddLog("Polling started");

        try
        {
            await foreach (var result in _testService.PollAsync(
                ProtocolType.ModbusTcp,
                Address,
                Enum.Parse<DataType>(SelectedDataType),
                PollInterval,
                CreateParameters(),
                _pollingCts.Token))
            {
                AddResult(result);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AddLog($"Polling error: {ex.Message}");
        }
        finally
        {
            IsPolling = false;
            _pollingCts?.Dispose();
            _pollingCts = null;
            AddLog("Polling stopped");
        }
    }

    private void OnStopPolling()
    {
        _pollingCts?.Cancel();
    }

    private void OnAddTestData()
    {
        TestDataList.Add(new ModbusTestData
        {
            Address = Address,
            DataType = Enum.Parse<DataType>(SelectedDataType),
            RegisterType = Enum.Parse<ClientModbusRegisterType>(SelectedRegisterType),
            Quantity = ParseQuantity(),
            Value = string.IsNullOrWhiteSpace(WriteValue) ? null : ConvertValue(WriteValue, Enum.Parse<DataType>(SelectedDataType)),
            Description = $"Point {TestDataList.Count + 1}"
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
            MessageBox.Show("Add test data first.");
            return;
        }

        var result = await _testService.BatchReadAsync(ProtocolType.ModbusTcp, TestDataList.ToList());
        foreach (var testResult in result.Results)
        {
            AddResult(testResult);
        }

        AddLog($"Batch read done: OK={result.SuccessCount}, Fail={result.FailCount}, {result.TotalDuration}ms");
    }

    private async void OnBatchWrite()
    {
        if (TestDataList.Count == 0)
        {
            MessageBox.Show("Add test data first.");
            return;
        }

        var result = await _testService.BatchWriteAsync(ProtocolType.ModbusTcp, TestDataList.ToList());
        foreach (var testResult in result.Results)
        {
            AddResult(testResult);
        }

        AddLog($"Batch write done: OK={result.SuccessCount}, Fail={result.FailCount}, {result.TotalDuration}ms");
    }

    private Dictionary<string, object> CreateParameters()
    {
        return new Dictionary<string, object>
        {
            ["SlaveId"] = SlaveId,
            ["RegisterType"] = SelectedRegisterType,
            ["Quantity"] = ParseQuantity()
        };
    }

    private ushort ParseQuantity()
    {
        return ushort.TryParse(Quantity, out var parsed) && parsed > 0 ? parsed : (ushort)1;
    }

    private static object ConvertValue(string valueText, DataType dataType)
    {
        if (valueText.Contains(',', StringComparison.Ordinal))
        {
            return valueText;
        }

        return dataType switch
        {
            DataType.Bool or DataType.Bit => bool.Parse(valueText),
            DataType.Int8 => sbyte.Parse(valueText),
            DataType.UInt8 => byte.Parse(valueText),
            DataType.Int16 => short.Parse(valueText),
            DataType.UInt16 => ushort.Parse(valueText),
            DataType.Int32 => int.Parse(valueText),
            DataType.UInt32 => uint.Parse(valueText),
            DataType.Int64 => long.Parse(valueText),
            DataType.UInt64 => ulong.Parse(valueText),
            DataType.Float => float.Parse(valueText),
            DataType.Double => double.Parse(valueText),
            _ => valueText
        };
    }

    private void AddResult(DeviceTestResult result)
    {
        TestResults.Insert(0, result);
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
        OnStopPolling();
    }
}
