using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Matawaka.Workbench.App;

internal readonly record struct BrandingImageEvidenceV060(
    BitmapSource Source,
    int PixelWidth,
    int PixelHeight,
    int MinLuminance,
    int MaxLuminance,
    string Sha256,
    int DecodedBytes)
{
    internal int LuminanceRange => MaxLuminance - MinLuminance;
    internal bool HasVisibleVariation => LuminanceRange >= 16;
}

/// <summary>
/// Exact synchronous v0.60 branding image loader.
///
/// The active product uses only the neutral Matawaka Workbench artwork. Binary image
/// bytes are transported as bounded ASCII base64 chunks. At runtime/build qualification
/// the chunks are concatenated, reverse-decoded, SHA-256 checked, decoded with WPF
/// BitmapDecoder(OnLoad), dimension checked and rejected if visually flat.
///
/// historical user-supplied v0.55.2 -> v0.60 transition artwork remains provenance evidence only
/// and is not an active presentation surface.
/// </summary>
internal static class BrandingImageResourcesV060
{
    internal const string ExpectedNeutralArtworkSha256 = "3ac0eed186a546ee029cba69790d064948bae40de6334971f25b14e6fbc9951c";
    internal const int ExpectedPixelWidth = 400;
    internal const int ExpectedPixelHeight = 225;

    private static readonly string[] NeutralArtworkChunkResources =
    {
        "Matawaka.Workbench.App.Branding.NeutralBase64.001",
        "Matawaka.Workbench.App.Branding.NeutralBase64.002",
        "Matawaka.Workbench.App.Branding.NeutralBase64.003",
        "Matawaka.Workbench.App.Branding.NeutralBase64.004"
    };

    internal static BrandingImageEvidenceV060 LoadSplash()
        => LoadExactBitmap(NeutralArtworkChunkResources, ExpectedNeutralArtworkSha256, "startup-splash");

    internal static BrandingImageEvidenceV060 LoadCurrentWorkbenchArtwork()
        => LoadExactBitmap(NeutralArtworkChunkResources, ExpectedNeutralArtworkSha256, "current-workbench-artwork");

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

    private static BrandingImageEvidenceV060 LoadExactBitmap(
        IReadOnlyList<string> chunkResourceNames,
        string expectedSha256,
        string label)
    {
        var assembly = typeof(BrandingImageResourcesV060).Assembly;
        var encoded = new StringBuilder();

        foreach (var resourceName in chunkResourceNames)
        {
            using var resource = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing branding base64 chunk: {resourceName}");
            using var reader = new StreamReader(resource, Encoding.ASCII, false, 1024, leaveOpen: false);
            encoded.Append(reader.ReadToEnd().Trim());
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded.ToString());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"Branding {label} base64 transport is invalid.", ex);
        }

        var observedSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(observedSha256, expectedSha256, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Branding {label} reverse-decoded SHA-256 mismatch: observed={observedSha256}; expected={expectedSha256}");

        using var imageStream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(
            imageStream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count != 1)
            throw new InvalidOperationException(
                $"Unexpected branding {label} frame count: {decoder.Frames.Count}");

        var source = decoder.Frames[0];
        if (source.PixelWidth != ExpectedPixelWidth || source.PixelHeight != ExpectedPixelHeight)
            throw new InvalidOperationException(
                $"Unexpected branding {label} dimensions: {source.PixelWidth}x{source.PixelHeight}; expected={ExpectedPixelWidth}x{ExpectedPixelHeight}");

        if (source.CanFreeze && !source.IsFrozen)
            source.Freeze();

        var (min, max) = MeasureVisibleLuminance(source);
        if (max - min < 16)
            throw new InvalidOperationException(
                $"Decoded branding {label} is visually flat: luminanceRange={max - min}");

        return new BrandingImageEvidenceV060(
            source,
            source.PixelWidth,
            source.PixelHeight,
            min,
            max,
            observedSha256,
            bytes.Length);
    }
}
