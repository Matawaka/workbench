using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record WorkbenchStagedSourceChange(
    string Path,
    string Action,
    string? CurrentSha256,
    string StagedSha256,
    long StagedBytes);

public sealed record WorkbenchStagedApplyPlanReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string TargetVersion,
    string TargetTag,
    string PredecessorCommit,
    string CurrentHead,
    string StagingRoot,
    bool MaterializationReceiptEligible,
    bool PredecessorReverified,
    bool WorkingTreeClean,
    bool StagingRootBounded,
    bool ExactStagedFileSetVerified,
    bool StagedPayloadDigestsVerified,
    IReadOnlyList<WorkbenchStagedSourceChange> SourceChanges,
    int AddCount,
    int ReplaceCount,
    int NoOpCount,
    bool SourceMutationAuthorized,
    bool BuildAuthorized,
    bool CheckpointAuthorized,
    string Status,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// v0.12 converts an already-authorized v0.11 staging-only materialization into
/// a fresh read-only source-apply plan. It may inspect repository/staging bytes,
/// run fixed read-only Git queries, and write a local plan receipt. It cannot
/// overwrite tracked Workbench source, build, commit/tag, fetch/push, use the
/// network, mutate catalog repositories, or grant Agent Execute authority.
/// </summary>
public sealed class StagedUpdateApplyPlanService
{
    public const string ReceiptSchema = "matawaka.workbench-staged-apply-plan-receipt/v0.12";
    public const string Version = "0.12.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<(WorkbenchStagedApplyPlanReceipt Receipt, string ArtifactPath)> PlanAsync(
        WorkbenchUpdateMaterializationReceipt materialization,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (materialization is null)
            throw new InvalidDataException("A materialization receipt is required.");

