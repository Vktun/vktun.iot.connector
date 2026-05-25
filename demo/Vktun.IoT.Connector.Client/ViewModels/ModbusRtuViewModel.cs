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
using ClientModbusRegisterType = Vktun.IoT.Connector.Client.Models.ModbusRegisterType;
using ClientParity = Vktun.IoT.Connector.Client.Models.Parity;
using ClientStopBits = Vktun.IoT.Connector.Client.Models.StopBits;

namespace Vktun.IoT.Connector.Client.ViewModels;

public class ModbusRtuViewModel : BindableBase, INavigationAware
{
    private readonly IProtocolTestService _testService;
    private readonly StringBuilder _logBuilder = new();
    private CancellationTokenSource? _pollingCts;

    private string _selectedPortName = "COM1";
    private int _selectedBaudRate = 9600;
    private int _selectedDataBits = 8;
    private string _selectedStopBits = "One";
    private string _selectedParity = "None";
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
        RegisterTypes = new ObservableCollection<string> { "Coil", "DiscreteInput", "InputRegister", "HoldingRegister" };
        DataTypes = new ObservableCollection<string>
        {
            "Bool", "Int8", "UInt8", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Float", "Double"
        };

        TestDataList = new ObservableCollection<ProtocolTestData>();
        TestResults = new ObservableCollection<DeviceTestResult>();

        OpenCommand = new DelegateCommand(OnOpen, () => !IsConnected);
        CloseCommand = new DelegateCommand(OnClose, () => IsConnected);
        ReadCommand = new DelegateCommand(OnRead, () => IsConnected);
        WriteCommand = new DelegateCommand(OnWrite, () => IsConnected);
        StartPollingCommand = new DelegateCommand(OnStartPolling, () => IsConnected && !IsPolling);
        StopPollingCommand = new DelegateCommand(OnStopPolling, () => IsPolling);
        AddTestDataCommand = new DelegateCommand(OnAddTestData);
        RemoveTestDataCommand = new DelegateCommand(OnRemoveTestData, () => SelectedTestData != null);
        BatchReadCommand = new DelegateCommand(OnBatchRead, () => IsConnected);
        BatchWriteCommand = new DelegateCommand(OnBatchWrite, () => IsConnected);
    }

    public ObservableCollection<string> PortNames { get; }
    public ObservableCollection<int> BaudRates { get; }
    public ObservableCollection<int> DataBitsList { get; }
    public ObservableCollection<string> StopBitsList { get; }
    public ObservableCollection<string> ParityList { get; }
    public ObservableCollection<string> RegisterTypes { get; }
    public ObservableCollection<string> DataTypes { get; }
    public ObservableCollection<ProtocolTestData> TestDataList { get; }
    public ObservableCollection<DeviceTestResult> TestResults { get; }

    public string SelectedPortName
    {
        get => _selectedPortName;
        set => SetProperty(ref _selectedPortName, value);
    }

    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set => SetProperty(ref _selectedBaudRate, value);
    }

    public int SelectedDataBits
    {
        get => _selectedDataBits;
        set => SetProperty(ref _selectedDataBits, value);
    }

    public string SelectedStopBits
    {
        get => _selectedStopBits;
        set => SetProperty(ref _selectedStopBits, value);
    }

    public string SelectedParity
    {
        get => _selectedParity;
        set => SetProperty(ref _selectedParity, value);
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

            OpenCommand.RaiseCanExecuteChanged();
            CloseCommand.RaiseCanExecuteChanged();
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

    public DelegateCommand OpenCommand { get; }
    public DelegateCommand CloseCommand { get; }
    public DelegateCommand ReadCommand { get; }
    public DelegateCommand WriteCommand { get; }
    public DelegateCommand StartPollingCommand { get; }
    public DelegateCommand StopPollingCommand { get; }
    public DelegateCommand AddTestDataCommand { get; }
    public DelegateCommand RemoveTestDataCommand { get; }
    public DelegateCommand BatchReadCommand { get; }
    public DelegateCommand BatchWriteCommand { get; }

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
                StopBits = Enum.Parse<ClientStopBits>(SelectedStopBits),
                Parity = Enum.Parse<ClientParity>(SelectedParity),
                SlaveId = SlaveId
            };

            IsConnected = await _testService.ConnectAsync(config);
            AddLog(IsConnected ? "Opened" : "Open failed");
        }
        catch (Exception ex)
        {
            AddLog($"Open error: {ex.Message}");
        }
    }

    private async void OnClose()
    {
        OnStopPolling();
        await _testService.DisconnectAsync(ProtocolType.ModbusRtu);
        IsConnected = false;
        AddLog("Closed");
    }

    private async void OnRead()
    {
        try
        {
            var result = await _testService.ReadAsync(
                ProtocolType.ModbusRtu,
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
            var result = await _testService.WriteAsync(ProtocolType.ModbusRtu, Address, value, dataType, CreateParameters());

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
                ProtocolType.ModbusRtu,
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
        var dataType = Enum.Parse<DataType>(SelectedDataType);
        TestDataList.Add(new ModbusTestData
        {
            Address = Address,
            DataType = dataType,
            RegisterType = Enum.Parse<ClientModbusRegisterType>(SelectedRegisterType),
            Quantity = ParseQuantity(),
            Value = string.IsNullOrWhiteSpace(WriteValue) ? null : ConvertValue(WriteValue, dataType),
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

        var result = await _testService.BatchReadAsync(ProtocolType.ModbusRtu, TestDataList.ToList());
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

        var result = await _testService.BatchWriteAsync(ProtocolType.ModbusRtu, TestDataList.ToList());
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
