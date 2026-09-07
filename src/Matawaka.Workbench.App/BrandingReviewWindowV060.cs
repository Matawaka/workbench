using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Matawaka.Workbench.App;

/// <summary>
/// Dedicated presentation-only v0.60 branding review surface.
/// It does not construct MainWindow or initialize settings/agent/maintenance/runtime/publication surfaces.
/// </summary>
internal sealed class BrandingReviewWindowV060 : Window
{
    internal const string SmokeArgumentV060 = "--branding-review-smoke";

    private static readonly string DiagnosticRootV060 = Path.Combine(
        Path.GetTempPath(), "Matawaka", "Workbench", "branding-review-v060");

    internal static readonly string SmokeReceiptPathV060 = Path.Combine(DiagnosticRootV060, "smoke.json");
    internal static readonly string FailureReceiptPathV060 = Path.Combine(DiagnosticRootV060, "failure.json");
    internal static readonly string SmokeSplashRenderPathV060 = Path.Combine(DiagnosticRootV060, "smoke-splash-render.png");
    internal static readonly string SmokeCurrentBrandingRenderPathV060 = Path.Combine(DiagnosticRootV060, "smoke-current-branding-render.png");

    private readonly bool _smoke;
    private readonly BrandingSplashWindowV060 _splash;
    private readonly BrandingImageEvidenceV060 _currentBrandingEvidence;
    private readonly Image _currentBrandingImage;
    private readonly List<Button> _reviewActionReplicas = new();
    private bool _contentRendered;

    internal BrandingReviewWindowV060(bool smoke, BrandingSplashWindowV060 splash)
    {
        _smoke = smoke;
        _splash = splash;
        _currentBrandingEvidence = BrandingImageResourcesV060.LoadCurrentWorkbenchArtwork();

        Title = MainWindow.NormalTitleV060;
        Width = 1220;
        Height = 880;
        MinWidth = 980;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(2, 8, 18));
        Foreground = Brushes.White;
        Icon = MainWindow.LoadV060BrandingIcon();
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _currentBrandingImage = new Image
        {
            Source = _currentBrandingEvidence.Source,
            Height = 240,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(_currentBrandingImage, BitmapScalingMode.HighQuality);

        Content = BuildContent();
        ContentRendered += OnContentRendered;
    }

    private UIElement BuildContent()
    {
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var maintenanceReplica = BuildMaintenanceReplica();
        Grid.SetRow(maintenanceReplica, 0);
        root.Children.Add(maintenanceReplica);

        var artworkCard = BuildArtworkCard();
        Grid.SetRow(artworkCard, 1);
        root.Children.Add(artworkCard);

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
            FontSize = 14,
            Padding = new Thickness(14),
            MinHeight = 170
        };
        Grid.SetRow(progress, 2);
        root.Children.Add(progress);

        var decodeEvidence = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(36, 92, 132)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = new TextBlock
            {
                Text = $"Image decode: neutral artwork {_currentBrandingEvidence.PixelWidth}×{_currentBrandingEvidence.PixelHeight} " +
                       $"(range {_currentBrandingEvidence.LuminanceRange}) — reused for splash/current branding",
                Foreground = new SolidColorBrush(Color.FromRgb(174, 208, 230)),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        Grid.SetRow(decodeEvidence, 3);
        root.Children.Add(decodeEvidence);

        var status = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
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
        Grid.SetRow(status, 4);
        root.Children.Add(status);

        return root;
    }

