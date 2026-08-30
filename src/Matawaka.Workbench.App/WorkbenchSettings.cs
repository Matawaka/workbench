using System.IO;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record WorkbenchSettings(string WorkspaceRoot, string CatalogRoot);

public static class WorkbenchSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "workbench.settings.json");

    public static WorkbenchSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                var value = JsonSerializer.Deserialize<WorkbenchSettings>(json, JsonOptions);
                if (value is not null && !string.IsNullOrWhiteSpace(value.WorkspaceRoot))
                    return value;
            }
        }
        catch
        {
            // Fall back to deterministic local discovery. Settings are convenience, not authority.
        }

        var workspace = DetectWorkspaceRoot();
        return new WorkbenchSettings(workspace, Path.Combine(workspace, "Catalog"));
    }

    public static void Save(WorkbenchSettings settings)
    {
        Directory.CreateDirectory(AppContext.BaseDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json, new UTF8Encoding(false));
    }

    private static string DetectWorkspaceRoot()
    {
        var environmentRoot = Environment.GetEnvironmentVariable("MATAWAKA_HOME");
        if (!string.IsNullOrWhiteSpace(environmentRoot))
            return environmentRoot;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && current is not null; i++, current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Catalog")))
                return current.FullName;

            if (string.Equals(current.Name, "Matawaka", StringComparison.OrdinalIgnoreCase))
                return current.FullName;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Matawaka");
    }
}
