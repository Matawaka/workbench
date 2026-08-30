using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record WorkbenchUpdateManifestFile(string Path, string Sha256);

public sealed record WorkbenchUpdatePackageManifest(
    string Schema,
    string PackageVersion,
    string TargetVersion,
    string PredecessorTag,
    string PredecessorCommit,
    string TargetTag,
    string PayloadRoot,
    IReadOnlyList<WorkbenchUpdateManifestFile> Files,
    bool NetworkAccessRequested,
    bool CatalogMutationRequested,
    bool AgentExecuteRequested,
    bool ArbitraryProcessExecutionRequested,
    bool InstallerScriptExecutionRequested,
    IReadOnlyList<string> NonEffects);

public sealed record WorkbenchUpdatePlanReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string PackageFileName,
    string PackageSha256,
    long PackageBytes,
    string ManifestSchema,
    string TargetVersion,
    string PredecessorTag,
    string PredecessorCommit,
    string TargetTag,
    string CurrentHead,
    IReadOnlyList<string> CurrentTags,
    bool PredecessorTagMatched,
    bool PredecessorCommitMatched,
    bool PackageStructureValidated,
    bool PayloadDigestsValidated,
    int PayloadFileCount,
    long PayloadBytes,
    IReadOnlyList<WorkbenchUpdateManifestFile> PayloadFiles,
    bool MaterializationAuthorized,
    bool BuildAuthorized,
    bool CheckpointAuthorized,
    string Status,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// v0.10 performs local package intake and planning only. It never extracts a
