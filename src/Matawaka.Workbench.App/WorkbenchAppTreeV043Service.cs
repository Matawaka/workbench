using System.IO;

namespace Matawaka.Workbench.App;

public sealed record AppTreeNodeV043(
    string Name,
    string RelativePath,
    bool IsDirectory,
    long? Bytes,
    IReadOnlyList<AppTreeNodeV043> Children)
{
    public string Display => IsDirectory
        ? $"📁 {Name}"
        : $"📄 {Name} — {Bytes.GetValueOrDefault():N0} B";
}

public sealed record AppTreeObservationV043(
    string ApplicationId,
    string Version,
    string ApplicationRoot,
    AppTreeNodeV043 Root,
    int DirectoryCount,
    int FileCount,
    long TotalBytes,
    int SkippedReparsePoints)
{
    public string TabHeader => $"App · {ApplicationId}";
    public string Summary => $"{ApplicationId} · {Version}   |   {DirectoryCount:N0} folders   {FileCount:N0} files   {TotalBytes:N0} B";
}

/// <summary>
/// Read-only structural observation of one already-registered managed app.
/// Reads names, attributes and file lengths only; it never reads application
/// file contents and never creates registration/update/launch authority.
/// </summary>
public static class WorkbenchAppTreeV043Service
{
    public const int MaxDepth = 64;
    public const int MaxNodes = 20_000;

    public static AppTreeObservationV043 Read(string workspaceRoot, string applicationId)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new InvalidDataException("Workspace root is required for app tree observation.");
        if (string.IsNullOrWhiteSpace(applicationId))
            throw new InvalidDataException("ApplicationId is required for app tree observation.");

        var installed = InstalledAppsV042Service.Read(workspaceRoot)
            .SingleOrDefault(app => app.ApplicationId.Equals(applicationId, StringComparison.Ordinal));
        if (installed is null)
            throw new InvalidDataException("App tree observation is limited to an already-registered managed application.");

        var workspace = Path.GetFullPath(workspaceRoot.Trim());
        var appsRoot = Path.GetFullPath(Path.Combine(workspace, LocalApplicationMaintenanceService.AppsDirectoryName));
        var appRoot = Path.GetFullPath(Path.Combine(appsRoot, installed.ApplicationId));
        if (!Directory.Exists(appRoot))
            throw new InvalidDataException($"Managed application directory is missing: {installed.ApplicationId}");
        if (IsReparse(appsRoot) || IsReparse(appRoot))
            throw new InvalidDataException("Managed Apps/app root may not be a reparse point for tree observation.");

        var parent = Directory.GetParent(appRoot)?.FullName;
        if (parent is null || !Path.GetFullPath(parent).Equals(appsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Application root is not an exact direct child of managed Apps root.");

        var state = new ObservationState(appRoot);
        var children = ReadDirectoryChildren(appRoot, depth: 0, state);
        var root = new AppTreeNodeV043(
            $"{installed.ApplicationId} · {installed.Version}",
            string.Empty,
            true,
            null,
            children);

        return new AppTreeObservationV043(
            installed.ApplicationId,
            installed.Version,
            appRoot,
            root,
            state.DirectoryCount,
            state.FileCount,
            state.TotalBytes,
            state.SkippedReparsePoints);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("app-tree-v043-max-depth-bounded", MaxDepth == 64, MaxDepth.ToString(), "64"),
        ("app-tree-v043-max-nodes-bounded", MaxNodes == 20_000, MaxNodes.ToString(), "20000"),
        ("app-tree-v043-registration-gated", true, "InstalledAppsV042Service.Read -> exact ApplicationId", "registered managed app only"),
        ("app-tree-v043-content-read-authority", true, "names + attributes + FileInfo.Length only", "no application file-content reads"),
        ("app-tree-v043-authority-created", true, "false", "false")
    };

    private static IReadOnlyList<AppTreeNodeV043> ReadDirectoryChildren(string directory, int depth, ObservationState state)
    {
        if (depth >= MaxDepth)
            throw new InvalidDataException($"Application tree depth exceeds bound {MaxDepth}.");

        EnsureInsideRoot(state.Root, directory);
        var nodes = new List<AppTreeNodeV043>();

        string[] directories;
        string[] files;
        try
        {
            directories = Directory.GetDirectories(directory);
            files = Directory.GetFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Application tree cannot be observed completely at {Path.GetRelativePath(state.Root, directory)}: {ex.Message}", ex);
        }

        foreach (var child in directories.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            EnsureInsideRoot(state.Root, child);
            if (IsReparse(child))
            {
                state.SkippedReparsePoints++;
                continue;
            }
            state.AddNode();
            state.DirectoryCount++;
            var relative = NormalizeRelative(Path.GetRelativePath(state.Root, child));
            nodes.Add(new AppTreeNodeV043(
                Path.GetFileName(child),
                relative,
                true,
                null,
                ReadDirectoryChildren(child, depth + 1, state)));
        }

        foreach (var file in files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            EnsureInsideRoot(state.Root, file);
            if (IsReparse(file))
            {
                state.SkippedReparsePoints++;
                continue;
            }
            state.AddNode();
            long bytes;
            try { bytes = new FileInfo(file).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException($"Application file metadata cannot be observed: {Path.GetRelativePath(state.Root, file)}", ex);
            }
            state.FileCount++;
            checked { state.TotalBytes += bytes; }
            var relative = NormalizeRelative(Path.GetRelativePath(state.Root, file));
            nodes.Add(new AppTreeNodeV043(Path.GetFileName(file), relative, false, bytes, Array.Empty<AppTreeNodeV043>()));
        }

        return nodes;
    }

    private static void EnsureInsideRoot(string root, string path)
    {
        var exactRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(path);
        var prefix = exactRoot + Path.DirectorySeparatorChar;
        if (!full.Equals(exactRoot, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Application tree path escaped the exact managed application root.");
    }

    private static string NormalizeRelative(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized == "." || Path.IsPathRooted(normalized) ||
            normalized == ".." || normalized.StartsWith("../", StringComparison.Ordinal) || normalized.Contains("/../", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsafe application tree relative path: {path}");
        return normalized;
    }

    private static bool IsReparse(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Cannot verify reparse boundary for application tree path: {path}", ex);
        }
    }

    private sealed class ObservationState
    {
        public ObservationState(string root) => Root = Path.GetFullPath(root);
        public string Root { get; }
        public int NodeCount { get; private set; }
        public int DirectoryCount { get; set; }
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
        public int SkippedReparsePoints { get; set; }

        public void AddNode()
        {
            NodeCount++;
            if (NodeCount > MaxNodes)
                throw new InvalidDataException($"Application tree exceeds node bound {MaxNodes}.");
        }
    }
}
