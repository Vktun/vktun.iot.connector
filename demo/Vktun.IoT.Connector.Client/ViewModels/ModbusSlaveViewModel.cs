using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Client.ViewModels;

public class ModbusSlaveDataRow
{
    public string Area { get; set; } = string.Empty;
    public int Address { get; set; }
    public string Value { get; set; } = string.Empty;
}

public class ModbusSlaveTrafficRow
{
    public DateTime Timestamp { get; set; }
    public string ClientEndPoint { get; set; } = string.Empty;
    public byte UnitId { get; set; }
    public string FunctionCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Request { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public class ModbusSlaveViewModel : BindableBase, INavigationAware
{
    private readonly IModbusSlaveServer _slaveServer;
    private readonly StringBuilder _logBuilder = new();
    private ModbusSlaveDataStore? _dataStore;
    private string _listenAddress = "127.0.0.1";
    private int _port = 1502;
    private byte _slaveId = 1;
    private int _coilCount = 64;
    private int _discreteInputCount = 64;
    private int _inputRegisterCount = 64;
    private int _holdingRegisterCount = 64;
    private string _selectedArea = nameof(ModbusSlaveRegisterArea.HoldingRegisters);
    private string _editAddress = "0";
    private string _editValue = "0";
    private bool _isRunning;
    private string _logMessages = string.Empty;

    public ModbusSlaveViewModel(IModbusSlaveServer slaveServer)
    {
        _slaveServer = slaveServer;
        _slaveServer.TrafficReceived += OnTrafficReceived;

        Areas = new ObservableCollection<string>
        {
            nameof(ModbusSlaveRegisterArea.Coils),
            nameof(ModbusSlaveRegisterArea.DiscreteInputs),
            nameof(ModbusSlaveRegisterArea.InputRegisters),
            nameof(ModbusSlaveRegisterArea.HoldingRegisters)
        };
        DataRows = new ObservableCollection<ModbusSlaveDataRow>();
        TrafficRows = new ObservableCollection<ModbusSlaveTrafficRow>();

        StartCommand = new DelegateCommand(OnStart, () => !IsRunning);
        StopCommand = new DelegateCommand(OnStop, () => IsRunning);
        SetValueCommand = new DelegateCommand(OnSetValue);
        RefreshCommand = new DelegateCommand(RefreshDataRows);

        RefreshDataRows();
    }

    public string ListenAddress
    {
        get => _listenAddress;
        set => SetProperty(ref _listenAddress, value);
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

    public int CoilCount
    {
        get => _coilCount;
        set => SetProperty(ref _coilCount, value);
    }

    public int DiscreteInputCount
    {
        get => _discreteInputCount;
        set => SetProperty(ref _discreteInputCount, value);
    }

    public int InputRegisterCount
    {
        get => _inputRegisterCount;
        set => SetProperty(ref _inputRegisterCount, value);
    }

    public int HoldingRegisterCount
    {
        get => _holdingRegisterCount;
        set => SetProperty(ref _holdingRegisterCount, value);
    }

    public string SelectedArea
    {
        get => _selectedArea;
        set
        {
            if (SetProperty(ref _selectedArea, value))
            {
                RefreshDataRows();
            }
        }
    }

    public string EditAddress
    {
        get => _editAddress;
        set => SetProperty(ref _editAddress, value);
    }

    public string EditValue
    {
        get => _editValue;
        set => SetProperty(ref _editValue, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (!SetProperty(ref _isRunning, value))
            {
                return;
            }

            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }
    }

    public string LogMessages
    {
        get => _logMessages;
        set => SetProperty(ref _logMessages, value);
    }

    public ObservableCollection<string> Areas { get; }
    public ObservableCollection<ModbusSlaveDataRow> DataRows { get; }
    public ObservableCollection<ModbusSlaveTrafficRow> TrafficRows { get; }
    public DelegateCommand StartCommand { get; }
    public DelegateCommand StopCommand { get; }
    public DelegateCommand SetValueCommand { get; }
    public DelegateCommand RefreshCommand { get; }

    private async void OnStart()
    {
        try
        {
            var store = EnsureDataStore(recreate: true);
            var result = await _slaveServer.StartAsync(new ModbusSlaveOptions
            {
                ListenAddress = ListenAddress,
                Port = Port,
                SlaveId = SlaveId,
                CoilCount = CoilCount,
                DiscreteInputCount = DiscreteInputCount,
                InputRegisterCount = InputRegisterCount,
                HoldingRegisterCount = HoldingRegisterCount
            }, store);

            IsRunning = result.Success;
            AddLog(result.Success
                ? $"Started Modbus TCP slave at {result.LocalEndPoint} slaveId={SlaveId}"
                : $"Start failed: {result.ErrorMessage}");
            RefreshDataRows();
        }
        catch (Exception ex)
        {
            AddLog($"Start error: {ex.Message}");
        }
    }

    private async void OnStop()
    {
        var result = await _slaveServer.StopAsync();
        IsRunning = false;
        AddLog(result.Success ? "Stopped Modbus TCP slave" : $"Stop failed: {result.ErrorMessage}");
    }

    private void OnSetValue()
    {
        try
        {
            var store = EnsureDataStore();
            var area = Enum.Parse<ModbusSlaveRegisterArea>(SelectedArea);
            var address = ushort.Parse(EditAddress);

            switch (area)
            {
                case ModbusSlaveRegisterArea.Coils:
                    store.SetCoil(address, ParseBool(EditValue));
                    break;
                case ModbusSlaveRegisterArea.DiscreteInputs:
                    store.SetDiscreteInput(address, ParseBool(EditValue));
                    break;
                case ModbusSlaveRegisterArea.InputRegisters:
                    store.SetInputRegister(address, ushort.Parse(EditValue));
                    break;
                case ModbusSlaveRegisterArea.HoldingRegisters:
                    store.SetHoldingRegister(address, ushort.Parse(EditValue));
                    break;
            }

            AddLog($"Set {SelectedArea}[{address}] = {EditValue}");
            RefreshDataRows();
        }
        catch (Exception ex)
        {
            AddLog($"Set value failed: {ex.Message}");
        }
    }

    private ModbusSlaveDataStore EnsureDataStore(bool recreate = false)
    {
        if (_dataStore == null || recreate)
        {
            _dataStore = new ModbusSlaveDataStore(
                Math.Max(1, CoilCount),
                Math.Max(1, DiscreteInputCount),
                Math.Max(1, InputRegisterCount),
                Math.Max(1, HoldingRegisterCount));
        }

        return _dataStore;
    }

    private void RefreshDataRows()
    {
        var store = IsRunning ? _slaveServer.DataStore : EnsureDataStore();
        var area = Enum.Parse<ModbusSlaveRegisterArea>(SelectedArea);
        DataRows.Clear();

        var count = Math.Min(GetAreaCount(store, area), 128);
        for (var address = 0; address < count; address++)
        {
            DataRows.Add(new ModbusSlaveDataRow
            {
                Area = SelectedArea,
                Address = address,
                Value = ReadValue(store, area, (ushort)address)
            });
        }
    }

    private void OnTrafficReceived(object? sender, ModbusSlaveTrafficEntry entry)
    {
        RunOnUiThread(() =>
        {
            TrafficRows.Insert(0, new ModbusSlaveTrafficRow
            {
                Timestamp = entry.Timestamp,
                ClientEndPoint = entry.ClientEndPoint,
                UnitId = entry.UnitId,
                FunctionCode = $"0x{entry.FunctionCode:X2}",
                Status = entry.Success ? "OK" : "Fail",
                Request = ToHex(entry.RequestFrame),
                Response = ToHex(entry.ResponseFrame),
                Error = entry.ErrorMessage ?? string.Empty
            });

            while (TrafficRows.Count > 500)
            {
                TrafficRows.RemoveAt(TrafficRows.Count - 1);
            }

            RefreshDataRows();
        });
    }

    private static int GetAreaCount(ModbusSlaveDataStore store, ModbusSlaveRegisterArea area)
    {
        return area switch
        {
            ModbusSlaveRegisterArea.Coils => store.CoilCount,
            ModbusSlaveRegisterArea.DiscreteInputs => store.DiscreteInputCount,
            ModbusSlaveRegisterArea.InputRegisters => store.InputRegisterCount,
            ModbusSlaveRegisterArea.HoldingRegisters => store.HoldingRegisterCount,
            _ => 0
        };
    }

    private static string ReadValue(ModbusSlaveDataStore store, ModbusSlaveRegisterArea area, ushort address)
    {
        return area switch
        {
            ModbusSlaveRegisterArea.Coils => store.GetCoil(address).ToString(),
            ModbusSlaveRegisterArea.DiscreteInputs => store.GetDiscreteInput(address).ToString(),
            ModbusSlaveRegisterArea.InputRegisters => store.GetInputRegister(address).ToString(),
            ModbusSlaveRegisterArea.HoldingRegisters => store.GetHoldingRegister(address).ToString(),
            _ => string.Empty
        };
    }

    private static bool ParseBool(string value)
    {
        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToHex(byte[] frame)
    {
        return frame.Length == 0 ? string.Empty : BitConverter.ToString(frame).Replace("-", " ");
    }

    private void AddLog(string message)
    {
        _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        LogMessages = _logBuilder.ToString();
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
        IsRunning = _slaveServer.IsRunning;
        RefreshDataRows();
    }

    public bool IsNavigationTarget(NavigationContext navigationContext) => true;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }
}