    private UIElement BuildMaintenanceReplica()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Normal Workbench maintenance surface — VISUAL REPLICA ONLY; actions are not wired in this review",
            Foreground = new SolidColorBrush(Color.FromRgb(180, 218, 244)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 7)
        });

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(CreateReviewActionReplica("Update\nWorkbench", 92, true));
        buttons.Children.Add(CreateReviewActionReplica("Local\napps", 78, true));
        buttons.Children.Add(CreateReviewActionReplica("Publish\naccepted", 88, true));
        buttons.Children.Add(CreateReviewActionReplica("Lifecycle\nreceipt", 88, false));
        stack.Children.Add(buttons);

        return new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12),
            Background = new SolidColorBrush(Color.FromRgb(5, 15, 31)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(35, 92, 131)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = stack
        };
    }

    private Button CreateReviewActionReplica(string text, double width, bool rightMargin)
    {
        var button = new Button
        {
            Width = width,
            Height = 46,
            Margin = new Thickness(0, 0, rightMargin ? 6 : 0, 0),
            Content = new TextBlock { Text = text, TextAlignment = TextAlignment.Center },
            IsHitTestVisible = false,
            Focusable = false,
            IsTabStop = false,
            Tag = "VISUAL_REPLICA_NO_ACTION"
        };
        _reviewActionReplicas.Add(button);
        return button;
    }

    private UIElement BuildArtworkCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Matawaka Workbench — neutral current branding; historical version-transition artwork is not active UI",
            Foreground = new SolidColorBrush(Color.FromRgb(180, 218, 244)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 7)
        });
        stack.Children.Add(_currentBrandingImage);

        return new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Background = new SolidColorBrush(Color.FromRgb(2, 8, 18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(35, 92, 131)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = stack
        };
    }

    private async void OnContentRendered(object? sender, EventArgs e)
    {
        if (_contentRendered)
            return;

        _contentRendered = true;
        if (!_smoke)
            return;

        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        WriteSmokeReceipt();
        await Task.Delay(750);
        Close();
    }

    private void WriteSmokeReceipt()
    {
        Directory.CreateDirectory(DiagnosticRootV060);

        var splashRender = CaptureImageSourceRenderAtOrigin(_splash.ImageElement, SmokeSplashRenderPathV060);
        var currentBrandingRender = CaptureImageSourceRenderAtOrigin(_currentBrandingImage, SmokeCurrentBrandingRenderPathV060);

        var receipt = new
        {
            Schema = "matawaka.workbench-v060-branding-review-smoke/v0.6",
            Status = "BRANDING_REVIEW_WINDOW_CONTENT_RENDERED",
            ProcessId = Environment.ProcessId,
            ProcessPath = Environment.ProcessPath,
            Title,
            TitleIconAssigned = Icon is not null,
            OriginNormalizedWpfImageProbe = true,
            NeutralArtworkSha256 = _currentBrandingEvidence.Sha256,
            SplashSourcePixelWidth = _splash.ImageEvidence.PixelWidth,
            SplashSourcePixelHeight = _splash.ImageEvidence.PixelHeight,
            SplashSourceLuminanceRange = _splash.ImageEvidence.LuminanceRange,
            SplashSourceHasVisibleVariation = _splash.ImageEvidence.HasVisibleVariation,
            SplashElementActualWidth = _splash.RenderedImageWidth,
            SplashElementActualHeight = _splash.RenderedImageHeight,
            SplashRenderedPixelWidth = splashRender.PixelWidth,
            SplashRenderedPixelHeight = splashRender.PixelHeight,
            SplashRenderedLuminanceRange = splashRender.LuminanceRange,
            SplashRenderedHasVisibleVariation = splashRender.HasVisibleVariation,
            CurrentBrandingSourcePixelWidth = _currentBrandingEvidence.PixelWidth,
            CurrentBrandingSourcePixelHeight = _currentBrandingEvidence.PixelHeight,
            CurrentBrandingSourceLuminanceRange = _currentBrandingEvidence.LuminanceRange,
            CurrentBrandingSourceHasVisibleVariation = _currentBrandingEvidence.HasVisibleVariation,
            CurrentBrandingElementActualWidth = _currentBrandingImage.ActualWidth,
            CurrentBrandingElementActualHeight = _currentBrandingImage.ActualHeight,
            CurrentBrandingRenderedPixelWidth = currentBrandingRender.PixelWidth,
            CurrentBrandingRenderedPixelHeight = currentBrandingRender.PixelHeight,
            CurrentBrandingRenderedLuminanceRange = currentBrandingRender.LuminanceRange,
            CurrentBrandingRenderedHasVisibleVariation = currentBrandingRender.HasVisibleVariation,
            HistoricalVersionTransitionArtworkActive = false,
            ReviewActionReplicaCount = _reviewActionReplicas.Count,
            ReviewActionReplicaHitTestingEnabled = _reviewActionReplicas.Any(button => button.IsHitTestVisible),
            ReviewActionReplicaFocusable = _reviewActionReplicas.Any(button => button.Focusable || button.IsTabStop),
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

    private static RenderEvidenceV060 CaptureImageSourceRenderAtOrigin(Image liveImage, string path)
    {
        liveImage.UpdateLayout();

        var width = (int)Math.Ceiling(liveImage.ActualWidth);
        var height = (int)Math.Ceiling(liveImage.ActualHeight);
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"Branding image has no rendered area: {width}x{height}");
        if (liveImage.Source is null)
            throw new InvalidOperationException("Branding image has no WPF ImageSource.");

        var probe = new Image
        {
            Source = liveImage.Source,
            Width = width,
            Height = height,
            Stretch = liveImage.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(probe, BitmapScalingMode.HighQuality);
        probe.Measure(new Size(width, height));
        probe.Arrange(new Rect(0, 0, width, height));
        probe.UpdateLayout();

        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(probe);

        var (min, max) = BrandingImageResourcesV060.MeasureVisibleLuminance(rendered);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using (var output = File.Create(path))
            encoder.Save(output);

        return new RenderEvidenceV060(width, height, min, max);
    }

    internal static void WriteStartupFailure(Exception exception, bool smoke)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticRootV060);
            var receipt = new
            {
                Schema = "matawaka.workbench-v060-branding-review-startup-failure/v0.6",
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
            // Best-effort diagnostics only. Never convert logging failure into authority.
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
        "  exact four normal maintenance actions shown as non-interactive visual replicas\r\n" +
        "  neutral Matawaka Workbench artwork used for splash/current branding\r\n" +
        "  static historical version-transition artwork removed from active UI\r\n" +
        "\r\n" +
        "Boundaries:\r\n" +
        "  Branding != Authority\r\n" +
        "  Evidence displayed != Permission\r\n" +
        "  Implementation merged != Accepted release\r\n" +
        "  UI review != Publication authority\r\n";

    private readonly record struct RenderEvidenceV060(
        int PixelWidth,
        int PixelHeight,
        int MinLuminance,
        int MaxLuminance)
    {
        internal int LuminanceRange => MaxLuminance - MinLuminance;
        internal bool HasVisibleVariation => LuminanceRange >= 16;
    }
}
