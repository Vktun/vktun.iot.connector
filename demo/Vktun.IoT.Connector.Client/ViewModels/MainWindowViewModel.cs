using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace Vktun.IoT.Connector.Client.ViewModels;

public class MenuItem
{
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ViewName { get; set; } = string.Empty;
}

public class MainWindowViewModel : BindableBase
{
    private readonly IRegionManager _regionManager;
    private readonly DispatcherTimer _timer;
    private MenuItem? _selectedMenuItem;
    private string _statusMessage = "Ready";
    private DateTime _currentTime = DateTime.Now;
    private bool _isConnected;

    public MainWindowViewModel(IRegionManager regionManager)
    {
        _regionManager = regionManager;

        MenuItems = new ObservableCollection<MenuItem>
        {
            new() { Title = "Modbus TCP", Icon = "M", ViewName = "ModbusTcpView" },
            new() { Title = "Modbus RTU", Icon = "M", ViewName = "ModbusRtuView" },
            new() { Title = "Modbus Slave", Icon = "S", ViewName = "ModbusSlaveView" },
            new() { Title = "Siemens S7", Icon = "P", ViewName = "SiemensView" },
            new() { Title = "Mitsubishi PLC", Icon = "P", ViewName = "MitsubishiView" },
            new() { Title = "Omron PLC", Icon = "P", ViewName = "OmronView" },
            new() { Title = "Serial Port", Icon = "C", ViewName = "SerialPortView" },
            new() { Title = "Socket Debug", Icon = "S", ViewName = "SocketDebugView" }
        };

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => CurrentTime = DateTime.Now;
        _timer.Start();

        SelectedMenuItem = MenuItems[0];
    }

    public MenuItem? SelectedMenuItem
    {
        get => _selectedMenuItem;
        set
        {
            SetProperty(ref _selectedMenuItem, value);
            if (value != null)
            {
                NavigateTo(value.ViewName);
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public DateTime CurrentTime
    {
        get => _currentTime;
        set => SetProperty(ref _currentTime, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            SetProperty(ref _isConnected, value);
            RaisePropertyChanged(nameof(ConnectionStatusColor));
            RaisePropertyChanged(nameof(ConnectionStatusText));
        }
    }

    public Brush ConnectionStatusColor => IsConnected
        ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
        : new SolidColorBrush(Color.FromRgb(244, 67, 54));

    public string ConnectionStatusText => IsConnected ? "Connected" : "Disconnected";

    public ObservableCollection<MenuItem> MenuItems { get; }

    private void NavigateTo(string viewName)
    {
        _regionManager.RequestNavigate("ContentRegion", viewName);
    }
}
