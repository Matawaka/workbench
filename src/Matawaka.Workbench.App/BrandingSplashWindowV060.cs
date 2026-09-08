using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Matawaka.Workbench.App;

/// <summary>
/// Minimal presentation-only v0.60 splash surface.
///
/// The exact branding bitmap is synchronously decoded from the assembly manifest.
/// A missing/undecodable/flat image fails explicitly instead of producing a silent
/// black splash. This window has no settings/agent/runtime/acceptance surface.
/// </summary>
internal sealed class BrandingSplashWindowV060 : Window
{
    private readonly Image _image;

    internal BrandingImageEvidenceV060 ImageEvidence { get; }
    internal Image ImageElement => _image;
    internal double RenderedImageWidth => _image.ActualWidth;
    internal double RenderedImageHeight => _image.ActualHeight;

    internal BrandingSplashWindowV060()
    {
        ImageEvidence = BrandingImageResourcesV060.LoadSplash();

        Width = 800;
        Height = 450;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(2, 8, 18));
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _image = new Image
        {
            Source = ImageEvidence.Source,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(2, 8, 18)),
            Child = _image
        };
    }
}
