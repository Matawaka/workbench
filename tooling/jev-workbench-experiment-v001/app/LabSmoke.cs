using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Matawaka.Workbench.JevLab;

// In-process functional smoke of the actual WPF window and the same loading handler.
// Synthetic inputs only; renders do not claim a human or external-agent UI review.
internal static class LabSmoke
{
    internal static async Task RunAsync(JevLabWindow window, string outputDirectory)
    {
        var output = PrepareOutput(outputDirectory);
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        var sources = new[] { JevLabWindow.DemoPath("read-only"), JevLabWindow.DemoPath("scope-expansion") };
        var sourceFiles = sources.SelectMany(p => new[] { p, Path.Combine(Path.GetDirectoryName(p)!, "candidate.json"), Path.Combine(Path.GetDirectoryName(p)!, "receipt.json") }).ToArray();
        var before = sourceFiles.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
        Check(window.DisplayState == "EMPTY" && window.VisibleSignalCount == 0, "initial-empty-state");
        await RenderAsync(window, Path.Combine(output, "01-empty.png"));
        Check(await window.LoadObservationAsync(sources[0]), "read-only-demo-loads");
        Check(window.VisibleSignalCount == 6 && window.Observation?.Provider == "synthetic.offline", "six-synthetic-signals-visible");
        Check(window.Observation!.Signals.Single(x => x.Id == "operationalSpecificity").ResearchOnly, "specificity-research-only");
        await RenderAsync(window, Path.Combine(output, "02-read-only.png"));
        Check(await window.LoadObservationAsync(sources[1]), "scope-expansion-demo-loads");
        Check(window.Observation!.Signals.Single(x => x.Id == "scopeExpansion").Probability > .9, "second-observation-replaces-first");
        await RenderAsync(window, Path.Combine(output, "03-scope-expansion.png"));
        Check(!await window.LoadObservationAsync(Path.Combine(output, "absent-inputs.json")), "missing-file-rejected");
        Check(window.DisplayState == "ERROR" && window.Observation is null && window.VisibleSignalCount == 0, "failure-clears-old-signals-and-context");
        await RenderAsync(window, Path.Combine(output, "04-rejected-input.png"));
        Check(await window.LoadObservationAsync(sources[0]), "recovery-after-failed-load");
        window.Clear(); Check(window.DisplayState == "EMPTY" && window.VisibleSignalCount == 0 && window.Observation is null, "clear-removes-loaded-observation");
        var after = sourceFiles.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
        Check(before.SequenceEqual(after), "all-six-source-files-unchanged");
        var receipt = new { schema = "matawaka.jev-lab-ui-smoke/v0.1", status = "PASS", checks, realWpfWindowRendered = true, externalUiAutomation = "NOT_PART_OF_THIS_SMOKE", providerInvocations = 0, referenceAdmission = "NOT_ASSESSED", productionMainWindowConstructed = false };
        await WriteNewAsync(Path.Combine(output, "smoke.json"), JsonSerializer.SerializeToUtf8Bytes(receipt, new JsonSerializerOptions { WriteIndented = true }));
        var entries = Directory.GetFiles(output).Select(p => new { name = Path.GetFileName(p), rawSha256 = "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant(), bytes = new FileInfo(p).Length }).ToArray();
        await WriteNewAsync(Path.Combine(output, "OUTPUT-MANIFEST.json"), JsonSerializer.SerializeToUtf8Bytes(new { schema = "matawaka.jev-lab-output-manifest/v0.1", entries }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string PrepareOutput(string value)
    {
        if (!Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal) || value.Contains("://", StringComparison.Ordinal)) throw new IOException("LOCAL_ABSOLUTE_OUTPUT_REQUIRED");
        var output = Path.GetFullPath(value);
        if (output.StartsWith(@"\\", StringComparison.Ordinal) || output.StartsWith("//", StringComparison.Ordinal) || output.Length < 3 || !char.IsAsciiLetter(output[0]) || output[1] != ':' || output.AsSpan(2).Contains(':')) throw new IOException("LOCAL_DRIVE_OUTPUT_REQUIRED");
        var drive = new DriveInfo(Path.GetPathRoot(output)!);
        if (drive.DriveType == DriveType.Network) throw new IOException("NETWORK_OUTPUT_REFUSED");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("OUTPUT_EXISTS");
        var parent = Directory.GetParent(output) ?? throw new IOException("OUTPUT_PARENT_REQUIRED");
        if (!parent.Exists) throw new IOException("OUTPUT_PARENT_MUST_EXIST");
        for (DirectoryInfo? current = parent; current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("OUTPUT_REPARSE_REFUSED");
            if (File.Exists(Path.Combine(current.FullName, ".git")) || Directory.Exists(Path.Combine(current.FullName, ".git"))) throw new IOException("REPOSITORY_OUTPUT_REFUSED");
        }
        Directory.CreateDirectory(output);
        using var reservation = new FileStream(Path.Combine(output, "owner.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return output;
    }

    private static async Task RenderAsync(JevLabWindow window, string file)
    {
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None); encoder.Save(stream);
    }
    private static async Task WriteNewAsync(string file, byte[] bytes)
    {
        await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes);
    }
}
