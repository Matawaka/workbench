using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Matawaka.Workbench.App;

/// <summary>
/// Minimal presentation-only v0.60 splash surface.
///
/// This intentionally avoids System.Windows.SplashScreen because the real-process
/// qualification observed that framework path rejecting the qualified JPEG bytes.
/// The window has no settings/agent/maintenance/runtime/acceptance/publication surface.
/// </summary>
internal sealed class BrandingSplashWindowV060 : Window
{
    internal BrandingSplashWindowV060()
    {
        Width = 800;
        Height = 450;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;

        Content = new Image
        {
            Source = new BitmapImage(new Uri(
                "pack://application:,,,/Matawaka.Workbench.App;component/Assets/Branding/splash-v060.jpg",
                UriKind.Absolute)),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
    }
}