/// payload, executes an installer, builds a candidate, commits, tags, fetches,
/// pushes, or mutates catalog repositories. A future separately-authorized
/// materialization gate may consume a plan receipt.
/// </summary>
public sealed class LocalUpdateIntakeService
{
    public const string ManifestSchema = "matawaka.workbench-update-package/v0.10";
    public const string PlanSchema = "matawaka.workbench-update-plan-receipt/v0.10";
    public const string Version = "0.10.0";
    private const long MaxPackageBytes = 100L * 1024 * 1024;
    private const long MaxPayloadBytes = 80L * 1024 * 1024;
    private const long MaxSingleFileBytes = 16L * 1024 * 1024;
    private const int MaxFiles = 500;
    private const int MaxManifestBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<(WorkbenchUpdatePlanReceipt Receipt, string ArtifactPath)> PlanAsync(
        string packagePath,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            throw new InvalidDataException("Local Workbench update package is missing.");

        var package = new FileInfo(packagePath);
        if (package.Length <= 0 || package.Length > MaxPackageBytes)
            throw new InvalidDataException($"Update package size must be 1..{MaxPackageBytes} bytes.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var currentHead = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Trim();
        var currentTags = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD"))
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        var packageSha = HashFile(package.FullName);
        WorkbenchUpdatePackageManifest manifest;
        IReadOnlyList<WorkbenchUpdateManifestFile> observedFiles;
        long payloadBytes;

        using (var archive = ZipFile.OpenRead(package.FullName))
        {
            var fileEntries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
            if (fileEntries.Length == 0 || fileEntries.Length > MaxFiles + 1)
                throw new InvalidDataException("Update package has an invalid bounded file count.");

            foreach (var entry in fileEntries)
                ValidateEntryName(entry.FullName);

            var manifestEntry = fileEntries.SingleOrDefault(entry =>
                string.Equals(entry.FullName, "workbench-update-manifest.json", StringComparison.Ordinal));
            if (manifestEntry is null)
                throw new InvalidDataException("workbench-update-manifest.json is required at package root.");
            if (manifestEntry.Length <= 0 || manifestEntry.Length > MaxManifestBytes)
                throw new InvalidDataException("Update manifest has an invalid size.");

            await using (var stream = manifestEntry.Open())
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false))
            {
                var text = await reader.ReadToEndAsync(cancellationToken);
                manifest = JsonSerializer.Deserialize<WorkbenchUpdatePackageManifest>(text, JsonOptions)
                    ?? throw new InvalidDataException("Update manifest could not be parsed.");
            }

            ValidateManifest(manifest);
            var expected = manifest.Files
                .ToDictionary(item => NormalizeRelativePath(item.Path), item => item.Sha256.ToLowerInvariant(), StringComparer.Ordinal);

            var payloadEntries = fileEntries
                .Where(entry => !string.Equals(entry.FullName, "workbench-update-manifest.json", StringComparison.Ordinal))
                .ToArray();
            if (payloadEntries.Length != expected.Count)
                throw new InvalidDataException("Package payload file count differs from manifest.");

            var observed = new List<WorkbenchUpdateManifestFile>();
            long total = 0;
            foreach (var entry in payloadEntries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
            {
                if (!entry.FullName.StartsWith(manifest.PayloadRoot, StringComparison.Ordinal))
                    throw new InvalidDataException($"Unexpected package file outside payload root: {entry.FullName}");

                var relative = NormalizeRelativePath(entry.FullName[manifest.PayloadRoot.Length..]);
                if (!expected.TryGetValue(relative, out var expectedSha))
                    throw new InvalidDataException($"Package contains unmanifested payload file: {relative}");
                if (entry.Length < 0 || entry.Length > MaxSingleFileBytes)
                    throw new InvalidDataException($"Payload file exceeds bounded size: {relative}");

                total = checked(total + entry.Length);
                if (total > MaxPayloadBytes)
                    throw new InvalidDataException("Package payload exceeds bounded total size.");

                using var stream = entry.Open();
                using var sha = SHA256.Create();
                var actualSha = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
                if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Payload SHA-256 mismatch: {relative}");

                observed.Add(new WorkbenchUpdateManifestFile(relative, actualSha));
            }

            observedFiles = observed;
            payloadBytes = total;
        }

        var tagMatched = currentTags.Contains(manifest.PredecessorTag, StringComparer.Ordinal);
        var commitMatched = string.Equals(currentHead, manifest.PredecessorCommit, StringComparison.OrdinalIgnoreCase);
        var eligible = tagMatched && commitMatched;
        var nonEffects = new[]
        {
            "no package payload extraction",
            "no update materialization",
            "no dotnet build or publish",
            "no git add/commit/tag",
            "no git fetch or push",
            "no remote change",
            "no catalog repository mutation",
            "no network access",
            "no agent Execute authority",
            "no installer script execution",
            "plan receipt does not authorize a later materialization"
        };

        var receipt = new WorkbenchUpdatePlanReceipt(
            PlanSchema,
            Version,
            DateTimeOffset.Now,
            package.Name,
            packageSha,
            package.Length,
            manifest.Schema,
            manifest.TargetVersion,
            manifest.PredecessorTag,
            manifest.PredecessorCommit,
            manifest.TargetTag,
            currentHead,
            currentTags,
            tagMatched,
            commitMatched,
            true,
            true,
            observedFiles.Count,
            payloadBytes,
            observedFiles,
            false,
            false,
            false,
            eligible ? "READY_FOR_SEPARATE_MATERIALIZATION_AUTHORITY" : "BLOCKED_PREDECESSOR_MISMATCH",
            nonEffects,
            "Workbench v0.10 intake verifies a local bounded package and predecessor relationship only. It does not extract or apply the update. Materialization, build and checkpoint remain separate future authority gates.");

        var artifactDir = Path.Combine(repositoryRoot, "artifacts", "update-plans");
        Directory.CreateDirectory(artifactDir);
        var artifactPath = Path.Combine(artifactDir, $"update-plan-v0.10-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return (receipt, artifactPath);
    }

    private static void ValidateManifest(WorkbenchUpdatePackageManifest manifest)
    {
        if (!string.Equals(manifest.Schema, ManifestSchema, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported update manifest schema: {manifest.Schema}");
        if (!string.Equals(manifest.PackageVersion, "0.10", StringComparison.Ordinal))
            throw new InvalidDataException("Update package contract version must be 0.10.");
        if (string.IsNullOrWhiteSpace(manifest.TargetVersion) || string.IsNullOrWhiteSpace(manifest.PredecessorTag) ||
            string.IsNullOrWhiteSpace(manifest.PredecessorCommit) || string.IsNullOrWhiteSpace(manifest.TargetTag))
            throw new InvalidDataException("Update manifest target/predecessor identity is incomplete.");
        if (!string.Equals(manifest.PayloadRoot, "payload/", StringComparison.Ordinal))
            throw new InvalidDataException("Update payload root must be exactly payload/.");
        if (manifest.Files is null || manifest.Files.Count == 0 || manifest.Files.Count > MaxFiles)
            throw new InvalidDataException("Update manifest has an invalid bounded file list.");
        if (manifest.NetworkAccessRequested || manifest.CatalogMutationRequested || manifest.AgentExecuteRequested ||
            manifest.ArbitraryProcessExecutionRequested || manifest.InstallerScriptExecutionRequested)
            throw new InvalidDataException("v0.10 intake rejects packages requesting network, catalog mutation, agent Execute, arbitrary process, or installer-script execution.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            var normalized = NormalizeRelativePath(file.Path);
            if (!seen.Add(normalized))
                throw new InvalidDataException($"Duplicate payload path in manifest: {normalized}");
            if (string.IsNullOrWhiteSpace(file.Sha256) || file.Sha256.Length != 64 || file.Sha256.Any(ch => !Uri.IsHexDigit(ch)))
                throw new InvalidDataException($"Invalid payload SHA-256: {normalized}");
        }
    }

    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("/", StringComparison.Ordinal) ||
            name.StartsWith('\\') || name.Contains('\\') || name.Contains(':') || name.Contains('\0'))
            throw new InvalidDataException($"Unsafe ZIP entry path: {name}");

        var parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".."))
            throw new InvalidDataException($"ZIP path traversal rejected: {name}");
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Empty payload path.");
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 || normalized.Contains(':') || normalized.Contains('\0'))
            throw new InvalidDataException($"Unsafe payload path: {path}");
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".."))
            throw new InvalidDataException($"Payload path traversal rejected: {path}");
        return string.Join('/', parts);
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static async Task<string> RunGitReadOnlyAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed read-only git operation failed: {stderr.Trim()}");
        return stdout;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
