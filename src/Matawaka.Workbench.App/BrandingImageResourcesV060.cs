using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Matawaka.Workbench.App;

internal readonly record struct BrandingImageEvidenceV060(
    BitmapSource Source,
    int PixelWidth,
    int PixelHeight,
    int MinLuminance,
    int MaxLuminance)
{
    internal int LuminanceRange => MaxLuminance - MinLuminance;
    internal bool HasVisibleVariation => LuminanceRange >= 16;
}

/// <summary>
/// Exact, synchronous v0.60 branding image loader.
///
/// The first human visual review proved that a WPF window reaching ContentRendered
/// does not prove a pack-URI bitmap is actually visible. These images are therefore
/// loaded from manifest resources into memory with BitmapCacheOption.OnLoad. Missing
/// or undecodable bytes fail explicitly instead of silently degrading to a black frame.
/// </summary>
internal static class BrandingImageResourcesV060
{
    internal const string SplashResourceNameV060 = "Matawaka.Workbench.App.Branding.SplashV060";
    internal const string UpdateArtworkResourceNameV060 = "Matawaka.Workbench.App.Branding.UpdateArtworkV060";

    internal static BrandingImageEvidenceV060 LoadSplash()
        => LoadExactBitmap(SplashResourceNameV060);

    internal static BrandingImageEvidenceV060 LoadUpdateArtwork()
        => LoadExactBitmap(UpdateArtworkResourceNameV060);

    internal static (int MinLuminance, int MaxLuminance) MeasureVisibleLuminance(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        if (width <= 0 || height <= 0)
            return (0, 0);

        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        converted.CopyPixels(pixels, stride, 0);

        var min = 255;
        var max = 0;
        var observed = false;

        // Sample every fourth pixel in both dimensions. This is deterministic,
        // inexpensive and sufficient to reject a blank/flat presentation surface.
        for (var y = 0; y < height; y += 4)
        {
            var row = y * stride;
            for (var x = 0; x < width; x += 4)
            {
                var i = row + (x * 4);
                var b = pixels[i];
                var g = pixels[i + 1];
                var r = pixels[i + 2];
                var a = pixels[i + 3];
                if (a == 0)
                    continue;

                var luminance = ((r * 2126) + (g * 7152) + (b * 722)) / 10000;
                min = Math.Min(min, luminance);
                max = Math.Max(max, luminance);
                observed = true;
            }
        }

        return observed ? (min, max) : (0, 0);
    }

    private static BrandingImageEvidenceV060 LoadExactBitmap(string logicalName)
    {
        var assembly = typeof(BrandingImageResourcesV060).Assembly;
        using var resource = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Missing branding image manifest resource: {logicalName}");

        var decoder = BitmapDecoder.Create(
            resource,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count != 1)
            throw new InvalidOperationException(
                $"Unexpected branding image frame count for {logicalName}: {decoder.Frames.Count}");

        var source = decoder.Frames[0];
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
            throw new InvalidOperationException(
                $"Decoded branding image has invalid dimensions for {logicalName}: {source.PixelWidth}x{source.PixelHeight}");

        if (source.CanFreeze && !source.IsFrozen)
            source.Freeze();

        var (min, max) = MeasureVisibleLuminance(source);
        if (max - min < 16)
            throw new InvalidOperationException(
                $"Decoded branding image is visually flat for {logicalName}: luminanceRange={max - min}");

        return new BrandingImageEvidenceV060(
            source,
            source.PixelWidth,
            source.PixelHeight,
            min,
            max);
    }
}
