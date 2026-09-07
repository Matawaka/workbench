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
        var brandingReviewSmoke = e.Args.Any(arg => string.Equals(
            arg,
            BrandingReviewWindowV060.SmokeArgumentV060,
            StringComparison.OrdinalIgnoreCase));

        if (brandingReviewOnly && brandingReviewSmoke)
            throw new InvalidOperationException("Choose either branding review or branding review smoke, not both.");

        var brandingReviewRequested = brandingReviewOnly || brandingReviewSmoke;
        var selectedReviewModes = new[] { provenanceReviewOnly, capabilityEvidenceReviewOnly, brandingReviewRequested }.Count(value => value);
        if (selectedReviewModes > 1)
            throw new InvalidOperationException("Choose exactly one Workbench review-only mode.");

        if (brandingReviewRequested)
        {
            await RunIsolatedBrandingReviewV060Async(brandingReviewSmoke);
            return;
        }

        var semanticReviewOnly = provenanceReviewOnly || capabilityEvidenceReviewOnly;

        BrandingSplashWindowV060? splash = null;
        if (!semanticReviewOnly)
        {
            splash = new BrandingSplashWindowV060();
            splash.Show();
        }

        var window = new MainWindow();
        if (!semanticReviewOnly)
        {
            window.ConfigureV0562Routing();
        }

        if (!capabilityEvidenceReviewOnly)
        {
            window.ConfigureV0561ProvenanceReviewRouting(provenanceReviewOnly);
        }

        window.ConfigureV059CapabilityEvidenceReviewRouting(capabilityEvidenceReviewOnly);

        if (!semanticReviewOnly)
        {
            window.ConfigureV060Branding(reviewOnly: false);
        }

        MainWindow = window;
        window.Show();

        if (splash is not null)
        {
            await Task.Delay(900);
            splash.Close();
        }
    }

    private async Task RunIsolatedBrandingReviewV060Async(bool smoke)
    {
        BrandingSplashWindowV060? splash = null;
        try
        {
            splash = new BrandingSplashWindowV060();
            splash.Show();

            var reviewWindow = new BrandingReviewWindowV060(smoke, splash);
            MainWindow = reviewWindow;
            reviewWindow.Show();

            // Human review sees the actual branded splash; CI keeps it available long
            // enough for BrandingReviewWindowV060 to capture a real WPF render of it.
            await Task.Delay(smoke ? 1400 : 900);
            splash.Close();
        }
        catch (Exception ex)
        {
            try
            {
                splash?.Close();
            }
            catch
            {
                // Failure diagnostics must not mask the original startup exception.
            }

            BrandingReviewWindowV060.WriteStartupFailure(ex, smoke);

            if (!smoke)
            {
                MessageBox.Show(
                    "Matawaka Workbench v0.60 branding review could not start.\n\n" +
                    "A local diagnostic receipt was written to:\n" +
                    BrandingReviewWindowV060.FailureReceiptPathV060,
                    "Matawaka Workbench — branding review startup failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            Shutdown(-1);
        }
    }
}

internal static class V048StringCompatibilityExtensions
{
    public static bool EndsWith(this string value, char suffix, StringComparison comparisonType)
        => value.EndsWith(suffix.ToString(), comparisonType);
}
