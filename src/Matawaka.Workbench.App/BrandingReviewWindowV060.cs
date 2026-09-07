using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Matawaka.Workbench.App;

/// <summary>
/// Dedicated v0.60 branding review surface.
///
/// This window is intentionally isolated from MainWindow so human/CI branding review
/// cannot initialize WorkbenchSettingsStore, WorkbenchAgentService, maintenance,
/// local-app, runtime/model, acceptance or publication surfaces.
/// </summary>
internal sealed class BrandingReviewWindowV060 : Window
{
    internal const string SmokeArgumentV060 = "--branding-review-smoke";

    private static readonly string DiagnosticRootV060 = Path.Combine(
        Path.GetTempPath(),
        "Matawaka",
        "Workbench",
        "branding-review-v060");

    internal static readonly string SmokeReceiptPathV060 = Path.Combine(
        DiagnosticRootV060,
        "smoke.json");

    internal static readonly string FailureReceiptPathV060 = Path.Combine(
        DiagnosticRootV060,
        "failure.json");

    private readonly bool _smoke;
    private bool _contentRendered;

    internal BrandingReviewWindowV060(bool smoke)
    {
        _smoke = smoke;

        Title = MainWindow.NormalTitleV060;
        Width = 1160;
        Height = 820;
        MinWidth = 900;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(2, 8, 18));
        Foreground = Brushes.White;

        Content = BuildContent();

        ContentRendered += OnContentRendered;
    }

    private UIElement BuildContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(18)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var artwork = new Image
        {
            Source = new BitmapImage(new Uri(
                "pack://application:,,,/Matawaka.Workbench.App;component/Assets/Branding/update-v060.jpg",
                UriKind.Absolute)),
            Height = 360,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(artwork, 0);
        root.Children.Add(artwork);

        var progress = new TextBox
        {
            Text = BuildProgressSummary(),
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(5, 15, 31)),
            Foreground = new SolidColorBrush(Color.FromRgb(224, 239, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(39, 108, 158)),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            Padding = new Thickness(16)
        };
        Grid.SetRow(progress, 1);
        root.Children.Add(progress);

        var status = new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(14, 10, 14, 10),
            BorderBrush = new SolidColorBrush(Color.FromRgb(46, 143, 199)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = _smoke
                    ? "CI BRANDING REVIEW SMOKE — PRESENTATION ONLY"
                    : "READ-ONLY BRANDING REVIEW — NO UPDATE / ACCEPT / PUBLISH AUTHORITY",
                Foreground = new SolidColorBrush(Color.FromRgb(161, 220, 255)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        Grid.SetRow(status, 2);
        root.Children.Add(status);

        return root;
    }

    private async void OnContentRendered(object? sender, EventArgs e)
    {
        if (_contentRendered)
            return;

        _contentRendered = true;

        if (!_smoke)
            return;

        WriteSmokeReceipt();

        // Keep the real WPF window alive long enough for the splash to close and
        // the hosted Windows process smoke to observe a genuine rendered surface.
        await Task.Delay(900);
        Close();
    }

    private void WriteSmokeReceipt()
    {
        Directory.CreateDirectory(DiagnosticRootV060);

        var receipt = new
        {
            Schema = "matawaka.workbench-v060-branding-review-smoke/v0.2",
            Status = "BRANDING_REVIEW_WINDOW_CONTENT_RENDERED",
            ProcessId = Environment.ProcessId,
            ProcessPath = Environment.ProcessPath,
            Title,
            FullMainWindowConstructed = false,
            WorkbenchSettingsStoreInitialized = false,
            WorkbenchAgentServiceInitialized = false,
            MaintenanceAuthorityCreated = false,
            AcceptedReleaseIdentityCreated = false,
            PublicationPerformed = false,
            WorkbenchNetworkTransportPerformed = false,
            ProcessNetworkIsolationProven = false,
            RuntimeOrModelExecutionPerformed = false,
            DiagnosticFilesystemWriteOnly = true
        };

        File.WriteAllText(
            SmokeReceiptPathV060,
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void WriteStartupFailure(Exception exception, bool smoke)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticRootV060);

            var receipt = new
            {
                Schema = "matawaka.workbench-v060-branding-review-startup-failure/v0.2",
                Status = "BRANDING_REVIEW_STARTUP_FAILED",
                Mode = smoke ? "SMOKE" : "HUMAN_REVIEW",
                ProcessId = Environment.ProcessId,
                ProcessPath = Environment.ProcessPath,
                ExceptionType = exception.GetType().FullName,
                ExceptionMessage = exception.Message,
                ExceptionStackTrace = exception.ToString(),
                FullMainWindowConstructed = false,
                WorkbenchSettingsStoreInitialized = false,
                WorkbenchAgentServiceInitialized = false,
                MaintenanceAuthorityCreated = false,
                AcceptedReleaseIdentityCreated = false,
                PublicationPerformed = false,
                WorkbenchNetworkTransportPerformed = false,
                ProcessNetworkIsolationProven = false,
                RuntimeOrModelExecutionPerformed = false,
                DiagnosticFilesystemWriteOnly = true
            };

            File.WriteAllText(
                FailureReceiptPathV060,
                JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort diagnostics only. Never convert a logging failure into authority.
        }
    }

    private static string BuildProgressSummary() =>
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
        "Windows branding review:\r\n" +
        "  isolated presentation-only startup path\r\n" +
        "  Matawaka executable icon\r\n" +
        "  startup splash\r\n" +
        "  v0.55.2 -> v0.60 artwork\r\n" +
        "\r\n" +
        "Boundaries:\r\n" +
        "  Branding != Authority\r\n" +
        "  Evidence displayed != Permission\r\n" +
        "  Implementation merged != Accepted release\r\n" +
        "  UI review != Publication authority\r\n";
}
