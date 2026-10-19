using System.Windows;

namespace Eario;

public partial class App : System.Windows.Application
{
    private TrayHost? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartupRegistration.EnsureEnabled();
        _tray = new TrayHost();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
