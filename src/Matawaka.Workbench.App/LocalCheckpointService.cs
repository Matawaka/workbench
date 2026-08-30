using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalCheckpointCandidate(
    string Version,
    string RepositoryRoot,
    string PreviousHead,
    string ExpectedPredecessorTag,
    string TargetTag,
    string CommitMessage,
    string AcceptanceArtifactPath,
    string AcceptanceArtifactSha256,
    string BuildSourceManifestPath,
    string BuildSourceManifestSha256,
    string AppExecutableSha256,
    IReadOnlyList<string> ChangedFiles);

public sealed record BuildSourceManifestFile(string Path, string Sha256);

public sealed record BuildSourceManifest(
    string Schema,
    string Version,
    string PredecessorGitSha,
    DateTimeOffset ObservedAt,
    IReadOnlyList<BuildSourceManifestFile> Files);

public sealed record LocalCheckpointAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string TargetRepository,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool FixedGitInvocationOnly,
    bool ArbitraryProcessExecutionAllowed,
    bool CatalogMutationAllowed,
    bool RemotePushAllowed,
    bool NetworkAccessAllowed,
    IReadOnlyList<string> AllowedGitOperations,
    IReadOnlyList<string> NonEffects);

public sealed record LocalCheckpointReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string PreviousHead,
    string NewHead,
    string Tag,
    string CommitMessage,
    string AcceptanceArtifactPath,
    string AcceptanceArtifactSha256,
    string BuildSourceManifestPath,
    string BuildSourceManifestSha256,
    string AppExecutableSha256,
    IReadOnlyList<string> ChangedFiles,
    LocalCheckpointAuthorityReceipt Authority,
    bool WorkingTreeCleanAfterCommit,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Explicitly user-authorized local checkpoint operation for the Workbench's
