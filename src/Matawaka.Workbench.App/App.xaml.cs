using System.Windows;

namespace Matawaka.Workbench.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
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

        if (provenanceReviewOnly && capabilityEvidenceReviewOnly)
            throw new InvalidOperationException("Choose exactly one Workbench review-only mode.");

        var anyReviewOnly = provenanceReviewOnly || capabilityEvidenceReviewOnly;
        var window = new MainWindow();
        if (!anyReviewOnly)
        {
            window.ConfigureV0562Routing();
        }

        if (!capabilityEvidenceReviewOnly)
        {
            window.ConfigureV0561ProvenanceReviewRouting(provenanceReviewOnly);
        }

        window.ConfigureV059CapabilityEvidenceReviewRouting(capabilityEvidenceReviewOnly);
        MainWindow = window;
        window.Show();
    }
}

internal static class V048StringCompatibilityExtensions
{
    public static bool EndsWith(this string value, char suffix, StringComparison comparisonType)
        => value.EndsWith(suffix.ToString(), comparisonType);
}
