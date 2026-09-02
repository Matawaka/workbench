using System.Windows;

namespace Matawaka.Workbench.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        window.ConfigureV043Routing();
        MainWindow = window;
        window.Show();
    }
}
