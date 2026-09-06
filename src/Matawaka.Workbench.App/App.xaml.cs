using System.Windows;

namespace Matawaka.Workbench.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var provenanceReviewOnly = e.Args.Any(arg => string.Equals(
            arg,
            global::Matawaka.Workbench.App.MainWindow.ProvenanceReviewOnlyArgumentV0561,
            StringComparison.OrdinalIgnoreCase));
        var capabilityEvidenceReviewOnly = e.Args.Any(arg => string.Equals(
            arg,
            global::Matawaka.Workbench.App.MainWindow.CapabilityEvidenceReviewOnlyArgumentV059,
            StringComparison.OrdinalIgnoreCase));
        var brandingReviewOnly = e.Args.Any(arg => string.Equals(
            arg,
            global::Matawaka.Workbench.App.MainWindow.BrandingReviewOnlyArgumentV060,
            StringComparison.OrdinalIgnoreCase));

        var selectedReviewModes = new[] { provenanceReviewOnly, capabilityEvidenceReviewOnly, brandingReviewOnly }.Count(value => value);
        if (selectedReviewModes > 1)
            throw new InvalidOperationException("Choose exactly one Workbench review-only mode.");

        var semanticReviewOnly = provenanceReviewOnly || capabilityEvidenceReviewOnly;
        var anyReviewOnly = semanticReviewOnly || brandingReviewOnly;

        SplashScreen? splash = null;
        if (!semanticReviewOnly)
        {
            splash = new SplashScreen("Assets/Branding/splash-v060.jpg");
            splash.Show(autoClose: false);
        }

        var window = new MainWindow();
        if (!anyReviewOnly)
        {
            window.ConfigureV0562Routing();
        }

        if (!brandingReviewOnly)
        {
            if (!capabilityEvidenceReviewOnly)
            {
                window.ConfigureV0561ProvenanceReviewRouting(provenanceReviewOnly);
            }

            window.ConfigureV059CapabilityEvidenceReviewRouting(capabilityEvidenceReviewOnly);
        }

        if (!semanticReviewOnly)
        {
            window.ConfigureV060Branding(brandingReviewOnly);
        }

        MainWindow = window;
        window.Show();

        if (splash is not null)
        {
            await Task.Delay(650);
            splash.Close(TimeSpan.FromMilliseconds(180));
        }
    }
}

internal static class V048StringCompatibilityExtensions
{
    public static bool EndsWith(this string value, char suffix, StringComparison comparisonType)
        => value.EndsWith(suffix.ToString(), comparisonType);
}
