using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.35.1 local checkpoint successor over exact accepted v0.35.
/// It exists only to accept the lifecycle version-key stabilization patch.
/// Publication and lifecycle assessment remain separate later actions.
/// </summary>
public sealed class LocalCheckpointV0351Service
{
    public const string Version = "0.35.1";
    public const string ExpectedPredecessorCommit = "689cdf5ef2f9f403efe09bb251c91da1c5951ec6";
    public const string ExpectedPredecessorTag = "workbench-v0.35-accepted";
    public const string TargetTag = "workbench-v0.35.1-accepted";
    public const string CommitMessage = "Checkpoint Workbench v0.35.1 lifecycle version-key stabilization";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
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
        if (!acceptance.Passed ||
            !string.Equals(acceptance.Version, Version, StringComparison.Ordinal) ||
            !string.Equals(acceptance.Schema, "matawaka.workbench-acceptance-receipt/v0.35.1", StringComparison.Ordinal))
            throw new InvalidDataException("A passing Workbench v0.35.1 Self-test receipt is required before stabilization checkpoint acceptance.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var acceptancePath = ValidateAcceptanceArtifact(repositoryRoot, acceptanceArtifactPath, acceptance);
        VerifyRunningExecutable(acceptance.AppExecutableSha256);

        var currentHead = RequireSha(
            (await RunGitAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Stdout,
            "HEAD");
        if (!string.Equals(currentHead, ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"HEAD {currentHead} is not exact accepted v0.35 predecessor {ExpectedPredecessorCommit}.");

        var predecessorTagHead = RequireSha(
            (await RunGitAsync(repositoryRoot, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout,
            ExpectedPredecessorTag);
        if (!string.Equals(currentHead, predecessorTagHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Accepted predecessor tag {ExpectedPredecessorTag} is not at exact current HEAD.");

        var existingTag = (await RunGitAsync(repositoryRoot, cancellationToken, "tag", "--list", TargetTag)).Stdout.Trim();
        if (!string.IsNullOrWhiteSpace(existingTag))
            throw new InvalidDataException($"Target stabilization tag already exists: {TargetTag}");

        var userName = (await RunGitAsync(repositoryRoot, cancellationToken, true, "config", "--get", "user.name")).Stdout.Trim();
        var userEmail = (await RunGitAsync(repositoryRoot, cancellationToken, true, "config", "--get", "user.email")).Stdout.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(userEmail))
            throw new InvalidDataException("Local Git identity is missing. user.name and user.email must already be configured.");

        var changedFiles = ParseStatusPaths(
            (await RunGitAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (changedFiles.Count == 0)
            throw new InvalidDataException("There are no Workbench v0.35.1 stabilization source changes to checkpoint.");
        if (changedFiles.Any(IsForbiddenCheckpointPath))
            throw new InvalidDataException("v0.35.1 checkpoint contains a forbidden artifacts/.workbench path.");

        var (buildManifestPath, buildManifestSha256) = ValidateBuildSourceManifest(
            repositoryRoot, currentHead, changedFiles);

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
        if (!string.Equals(candidate.Version, Version, StringComparison.Ordinal) ||
            !string.Equals(candidate.PreviousHead, ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.ExpectedPredecessorTag, ExpectedPredecessorTag, StringComparison.Ordinal) ||
            !string.Equals(candidate.TargetTag, TargetTag, StringComparison.Ordinal))
            throw new InvalidDataException("Checkpoint candidate does not match the fixed v0.35.1 stabilization contract.");

        var repositoryRoot = candidate.RepositoryRoot;
        var headBefore = RequireSha(
            (await RunGitAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Stdout,
            "HEAD");
        if (!string.Equals(headBefore, candidate.PreviousHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed after v0.35.1 checkpoint preview.");

        var currentStatus = ParseStatusPaths(
            (await RunGitAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (!currentStatus.SequenceEqual(candidate.ChangedFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Workbench working tree changed after v0.35.1 checkpoint preview.");

        var tagCreated = false;
        var commitCreated = false;
        try
        {
            await RunGitAsync(repositoryRoot, cancellationToken, "add", "-A", "--", ".");
            await RunGitAsync(repositoryRoot, cancellationToken, "commit", "-m", CommitMessage);
            commitCreated = true;

            var newHead = RequireSha(
                (await RunGitAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Stdout,
                "new HEAD");
            if (string.Equals(newHead, candidate.PreviousHead, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Git commit did not advance v0.35.1 HEAD.");

            await RunGitAsync(
                repositoryRoot,
                cancellationToken,
                "tag", "-a", TargetTag,
                "-m",
                "Accepted Workbench v0.35.1: lifecycle tag/schema-token and semantic-Version binding stabilized; no new product authority");
            tagCreated = true;

            var cleanStatus = (await RunGitAsync(
                repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout;
            if (!string.IsNullOrWhiteSpace(cleanStatus))
                throw new InvalidDataException("Workbench working tree is not clean after v0.35.1 checkpoint commit.");

            var nonEffects = new[]
            {
                "no local application update performed by checkpoint",
                "no catalog repository mutation",
                "no git fetch",
                "no git push",
                "no remote creation/update",
                "no network access",
                "no application or installer launch",
                "no Agent Execute authority",
                "no ActionPermit creation",
                "no remote publication authority inferred from checkpoint",
                "no lifecycle action authority inferred from version-key stabilization",
                "no canonical UU-AAP conformance claim",
                "no Stable Core or interface-registry promotion"
            };
            var authority = new LocalCheckpointAuthorityReceipt(
                "matawaka.workbench-local-checkpoint-authority-receipt/v0.35.1",
                "human-operator-at-workbench-ui",
                "git.local-checkpoint.accept",
                repositoryRoot,
                "explicit v0.35.1 Accept checkpoint button + confirmation after passing v0.35.1 Self-test",
                true,
                true,
                false,
                false,
                false,
                false,
                new[]
                {
                    "git add -A -- .",
                    "git commit -m <fixed-v0.35.1-message>",
                    "git tag -a workbench-v0.35.1-accepted"
                },
                nonEffects);

            return new LocalCheckpointReceipt(
                "matawaka.workbench-local-checkpoint-receipt/v0.35.1",
                Version,
                DateTimeOffset.Now,
                candidate.PreviousHead,
                newHead,
                TargetTag,
                CommitMessage,
                candidate.AcceptanceArtifactPath,
                candidate.AcceptanceArtifactSha256,
                candidate.BuildSourceManifestPath,
                candidate.BuildSourceManifestSha256,
                candidate.AppExecutableSha256,
                candidate.ChangedFiles,
                authority,
                true,
                nonEffects,
                "Local Workbench v0.35.1 stabilization acceptance only. Remote publication and lifecycle v2 assessment remain separate later decisions.");
        }
        catch
        {
            if (tagCreated)
                await RunGitAsync(repositoryRoot, CancellationToken.None, true, "tag", "-d", TargetTag);
            if (commitCreated)
                await RunGitAsync(repositoryRoot, CancellationToken.None, true, "reset", "--mixed", candidate.PreviousHead);
            else
                await RunGitAsync(repositoryRoot, CancellationToken.None, true, "reset");
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
        var path = Path.Combine(directory, $"checkpoint-v0.35.1-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(receipt, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);
        return path;
    }

    private static string ValidateAcceptanceArtifact(
        string repositoryRoot,
        string artifactPath,
        WorkbenchAcceptanceReceipt acceptance)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
            throw new InvalidDataException("Passing v0.35.1 acceptance artifact file is missing.");
        var full = Path.GetFullPath(artifactPath);
        var allowedRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "acceptance")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance artifact must be under Workbench/artifacts/acceptance.");
        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(
            File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.35.1 acceptance artifact could not be parsed.");
        if (!parsed.Passed ||
            !string.Equals(parsed.Schema, "matawaka.workbench-acceptance-receipt/v0.35.1", StringComparison.Ordinal) ||
            !string.Equals(parsed.Version, Version, StringComparison.Ordinal) ||
            !string.Equals(parsed.RunId, acceptance.RunId, StringComparison.Ordinal) ||
            !string.Equals(parsed.AppExecutableSha256, acceptance.AppExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance artifact does not match the passing in-memory v0.35.1 Self-test receipt.");
        return full;
    }

    private static (string Path, string Sha256) ValidateBuildSourceManifest(
        string repositoryRoot,
        string predecessorHead,
        IReadOnlyList<string> changedFiles)
    {
        var checkpointDir = Path.Combine(repositoryRoot, "artifacts", "checkpoints");
        var manifestPath = Directory.Exists(checkpointDir)
            ? Directory.GetFiles(
                    checkpointDir,
                    "v0.35.1-source-manifest*.json",
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new InvalidDataException("v0.35.1 build source manifest is missing. Refusing source not byte-bound to the built candidate.");

        var manifest = JsonSerializer.Deserialize<BuildSourceManifest>(
            File.ReadAllText(manifestPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.35.1 build source manifest could not be parsed.");
        if (!string.Equals(manifest.Schema, "matawaka.workbench-build-source-manifest/v0.35", StringComparison.Ordinal) ||
            !string.Equals(manifest.Version, Version, StringComparison.Ordinal) ||
            !string.Equals(manifest.PredecessorGitSha, predecessorHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected v0.35.1 build source manifest identity/predecessor.");

        var manifestFiles = manifest.Files.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
        var currentFiles = changedFiles.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!manifestFiles.Select(item => item.Path).SequenceEqual(currentFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Current changed-file set differs from the v0.35.1 source set captured at build time.");

        foreach (var item in manifestFiles)
        {
            var full = Path.GetFullPath(Path.Combine(repositoryRoot, item.Path.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = Path.GetFullPath(repositoryRoot) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                throw new InvalidDataException($"Build-bound v0.35.1 source file missing or escapes Workbench root: {item.Path}");
            if (!string.Equals(HashFile(full), item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"v0.35.1 source file changed after candidate build: {item.Path}");
        }
        return (manifestPath, HashFile(manifestPath));
    }

    private static void VerifyRunningExecutable(string expectedSha256)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidDataException("Running Workbench executable cannot be resolved.");
        if (!string.Equals(HashFile(path), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Running executable digest no longer matches v0.35.1 Self-test receipt.");
    }

    private static IReadOnlyList<string> ParseStatusPaths(string stdout)
        => stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length >= 4 ? line[3..].Trim() : line.Trim())
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static bool IsForbiddenCheckpointPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".workbench/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("../", StringComparison.Ordinal);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RequireSha(string value, string name)
    {
        var sha = value.Trim();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidDataException($"{name} is not a Git SHA-1: {sha}");
        return sha.ToLowerInvariant();
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

    private static Task<GitResult> RunGitAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        params string[] args)
        => RunGitAsync(repositoryRoot, cancellationToken, false, args);

    private static async Task<GitResult> RunGitAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        bool allowFailure,
        params string[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeout);
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
            throw new InvalidDataException("Failed to start fixed v0.35.1 checkpoint Git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException($"v0.35.1 checkpoint Git operation exceeded {GitTimeout.TotalSeconds:0}s timeout.");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 && !allowFailure)
            throw new InvalidDataException($"v0.35.1 checkpoint Git operation failed: {stderr.Trim()}");
        return new GitResult(process.ExitCode, stdout, stderr);
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
