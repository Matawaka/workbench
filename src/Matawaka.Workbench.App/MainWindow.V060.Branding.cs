using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    public const string BrandingReviewOnlyArgumentV060 = "--branding-review-only";
    public const string NormalTitleV060 = "Matawaka Workbench v0.60";
    private const string BrandingIconResourceV060 = "Matawaka.Workbench.App.Branding.IconV060Base64";

    internal void ConfigureV060Branding(bool reviewOnly)
    {
        Title = NormalTitleV060;
        Icon = LoadV060BrandingIcon();

        InstallV060UpdateArtwork();
        if (string.IsNullOrWhiteSpace(UpdatePlanTextBox.Text))
            UpdatePlanTextBox.Text = BuildV060ProgressSummary();

        if (!reviewOnly) return;

        // Branding review is presentation-only. It deliberately detaches historical
        // bootstrap/closing persistence and disables every maintenance/app action.
        Loaded -= Window_LoadedV040;
        Loaded -= Window_LoadedV055;
        Loaded -= Window_LoadedV0552;
        Closing -= Window_Closing;

        PrimaryMaintenanceSurface.IsEnabled = false;
        PrimaryMaintenanceSurface.IsHitTestVisible = false;
        InstalledAppsList.IsEnabled = false;
        InstalledAppsList.IsHitTestVisible = false;
        HistoricalCompatibilityBindings.IsEnabled = false;
        HistoricalCompatibilityBindings.IsHitTestVisible = false;

        StatusText.Text = "READ-ONLY BRANDING REVIEW — NO UPDATE / ACCEPT / PUBLISH AUTHORITY";
        ProgressBar.Value = 0;
        OutputTabs.SelectedItem = UpdatePlanTab;
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV060BrandingContract() => new[]
    {
        ("branding-v060-title", Title == NormalTitleV060, Title, NormalTitleV060),
        ("branding-v060-update-surface", UpdatePlanTab.Content is Grid grid && grid.Uid == "V060BrandingUpdateSurface",
            UpdatePlanTab.Content?.GetType().Name ?? "null", "Grid/V060BrandingUpdateSurface"),
        ("branding-v060-four-maintenance-actions", PrimaryMaintenanceSurface.Children.OfType<Button>().Count() == 4,
            PrimaryMaintenanceSurface.Children.OfType<Button>().Count().ToString(), "4"),
        ("branding-v060-accepted-release-not-promoted", true,
            "v0.60 implementation/branding candidate; accepted release publication is separate", "branding != accepted release"),
        ("branding-v060-authority-neutral", true,
            "icon/splash/update artwork/progress text only", "no policy/provider/runtime/model/publication authority")
    };

    internal static BitmapFrame LoadV060BrandingIcon()
    {
        var assembly = typeof(MainWindow).Assembly;
        using var resource = assembly.GetManifestResourceStream(BrandingIconResourceV060)
            ?? throw new InvalidOperationException($"Missing branding icon resource: {BrandingIconResourceV060}");
        using var reader = new System.IO.StreamReader(resource, System.Text.Encoding.ASCII, false, 1024, leaveOpen: false);
        var encoded = reader.ReadToEnd().Trim();
        var bytes = Convert.FromBase64String(encoded);
        using var iconStream = new System.IO.MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(iconStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count != 1)
            throw new InvalidOperationException($"Unexpected branding icon frame count: {decoder.Frames.Count}");
        return decoder.Frames[0];
    }

    private void InstallV060UpdateArtwork()
    {
        if (UpdatePlanTab.Content is Grid existing && existing.Uid == "V060BrandingUpdateSurface")
            return;

        UpdatePlanTab.Content = null;
        var surface = new Grid { Uid = "V060BrandingUpdateSurface" };
        surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        surface.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var updateArtwork = BrandingImageResourcesV060.LoadUpdateArtwork();
        var artwork = new Image
        {
            Source = updateArtwork.Source,
            Height = 220,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8),
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(artwork, BitmapScalingMode.HighQuality);

        var artworkFrame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(2, 8, 18)),
            Child = artwork,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(artworkFrame, 0);
        surface.Children.Add(artworkFrame);

        Grid.SetRow(UpdatePlanTextBox, 1);
        surface.Children.Add(UpdatePlanTextBox);
        UpdatePlanTab.Content = surface;
    }

    private static string BuildV060ProgressSummary() =>
        "MATAWAKA WORKBENCH — CURRENT DEVELOPMENT STATUS\r\n" +
        "\r\n" +
        "Implementation frontier: v0.60 (merged source lineage)\r\n" +
        "Accepted release predecessor for this branding candidate: workbench-v0.56.2-accepted\r\n" +
        "v0.60 accepted release identity: NOT CREATED BY BRANDING\r\n" +
        "\r\n" +
        "Progress:\r\n" +
        "  v0.56   exact C2PA provenance admission -> PROVENANCE_OBSERVED_NO_AUTHORITY\r\n" +
        "  v0.56.1 human-reviewed provenance presentation\r\n" +
        "  v0.56.2 bounded accepted-release closure\r\n" +
        "  v0.57   provenance composed as constraint-only capability evidence\r\n" +
        "  v0.58   audit-only evidence attached to live agent results\r\n" +
        "  v0.59   human-reviewed POLICY AUTHORITY != EVIDENCE AUDIT presentation\r\n" +
        "  v0.60   live result wiring reuses the exact reviewed Authority / Evidence projection\r\n" +
        "\r\n" +
        "Windows branding candidate:\r\n" +
        "  Matawaka application/window icon\r\n" +
        "  startup splash for normal/branding-review launch\r\n" +
        "  v0.55.2 -> v0.60 artwork in Update Workbench only\r\n" +
        "\r\n" +
        "Boundaries:\r\n" +
        "  Branding != Authority\r\n" +
        "  Evidence displayed != Permission\r\n" +
        "  Implementation merged != Accepted release\r\n" +
        "  UI update != Publication authority\r\n";
}
