using System.Windows;

namespace Vktun.IoT.Connector.Client;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var bootstrapper = new Bootstrapper();
        bootstrapper.Run();
    }
}
