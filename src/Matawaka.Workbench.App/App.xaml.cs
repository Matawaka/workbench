using System.Windows;

namespace Matawaka.Workbench.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        window.ConfigureV0516Routing();
        window.ConfigureV0516AcceptanceRouting();
        MainWindow = window;
        window.Show();
    }
}

internal static class V048StringCompatibilityExtensions
{
    public static bool EndsWith(this string value, char suffix, StringComparison comparisonType)
        => value.EndsWith(suffix.ToString(), comparisonType);
}
