using Prism.DryIoc;
using Prism.Ioc;
using System.Windows;
using Vktun.IoT.Connector.Client.Services;
using Vktun.IoT.Connector.Client.Views;

namespace Vktun.IoT.Connector.Client;

public class Bootstrapper : PrismBootstrapper
{
    protected override Window CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<IProtocolTestService, ProtocolTestService>();
        containerRegistry.RegisterSingleton<IConnectionService, ConnectionService>();
        containerRegistry.RegisterSingleton<ISocketTestService, SocketTestService>();
        
        containerRegistry.RegisterForNavigation<ModbusTcpView>();
        containerRegistry.RegisterForNavigation<ModbusRtuView>();
        containerRegistry.RegisterForNavigation<SiemensView>();
        containerRegistry.RegisterForNavigation<MitsubishiView>();
        containerRegistry.RegisterForNavigation<OmronView>();
        containerRegistry.RegisterForNavigation<SerialPortView>();
        containerRegistry.RegisterForNavigation<SocketDebugView>();
    }
}
