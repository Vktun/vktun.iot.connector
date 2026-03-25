using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using Vktun.IoT.Connector.Core.Enums;

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
    
    private string _statusMessage = "就绪";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
    
    private DateTime _currentTime = DateTime.Now;
    public DateTime CurrentTime
    {
        get => _currentTime;
        set => SetProperty(ref _currentTime, value);
    }
    
    private bool _isConnected;
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
    
    public string ConnectionStatusText => IsConnected ? "已连接" : "未连接";
    
    public ObservableCollection<MenuItem> MenuItems { get; }
    
    public MainWindowViewModel(IRegionManager regionManager)
    {
        _regionManager = regionManager;
        
        MenuItems = new ObservableCollection<MenuItem>
        {
            new MenuItem { Title = "Modbus TCP", Icon = "🔌", ViewName = "ModbusTcpView" },
            new MenuItem { Title = "Modbus RTU", Icon = "🔌", ViewName = "ModbusRtuView" },
            new MenuItem { Title = "西门子 S7", Icon = "🏭", ViewName = "SiemensView" },
            new MenuItem { Title = "三菱 PLC", Icon = "🏭", ViewName = "MitsubishiView" },
            new MenuItem { Title = "欧姆龙 PLC", Icon = "🏭", ViewName = "OmronView" },
            new MenuItem { Title = "串口调试", Icon = "📡", ViewName = "SerialPortView" }
        };
        
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (s, e) => CurrentTime = DateTime.Now;
        _timer.Start();
        
        SelectedMenuItem = MenuItems[0];
    }
    
    private void NavigateTo(string viewName)
    {
        _regionManager.RequestNavigate("ContentRegion", viewName);
    }
}