        if (!string.Equals(materialization.Status, "MATERIALIZED_STAGING_ONLY", StringComparison.Ordinal) ||
            !materialization.PackageDigestReverified ||
            !materialization.PredecessorReverified ||
            !materialization.WorkingTreeCleanBeforeMaterialization ||
            !materialization.PayloadDigestsReverifiedAfterWrite ||
            !materialization.Authority.StagingOnly ||
            materialization.Authority.RepositorySourceMutationAllowed ||
            materialization.Authority.BuildAllowed ||
            materialization.Authority.CheckpointAllowed)
            throw new InvalidDataException("The materialization receipt is not eligible for staged source-apply planning.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var currentHead = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Trim();
        if (!string.Equals(currentHead, materialization.PredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed after materialization. Re-plan/re-materialize before source-apply planning.");

        var currentTags = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD"))
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (!currentTags.Contains(materialization.Authority is null ? "" : InferPredecessorTag(materialization), StringComparer.Ordinal))
            throw new InvalidDataException("The accepted predecessor tag no longer points at Workbench HEAD.");

        var status = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        if (!string.IsNullOrWhiteSpace(status))
            throw new InvalidDataException("Workbench working tree must be clean before staged source-apply planning.");

        var stagingRoot = ValidateStagingRoot(repositoryRoot, materialization.StagingRoot);
        var payloadRoot = Path.Combine(stagingRoot, "payload");
        if (!Directory.Exists(payloadRoot))
            throw new InvalidDataException("Materialized payload directory is missing.");

        var expected = materialization.PayloadFiles
            .Select(item => new WorkbenchUpdateManifestFile(NormalizeRelativePath(item.Path), item.Sha256))
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ToArray();

        var actualPaths = Directory.GetFiles(payloadRoot, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(payloadRoot, path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var expectedPaths = expected.Select(item => item.Path).ToArray();
        if (!actualPaths.SequenceEqual(expectedPaths, StringComparer.Ordinal))
            throw new InvalidDataException("Materialized staging contains missing or extra payload files.");

        var changes = new List<WorkbenchStagedSourceChange>();
        foreach (var item in expected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stagedPath = ResolveBoundedPath(payloadRoot, item.Path, "staging");
            if (!File.Exists(stagedPath))
                throw new InvalidDataException($"Materialized payload file missing: {item.Path}");

            var stagedSha = HashFile(stagedPath);
            if (!string.Equals(stagedSha, item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Materialized payload SHA-256 mismatch: {item.Path}");

            var destination = ResolveRepositoryDestination(repositoryRoot, item.Path);
            var currentSha = File.Exists(destination) ? HashFile(destination) : null;
            var action = currentSha is null
                ? "Add"
                : string.Equals(currentSha, stagedSha, StringComparison.OrdinalIgnoreCase)
                    ? "NoOp"
                    : "Replace";

            changes.Add(new WorkbenchStagedSourceChange(
                item.Path,
                action,
                currentSha,
                stagedSha,
                new FileInfo(stagedPath).Length));
        }

        var addCount = changes.Count(item => item.Action == "Add");
        var replaceCount = changes.Count(item => item.Action == "Replace");
        var noOpCount = changes.Count(item => item.Action == "NoOp");

        var nonEffects = new[]
        {
            "no tracked Workbench source overwrite",
            "no repository file add/delete/rename",
            "no dotnet restore/build/test/publish",
            "no installer or arbitrary process execution",
            "no git add/commit/tag",
            "no git fetch or push",
            "no remote change",
            "no catalog repository mutation",
            "no network access",
            "no agent Execute authority",
            "staged apply plan does not authorize source apply, build, checkpoint, or publication"
        };

        var receipt = new WorkbenchStagedApplyPlanReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            materialization.TargetVersion,
            materialization.TargetTag,
            materialization.PredecessorCommit,
            currentHead,
            stagingRoot,
            true,
            true,
            true,
            true,
            true,
            true,
            changes,
            addCount,
            replaceCount,
            noOpCount,
            false,
            false,
            false,
            addCount + replaceCount > 0 ? "READY_FOR_SEPARATE_SOURCE_APPLY_AUTHORITY" : "NO_SOURCE_CHANGE",
            nonEffects,
            "v0.12 proves the exact staged source delta without applying it. A READY receipt is evidence of a bounded possible source transition, not authority to mutate source, build, checkpoint, publish, mutate catalog repositories, or execute an agent action.");

        var artifactDir = Path.Combine(repositoryRoot, "artifacts", "update-apply-plans");
        Directory.CreateDirectory(artifactDir);
        var artifactPath = Path.Combine(artifactDir, $"staged-apply-plan-v0.12-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(
            artifactPath,
            JsonSerializer.Serialize(receipt, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);

        return (receipt, artifactPath);
    }

    private static string InferPredecessorTag(WorkbenchUpdateMaterializationReceipt materialization)
    {
        // v0.11 materialization is tied to the accepted predecessor encoded in
        // the package plan. For the v0.12 transition this is the v0.11 accepted tag.
        // Future versions should carry predecessor tag explicitly in the
        // materialization receipt instead of extending this compatibility bridge.
        if (string.Equals(materialization.PredecessorCommit, "990df6a47ea3b4b7f321d4b9eeff6ecf884ebaf3", StringComparison.OrdinalIgnoreCase))
            return "workbench-v0.11-accepted";

        throw new InvalidDataException("Unsupported materialization predecessor for v0.12 apply planning.");
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static string ValidateStagingRoot(string repositoryRoot, string stagingRoot)
    {
        if (string.IsNullOrWhiteSpace(stagingRoot))
            throw new InvalidDataException("Materialization staging root is missing.");

        var allowedParent = Path.GetFullPath(Path.Combine(repositoryRoot, ".workbench", "update-materializations")) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(stagingRoot.Trim());
        var candidateWithSeparator = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidateWithSeparator.StartsWith(allowedParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Materialization staging root escapes the fixed Workbench staging parent.");
        if (!Directory.Exists(candidate))
            throw new InvalidDataException("Materialization staging root no longer exists.");
        return candidate;
    }

    private static string ResolveRepositoryDestination(string repositoryRoot, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, ".git", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(".workbench/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Update payload target is not allowed in repository source: {relativePath}");

        return ResolveBoundedPath(repositoryRoot, normalized, "repository");
    }

    private static string ResolveBoundedPath(string rootPath, string relativePath, string label)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Update path escapes {label} root: {relativePath}");
        return destination;
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Empty update path.");
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 || normalized.Contains(':') || normalized.Contains('\0'))
            throw new InvalidDataException($"Unsafe update path: {path}");
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".."))
            throw new InvalidDataException($"Update path traversal rejected: {path}");
        return string.Join('/', parts);
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
        if (!process.Start())
            throw new InvalidDataException("Failed to start fixed read-only git process.");
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
