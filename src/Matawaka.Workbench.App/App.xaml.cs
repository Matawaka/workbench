using System.Windows;

namespace Matawaka.Workbench.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var reviewOnly = e.Args.Any(arg => string.Equals(
            arg,
            MainWindow.ProvenanceReviewOnlyArgumentV0561,
            StringComparison.OrdinalIgnoreCase));

        var window = new MainWindow();
        if (!reviewOnly)
        {
            window.ConfigureV0552Routing();
            window.ConfigureV0552AcceptanceRouting();
        }

        window.ConfigureV0561ProvenanceReviewRouting(reviewOnly);
        MainWindow = window;
        window.Show();
    }
}

internal static class V048StringCompatibilityExtensions
{
    public static bool EndsWith(this string value, char suffix, StringComparison comparisonType)
        => value.EndsWith(suffix.ToString(), comparisonType);
}
