using Prism.DryIoc;
using Prism.Ioc;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Vktun.IoT.Connector.Client.Services;
using Vktun.IoT.Connector.Client.Views;
using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.Client;

public class Bootstrapper : PrismBootstrapper
{
    protected override Window CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        var services = new ServiceCollection();
        services.AddVktunIoTConnector();
        var sdkProvider = services.BuildServiceProvider();

        containerRegistry.RegisterInstance<IServiceProvider>(sdkProvider);
        containerRegistry.RegisterInstance(sdkProvider.GetRequiredService<IModbusClient>());
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
