using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalAppContextFileV046(
    string Path,
    string Sha256,
    long Bytes,
    string Role);

public sealed record LocalAppUpdateContextV046(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string ApplicationId,
    string CurrentVersion,
    string ApplicationRoot,
    string IdentitySha256,
    string TreeSha256,
    IReadOnlyList<LocalAppContextFileV046> Files,
    bool ContainsFileContents,
    bool NetworkAccessPerformed,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed class LocalAppUpdateContextV046Service
{
    public const string Version = "0.46.0";
    public const string Schema = "matawaka.local-app-update-context/v0.46";
    public const int MaxFiles = 4096;
    public const long MaxTotalBytes = 2L * 1024L * 1024L * 1024L;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public LocalAppUpdateContextV046 Build(string workspaceRoot, string applicationId, CancellationToken cancellationToken)
    {
        var appRoot = LocalAppV046FileBoundary.ResolveRegisteredApplicationRoot(workspaceRoot, applicationId);
        var identityPath = Path.Combine(appRoot, LocalApplicationMaintenanceService.IdentityFileName);
        var identity = LocalAppV046FileBoundary.ReadIdentity(identityPath, applicationId);
        var files = LocalAppV046FileBoundary.Inventory(appRoot, includeSourceSidecar: true, MaxFiles, MaxTotalBytes, cancellationToken);
        var tree = LocalAppV046FileBoundary.ComputeTreeDigest(files);

        return new LocalAppUpdateContextV046(
            Schema,
            Version,
            DateTimeOffset.Now,
            applicationId,
            identity.Version,
            appRoot,
            LocalAppV046FileBoundary.HashFile(identityPath),
            tree,
            files,
            false,
            false,
            new[]
            {
                "no application file contents included",
                "no application mutation",
                "no source mutation",
                "no network or upload",
                "no application launch",
                "no update authority",
                "no Git/catalog/Agent Execute authority"
            },
            "Content-free predecessor inventory for sparse matawaka.local-app-update-package/v1 generation. Paths/SHA-256/sizes are evidence; file contents remain local until separately disclosed.");
    }

    public async Task<(LocalAppUpdateContextV046 Context, string ArtifactPath)> ExportAsync(
        string workspaceRoot,
        string applicationId,
        CancellationToken cancellationToken)
    {
        var context = Build(workspaceRoot, applicationId, cancellationToken);
        var dir = LocalAppV046FileBoundary.RequireWorkbenchArtifactDirectory(workspaceRoot, "local-app-context");
        var path = Path.Combine(dir, $"local-app-update-context-{LocalAppV046FileBoundary.SafeToken(applicationId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(context, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return (context, path);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("update-context-v046-content-free", true, "ContainsFileContents=false", "false"),
        ("update-context-v046-bounded-files", MaxFiles == 4096, MaxFiles.ToString(), "4096"),
        ("update-context-v046-network", true, "false", "false"),
        ("update-context-v046-sparse-purpose", true, "paths+sha256+bytes", "predecessor bindings without contents")
    };
}

internal static class LocalAppV046FileBoundary
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string ResolveWorkspaceRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var full = Path.GetFullPath(workspaceRoot.Trim());
        if (!Directory.Exists(full)) throw new InvalidDataException($"Workspace root does not exist: {full}");
        RejectReparse(full, "workspace root");
        return full;
    }

    public static string ResolveRegisteredApplicationRoot(string workspaceRoot, string applicationId)
    {
        if (!IsSafeApplicationId(applicationId)) throw new InvalidDataException($"Unsafe ApplicationId: {applicationId}");
        var workspace = ResolveWorkspaceRoot(workspaceRoot);
        var apps = Path.GetFullPath(Path.Combine(workspace, LocalApplicationMaintenanceService.AppsDirectoryName));
        if (!Directory.Exists(apps)) throw new InvalidDataException($"Managed Apps root missing: {apps}");
        RejectReparse(apps, "managed Apps root");
        var app = Path.GetFullPath(Path.Combine(apps, applicationId));
        EnsureDirectChild(apps, app, "registered application");
        if (!Directory.Exists(app)) throw new InvalidDataException($"Registered application root missing: {app}");
        RejectReparse(app, "registered application root");
        var identity = Path.Combine(app, LocalApplicationMaintenanceService.IdentityFileName);
        if (!File.Exists(identity)) throw new InvalidDataException($"Registered identity missing: {identity}");
        RejectReparse(identity, "registered identity");
        _ = ReadIdentity(identity, applicationId);
        return app;
    }

    public static string ResolveSourceRoot(string workspaceRoot, string applicationId, bool requireBinding)
    {
        if (!IsSafeApplicationId(applicationId)) throw new InvalidDataException($"Unsafe ApplicationId: {applicationId}");
        var workspace = ResolveWorkspaceRoot(workspaceRoot);
        var sources = Path.GetFullPath(Path.Combine(workspace, LocalAppSourceBindingV046Service.SourcesDirectoryName));
        if (!Directory.Exists(sources)) throw new InvalidDataException($"AppSources root missing: {sources}");
        RejectReparse(sources, "AppSources root");
        var source = Path.GetFullPath(Path.Combine(sources, applicationId));
        EnsureDirectChild(sources, source, "development source");
        if (!Directory.Exists(source)) throw new InvalidDataException($"Development source root missing: {source}");
        RejectReparse(source, "development source root");
        if (requireBinding)
        {
            var binding = Path.Combine(source, LocalAppSourceBindingV046Service.BindingFileName);
            if (!File.Exists(binding)) throw new InvalidDataException($"Development source is not bound: {binding}");
            RejectReparse(binding, "source binding");
        }
        return source;
    }

    public static LocalApplicationIdentity ReadIdentity(string identityPath, string expectedApplicationId)
    {
        var identity = JsonSerializer.Deserialize<LocalApplicationIdentity>(File.ReadAllBytes(identityPath), JsonOptions)
            ?? throw new InvalidDataException("Application identity could not be parsed.");
        if (identity.Schema != LocalApplicationMaintenanceService.IdentitySchema || identity.ApplicationId != expectedApplicationId)
            throw new InvalidDataException("Application identity does not match expected registered app.");
        return identity;
    }

    public static IReadOnlyList<LocalAppContextFileV046> Inventory(
        string root,
        bool includeSourceSidecar,
        int maxFiles,
        long maxTotalBytes,
        CancellationToken cancellationToken)
    {
        RejectReparse(root, "inventory root");
        var files = new List<LocalAppContextFileV046>();
        var stack = new Stack<string>();
        stack.Push(root);
        long total = 0;
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            RejectReparse(current, "inventory directory");
            foreach (var dir in Directory.EnumerateDirectories(current).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                RejectReparse(dir, "inventory subdirectory");
                stack.Push(dir);
            }
            foreach (var file in Directory.EnumerateFiles(current).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                RejectReparse(file, "inventory file");
                var relative = NormalizeRelative(Path.GetRelativePath(root, file));
                if (!includeSourceSidecar && relative.Equals(LocalAppSourceBindingV046Service.BindingFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var bytes = new FileInfo(file).Length;
                total += bytes;
                if (files.Count + 1 > maxFiles || total > maxTotalBytes)
                    throw new InvalidDataException($"Local-app inventory exceeds bounds. files>{maxFiles} or bytes>{maxTotalBytes}");
                files.Add(new LocalAppContextFileV046(relative, HashFile(file), bytes, RoleFor(relative)));
            }
        }
        var duplicates = files.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToArray();
        if (duplicates.Length > 0) throw new InvalidDataException("Windows case-colliding paths are not allowed.");
        return files.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
    }

    public static string ComputeTreeDigest(IEnumerable<LocalAppContextFileV046> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var item in files.OrderBy(x => x.Path, StringComparer.Ordinal))
            hash.AppendData(Encoding.UTF8.GetBytes($"{item.Path}\0{item.Sha256.ToLowerInvariant()}\0{item.Bytes}\n"));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string RequireWorkbenchArtifactDirectory(string workspaceRoot, string child)
    {
        var workspace = ResolveWorkspaceRoot(workspaceRoot);
        var workbench = Path.GetFullPath(Path.Combine(workspace, "Workbench"));
        if (!Directory.Exists(workbench)) throw new InvalidDataException($"Workbench root missing: {workbench}");
        var artifacts = Path.Combine(workbench, "artifacts", child);
        Directory.CreateDirectory(artifacts);
        return artifacts;
    }

    public static string NormalizeRelative(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized) || normalized.StartsWith('/') ||
            normalized == ".." || normalized.StartsWith("../") || normalized.Contains("/../") || normalized.Contains(':'))
            throw new InvalidDataException($"Unsafe relative path: {path}");
        return normalized;
    }

    public static void EnsureInsideRoot(string root, string path, string role)
    {
        var rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{role} escapes fixed root: {full}");
    }

    public static void RejectReparse(string path, string role)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Reparse points are refused at {role}: {path}");
    }

    public static void EnsureNoReparseBoundary(string root, string relativePath)
    {
        var current = Path.GetFullPath(root);
        foreach (var part in NormalizeRelative(relativePath).Split('/'))
        {
            current = Path.Combine(current, part);
            if (File.Exists(current) || Directory.Exists(current)) RejectReparse(current, "path boundary");
        }
    }

    public static string SafeToken(string value)
        => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '_'));

    private static bool IsSafeApplicationId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && char.IsLetterOrDigit(value[0]) &&
           value.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-');

    private static void EnsureDirectChild(string parent, string child, string role)
    {
        var observedParent = Directory.GetParent(child)?.FullName;
        if (observedParent is null || !Path.GetFullPath(observedParent).Equals(Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{role} must be a direct child of fixed root.");
    }

    private static string RoleFor(string path)
    {
        if (path.Equals(LocalApplicationMaintenanceService.IdentityFileName, StringComparison.OrdinalIgnoreCase)) return "identity";
        if (path.Equals(LocalAppSourceBindingV046Service.BindingFileName, StringComparison.OrdinalIgnoreCase)) return "source-binding";
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "executable";
        if (path.StartsWith("data/evidence/", StringComparison.OrdinalIgnoreCase)) return "private-evidence";
        if (path.StartsWith("data/history/", StringComparison.OrdinalIgnoreCase)) return "historical-potentially-superseded";
        if (path.StartsWith("data/imported/", StringComparison.OrdinalIgnoreCase)) return "mutable-imported";
        if (path.Equals("data/state.json", StringComparison.OrdinalIgnoreCase)) return "canonical-current-state-candidate";
        if (path.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) return "application-data";
        if (path.StartsWith("web/", StringComparison.OrdinalIgnoreCase)) return "web-ui";
        return "ordinary";
    }
}
