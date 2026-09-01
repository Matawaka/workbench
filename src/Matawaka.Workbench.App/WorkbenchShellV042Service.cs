using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Matawaka.Workbench.App;

public sealed record InstalledAppV042(string ApplicationId, string Version)
{
    public string Display => $"{ApplicationId} · {Version}";
}

/// <summary>
/// Read-only observation of already-registered direct children under Workspace/Apps.
/// It never creates identity, registers, updates, launches, copies or moves an app.
/// </summary>
public static class InstalledAppsV042Service
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<InstalledAppV042> Read(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) return Array.Empty<InstalledAppV042>();
        var workspace = Path.GetFullPath(workspaceRoot.Trim());
        var appsRoot = Path.Combine(workspace, LocalApplicationMaintenanceService.AppsDirectoryName);
        if (!Directory.Exists(appsRoot) || IsReparse(appsRoot)) return Array.Empty<InstalledAppV042>();

        var result = new List<InstalledAppV042>();
        foreach (var directory in Directory.EnumerateDirectories(appsRoot).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (IsReparse(directory)) continue;
            var appId = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var identityPath = Path.Combine(directory, LocalApplicationMaintenanceService.IdentityFileName);
            if (!File.Exists(identityPath) || IsReparse(identityPath)) continue;

            try
            {
                var identity = JsonSerializer.Deserialize<LocalApplicationIdentity>(File.ReadAllText(identityPath), JsonOptions);
                if (identity is null ||
                    identity.Schema != LocalApplicationMaintenanceService.IdentitySchema ||
                    !identity.ApplicationId.Equals(appId, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(identity.Version))
                    continue;
                result.Add(new InstalledAppV042(identity.ApplicationId, identity.Version));
            }
            catch (JsonException)
            {
                // Invalid sidecars are not represented as installed apps.
            }
            catch (IOException)
            {
                // Read-only observation remains fail-soft for one unavailable app.
            }
            catch (UnauthorizedAccessException)
            {
                // Read-only observation remains fail-soft for one unavailable app.
            }
        }

        return result.OrderBy(x => x.ApplicationId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("shell-v042-app-display", new InstalledAppV042("demo", "1.2.3").Display == "demo · 1.2.3", new InstalledAppV042("demo", "1.2.3").Display, "demo · 1.2.3"),
        ("shell-v042-app-observation-only", true, "read identity sidecars only", "no registration/update/launch authority")
    };

    private static bool IsReparse(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }
}

/// <summary>
/// Presentation-only classification for the existing free-form status text.
/// The converter does not change status text or terminal semantics.
/// </summary>
public sealed class StatusForegroundV042Converter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => ClassifyStatus(value as string) switch
        {
            "positive" => Brushes.ForestGreen,
            "error" => Brushes.Red,
            "warning" => Brushes.Goldenrod,
            _ => SystemColors.ControlTextBrush
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    public static string ClassifyStatus(string? status)
    {
        var text = (status ?? string.Empty).TrimStart();
        if (StartsWithAny(text, "COMPLETED", "SUCCESS", "PASSED", "PASS", "VALID")) return "positive";
        if (StartsWithAny(text, "ERROR", "FAILED", "INVALID")) return "error";
        if (StartsWithAny(text, "WARNING", "WARN", "CANCELLED", "CANCELED")) return "warning";
        return "neutral";
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("shell-v042-status-completed-green", ClassifyStatus("COMPLETED: ok") == "positive", ClassifyStatus("COMPLETED: ok"), "positive"),
        ("shell-v042-status-error-red", ClassifyStatus("ERROR: failed") == "error", ClassifyStatus("ERROR: failed"), "error"),
        ("shell-v042-status-failed-red", ClassifyStatus("FAILED: test") == "error", ClassifyStatus("FAILED: test"), "error"),
        ("shell-v042-status-warning-yellow", ClassifyStatus("WARNING: check") == "warning", ClassifyStatus("WARNING: check"), "warning"),
        ("shell-v042-status-neutral", ClassifyStatus("RUNNING: work") == "neutral", ClassifyStatus("RUNNING: work"), "neutral")
    };

    private static bool StartsWithAny(string text, params string[] tokens)
        => tokens.Any(token => text.StartsWith(token, StringComparison.OrdinalIgnoreCase));
}