/// own Git repository. It is intentionally separate from agent Execute.
/// It never pushes, fetches, mutates catalog repositories, or accepts command-
/// supplied executable paths/arguments.
/// </summary>
public sealed class LocalCheckpointService
{
    public const string Version = "0.17.0";
    public const string ExpectedPredecessorTag = "workbench-v0.16-accepted";
    public const string TargetTag = "workbench-v0.17-accepted";
    public const string CommitMessage = "Checkpoint Workbench v0.17 accepted recovery planning gate";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<LocalCheckpointCandidate> PreviewAsync(
        string workspaceRoot,
        string acceptanceArtifactPath,
        WorkbenchAcceptanceReceipt acceptance,
        CancellationToken cancellationToken)
    {
        if (!acceptance.Passed)
            throw new InvalidDataException("A passing Workbench self-test receipt is required before local checkpoint acceptance.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var acceptancePath = ValidateAcceptanceArtifact(repositoryRoot, acceptanceArtifactPath, acceptance);
        VerifyRunningExecutable(acceptance.AppExecutableSha256);

        var currentHead = (await RunGitAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Stdout.Trim();
        if (string.IsNullOrWhiteSpace(currentHead))
            throw new InvalidDataException("Workbench Git repository has no HEAD.");

        var predecessorTagHead = (await RunGitAsync(repositoryRoot, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout.Trim();
        if (!string.Equals(currentHead, predecessorTagHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"HEAD {currentHead} is not accepted predecessor {ExpectedPredecessorTag} ({predecessorTagHead}).");

        var existingTag = (await RunGitAsync(repositoryRoot, cancellationToken, "tag", "--list", TargetTag)).Stdout.Trim();
        if (!string.IsNullOrWhiteSpace(existingTag))
            throw new InvalidDataException($"Target tag already exists: {TargetTag}");

        var userName = (await RunGitAsync(repositoryRoot, cancellationToken, "config", "--get", "user.name", allowExitOne: true)).Stdout.Trim();
        var userEmail = (await RunGitAsync(repositoryRoot, cancellationToken, "config", "--get", "user.email", allowExitOne: true)).Stdout.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(userEmail))
            throw new InvalidDataException("Local Git identity is missing. user.name and user.email must already be configured for the Workbench repository.");

        var status = await RunGitAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        var changedFiles = ParseStatusPaths(status.Stdout);
        if (changedFiles.Count == 0)
            throw new InvalidDataException("There are no Workbench source changes to checkpoint.");

        if (changedFiles.Any(IsForbiddenCheckpointPath))
            throw new InvalidDataException("Checkpoint candidate contains a forbidden artifacts/.workbench path. Acceptance artifacts must remain outside the Git checkpoint.");

        var (buildManifestPath, buildManifestSha256) = ValidateBuildSourceManifest(repositoryRoot, currentHead, changedFiles);

        return new LocalCheckpointCandidate(
            Version,
            repositoryRoot,
            currentHead,
            ExpectedPredecessorTag,
            TargetTag,
            CommitMessage,
            acceptancePath,
            HashFile(acceptancePath),
            buildManifestPath,
            buildManifestSha256,
            acceptance.AppExecutableSha256,
            changedFiles);
    }

    public async Task<LocalCheckpointReceipt> AcceptAsync(
        LocalCheckpointCandidate candidate,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = candidate.RepositoryRoot;
        var headBefore = (await RunGitAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Stdout.Trim();
        if (!string.Equals(headBefore, candidate.PreviousHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed after checkpoint preview. Run Self-test and Preview again.");

        var currentStatus = ParseStatusPaths((await RunGitAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (!currentStatus.SequenceEqual(candidate.ChangedFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Workbench working tree changed after checkpoint preview. Refusing stale acceptance.");

        var tagCreated = false;
        var commitCreated = false;
        try
        {
            await RunGitAsync(repositoryRoot, cancellationToken, "add", "-A", "--", ".");
            await RunGitAsync(repositoryRoot, cancellationToken, "commit", "-m", candidate.CommitMessage);
            commitCreated = true;

            var newHead = (await RunGitAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Stdout.Trim();
            if (string.Equals(newHead, candidate.PreviousHead, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Git commit did not advance HEAD.");

            await RunGitAsync(repositoryRoot, cancellationToken,
                "tag", "-a", candidate.TargetTag,
                "-m", "Accepted Workbench v0.17: recovery planning remains read-only and authority-separated through local checkpoint");
            tagCreated = true;

            var cleanStatus = (await RunGitAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1")).Stdout;
            if (!string.IsNullOrWhiteSpace(cleanStatus))
                throw new InvalidDataException("Workbench working tree is not clean after checkpoint commit.");

            var authority = new LocalCheckpointAuthorityReceipt(
                "matawaka.workbench-local-checkpoint-authority-receipt/v0.17",
                "human-operator-at-workbench-ui",
                "git.local-checkpoint.accept",
                repositoryRoot,
                "explicit Accept checkpoint button + confirmation dialog after passing Self-test",
                true,
                true,
                false,
                false,
                false,
                false,
                new[] { "git add -A -- .", "git commit -m <fixed-v0.17-message>", "git tag -a <fixed-v0.17-tag>" },
                new[]
                {
                    "no catalog repository mutation",
                    "no git fetch",
                    "no git push",
                    "no remote creation/update",
                    "no network access",
                    "no agent Execute authority",
                    "no ActionPermit creation",
                    "no materialization authority inferred for catalog repositories",
                    "no arbitrary command or executable path accepted from JSON"
                });

            return new LocalCheckpointReceipt(
                "matawaka.workbench-local-checkpoint-receipt/v0.17",
                Version,
                DateTimeOffset.Now,
                candidate.PreviousHead,
                newHead,
                candidate.TargetTag,
                candidate.CommitMessage,
                candidate.AcceptanceArtifactPath,
                candidate.AcceptanceArtifactSha256,
                candidate.BuildSourceManifestPath,
                candidate.BuildSourceManifestSha256,
                candidate.AppExecutableSha256,
                candidate.ChangedFiles,
                authority,
                true,
                authority.NonEffects,
                "Workbench-local Git checkpoint only. This is an explicitly confirmed human maintenance operation and is not agent Execute, remote publication, canonical UU-AAP conformance, or authority over Matawaka catalog repositories.");
        }
        catch
        {
            if (tagCreated)
                await RunGitAsync(repositoryRoot, CancellationToken.None, "tag", "-d", candidate.TargetTag, allowFailure: true);

            if (commitCreated)
                await RunGitAsync(repositoryRoot, CancellationToken.None, "reset", "--mixed", candidate.PreviousHead, allowFailure: true);
            else
                await RunGitAsync(repositoryRoot, CancellationToken.None, "reset", allowFailure: true);

            throw;
        }
    }

    public static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        LocalCheckpointReceipt receipt,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(ResolveRepositoryRoot(workspaceRoot), "artifacts", "acceptance");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"checkpoint-v0.17-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
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

    private static string ValidateAcceptanceArtifact(
        string repositoryRoot,
        string artifactPath,
        WorkbenchAcceptanceReceipt acceptance)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
            throw new InvalidDataException("Passing acceptance artifact file is missing.");

        var full = Path.GetFullPath(artifactPath);
        var allowedRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "acceptance")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance artifact must be under Workbench/artifacts/acceptance.");

        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("Acceptance artifact could not be parsed.");

        if (!parsed.Passed || !string.Equals(parsed.RunId, acceptance.RunId, StringComparison.Ordinal))
            throw new InvalidDataException("Acceptance artifact does not match the passing in-memory Self-test receipt.");
        if (!string.Equals(parsed.AppExecutableSha256, acceptance.AppExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance artifact executable digest mismatch.");

        return full;
    }

    private static (string Path, string Sha256) ValidateBuildSourceManifest(
        string repositoryRoot,
        string predecessorHead,
        IReadOnlyList<string> changedFiles)
    {
        var checkpointDir = Path.Combine(repositoryRoot, "artifacts", "checkpoints");
        var manifestPath = Directory.Exists(checkpointDir)
            ? Directory.GetFiles(checkpointDir, "v0.17.0-source-manifest*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new InvalidDataException("v0.17 build source manifest is missing. Refusing to checkpoint source that is not byte-bound to the built candidate.");

        var manifest = JsonSerializer.Deserialize<BuildSourceManifest>(File.ReadAllText(manifestPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.17 build source manifest could not be parsed.");
        if (!string.Equals(manifest.Schema, "matawaka.workbench-build-source-manifest/v0.17", StringComparison.Ordinal) ||
            !string.Equals(manifest.Version, Version, StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected v0.17 build source manifest schema/version.");
        if (!string.Equals(manifest.PredecessorGitSha, predecessorHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Build source manifest predecessor does not match current accepted HEAD.");

        var manifestFiles = manifest.Files.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
        var currentFiles = changedFiles.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!manifestFiles.Select(item => item.Path).SequenceEqual(currentFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Current Workbench changed-file set differs from the source set captured at build time.");

        foreach (var item in manifestFiles)
        {
            var full = Path.GetFullPath(Path.Combine(repositoryRoot, item.Path.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = Path.GetFullPath(repositoryRoot) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                throw new InvalidDataException($"Build-bound source file missing or escapes Workbench root: {item.Path}");
            var actual = HashFile(full);
            if (!string.Equals(actual, item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Source file changed after build/Self-test candidate creation: {item.Path}");
        }

        return (manifestPath, HashFile(manifestPath));
    }

    private static void VerifyRunningExecutable(string expectedSha256)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidDataException("Running Workbench executable cannot be resolved.");
        var actual = HashFile(path);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Running executable digest no longer matches Self-test receipt. expected={expectedSha256}; actual={actual}");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static IReadOnlyList<string> ParseStatusPaths(string stdout)
    {
        return stdout
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length >= 4 ? line[3..].Trim() : line.Trim())
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsForbiddenCheckpointPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".workbench/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("../", StringComparison.Ordinal);
    }

    private static async Task<GitResult> RunGitAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        string arg0,
        string? arg1 = null,
        string? arg2 = null,
        string? arg3 = null,
        string? arg4 = null,
        string? arg5 = null,
        bool allowExitOne = false,
        bool allowFailure = false)
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

        foreach (var arg in new[] { arg0, arg1, arg2, arg3, arg4, arg5 })
            if (arg is not null) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed git process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var acceptedExit = process.ExitCode == 0 || (allowExitOne && process.ExitCode == 1);
        if (!acceptedExit && !allowFailure)
            throw new InvalidDataException($"Fixed git operation failed ({arg0}), exit={process.ExitCode}: {stderr.Trim()}");

        return new GitResult(process.ExitCode, stdout, stderr);
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
