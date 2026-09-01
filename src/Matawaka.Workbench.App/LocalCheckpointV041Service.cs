using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed class LocalCheckpointV041Service
{
    public const string Version = "0.41.0";
    public const string ExpectedPredecessorCommit = "45178dfc6488c2e4699b584ac29cbbc9c001c2f3";
    public const string ExpectedPredecessorTag = "workbench-v0.40.1-accepted";
    public const string TargetTag = "workbench-v0.41-accepted";
    public const string CommitMessage = "Checkpoint Workbench v0.41 JSON output search and local-app chat handoff";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public async Task<LocalCheckpointCandidate> PreviewAsync(
        string workspaceRoot,
        string acceptanceArtifactPath,
        WorkbenchAcceptanceReceipt acceptance,
        CancellationToken cancellationToken)
    {
        if (!acceptance.Passed || acceptance.Version != Version || acceptance.Schema != "matawaka.workbench-acceptance-receipt/v0.41")
            throw new InvalidDataException("A passing Workbench v0.41 Self-test receipt is required.");

        var root = ResolveRepositoryRoot(workspaceRoot);
        var acceptancePath = ValidateAcceptanceArtifact(root, acceptanceArtifactPath, acceptance);
        VerifyRunningExecutable(acceptance.AppExecutableSha256);

        var head = RequireSha((await RunGitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"HEAD {head} is not exact accepted v0.40.1 predecessor {ExpectedPredecessorCommit}.");
        var predecessorTagHead = RequireSha((await RunGitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout, ExpectedPredecessorTag);
        if (!predecessorTagHead.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Accepted v0.40.1 predecessor tag is not at exact current HEAD.");
        if (!string.IsNullOrWhiteSpace((await RunGitAsync(root, cancellationToken, "tag", "--list", TargetTag)).Stdout))
            throw new InvalidDataException($"Target tag already exists: {TargetTag}");

        var userName = (await RunGitAsync(root, cancellationToken, true, "config", "--get", "user.name")).Stdout.Trim();
        var userEmail = (await RunGitAsync(root, cancellationToken, true, "config", "--get", "user.email")).Stdout.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(userEmail))
            throw new InvalidDataException("Local Git identity is missing.");

        var changed = ParseStatusPaths((await RunGitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (changed.Count == 0) throw new InvalidDataException("There are no Workbench v0.41 source changes to checkpoint.");
        if (changed.Any(IsForbiddenCheckpointPath)) throw new InvalidDataException("Checkpoint contains forbidden artifacts/.workbench path.");
        var (manifestPath, manifestSha) = ValidateBuildSourceManifest(root, head, changed);

        return new LocalCheckpointCandidate(
            Version, root, head, ExpectedPredecessorTag, TargetTag, CommitMessage,
            acceptancePath, HashFile(acceptancePath), manifestPath, manifestSha,
            acceptance.AppExecutableSha256, changed);
    }

    public Task<LocalCheckpointReceipt> AcceptAsync(LocalCheckpointCandidate candidate, CancellationToken cancellationToken)
        => AcceptCoreAsync(
            candidate,
            "human-operator-at-workbench-ui",
            "explicit v0.41 Accept confirmation after passing v0.41 Self-test",
            explicitUiConfirmationRequired: true,
            bootstrapLeaseId: null,
            cancellationToken);

    public Task<LocalCheckpointReceipt> AcceptFromBootstrapAsync(
        LocalCheckpointCandidate candidate,
        string bootstrapLeaseId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bootstrapLeaseId))
            throw new InvalidDataException("A claimed v0.40 transition-bootstrap lease id is required for automatic v0.41 Accept.");
        return AcceptCoreAsync(
            candidate,
            "human-operator-via-one-shot-transition-bootstrap",
            $"one-shot v0.40 transition-bootstrap lease {bootstrapLeaseId}; originally authorized by explicit Update Workbench confirmation; exact v0.41 Self-test Passed=true required",
            explicitUiConfirmationRequired: false,
            bootstrapLeaseId,
            cancellationToken);
    }

    private async Task<LocalCheckpointReceipt> AcceptCoreAsync(
        LocalCheckpointCandidate candidate,
        string subject,
        string authoritySource,
        bool explicitUiConfirmationRequired,
        string? bootstrapLeaseId,
        CancellationToken cancellationToken)
    {
        if (candidate.Version != Version ||
            !candidate.PreviousHead.Equals(ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase) ||
            candidate.ExpectedPredecessorTag != ExpectedPredecessorTag || candidate.TargetTag != TargetTag)
            throw new InvalidDataException("Checkpoint candidate does not match fixed v0.41 contract.");

        var root = candidate.RepositoryRoot;
        var head = RequireSha((await RunGitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(candidate.PreviousHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed after v0.41 checkpoint preview.");
        var status = ParseStatusPaths((await RunGitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (!status.SequenceEqual(candidate.ChangedFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Workbench working tree changed after v0.41 checkpoint preview.");

        var acceptance = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(candidate.AcceptanceArtifactPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.41 acceptance artifact disappeared before checkpoint.");
        if (!acceptance.Passed || acceptance.Version != Version || acceptance.Schema != "matawaka.workbench-acceptance-receipt/v0.41")
            throw new InvalidDataException("v0.41 local Accept requires a still-passing exact acceptance artifact.");

        var committed = false;
        var tagged = false;
        try
        {
            await RunGitAsync(root, cancellationToken, "add", "-A", "--", ".");
            await RunGitAsync(root, cancellationToken, "commit", "-m", CommitMessage);
            committed = true;
            var newHead = RequireSha((await RunGitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "new HEAD");
            await RunGitAsync(root, cancellationToken, "tag", "-a", TargetTag, "-m",
                "Accepted Workbench v0.41: read-only JSON output search and local-app chat handoff guidance");
            tagged = true;

            if (!string.IsNullOrWhiteSpace((await RunGitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
                throw new InvalidDataException("Workbench working tree is not clean after v0.41 checkpoint.");

            var nonEffects = new[]
            {
                "no remote publication or network operation",
                "no candidate launch performed by checkpoint",
                "no reusable or future automatic acceptance authority",
                "no automatic retry authority",
                "no automatic Publish accepted or Lifecycle receipt",
                "no JSON/output/clipboard mutation authority",
                "no Local Apps import/copy/move authority",
                "no local application registration/update/package build",
                "no catalog mutation",
                "no Agent Execute or ActionPermit",
                "no canonical UU-AAP conformance or Stable Core promotion"
            };
            var allowed = new[]
            {
                "git add -A -- .",
                "git commit -m <fixed-v0.41-message>",
                "git tag -a workbench-v0.41-accepted"
            };
            var authority = new LocalCheckpointAuthorityReceipt(
                bootstrapLeaseId is null
                    ? "matawaka.workbench-local-checkpoint-authority-receipt/v0.41"
                    : "matawaka.workbench-local-checkpoint-bootstrap-authority-receipt/v0.41",
                subject,
                "git.local-checkpoint.accept",
                root,
                authoritySource,
                explicitUiConfirmationRequired,
                true,
                false, false, false, false,
                allowed,
                nonEffects);
            return new LocalCheckpointReceipt(
                "matawaka.workbench-local-checkpoint-receipt/v0.41", Version, DateTimeOffset.Now,
                candidate.PreviousHead, newHead, TargetTag, CommitMessage,
                candidate.AcceptanceArtifactPath, candidate.AcceptanceArtifactSha256,
                candidate.BuildSourceManifestPath, candidate.BuildSourceManifestSha256,
                candidate.AppExecutableSha256, candidate.ChangedFiles, authority, true, nonEffects,
                bootstrapLeaseId is null
                    ? "Local v0.41 checkpoint after explicit manual Accept. Publish/Lifecycle remain separate."
                    : $"Local v0.41 checkpoint consumed one-shot v0.40 transition-bootstrap lease {bootstrapLeaseId} only after exact first-boot Self-test Passed=true. This creates no reusable acceptance or publication authority.");
        }
        catch
        {
            if (tagged) await RunGitAsync(root, CancellationToken.None, true, "tag", "-d", TargetTag);
            if (committed) await RunGitAsync(root, CancellationToken.None, true, "reset", "--mixed", candidate.PreviousHead);
            else await RunGitAsync(root, CancellationToken.None, true, "reset");
            throw;
        }
    }

    public static async Task<string> WriteReceiptAsync(string workspaceRoot, LocalCheckpointReceipt receipt, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(ResolveRepositoryRoot(workspaceRoot), "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"checkpoint-v0.41-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("checkpoint-v041-fixed-predecessor", ExpectedPredecessorCommit == "45178dfc6488c2e4699b584ac29cbbc9c001c2f3", ExpectedPredecessorCommit, "accepted v0.40.1"),
        ("checkpoint-v041-fixed-target-tag", TargetTag == "workbench-v0.41-accepted", TargetTag, "workbench-v0.41-accepted"),
        ("checkpoint-v041-bootstrap-is-not-ui-confirmation", true, "bootstrap authority uses ExplicitUiConfirmationRequired=false", "false"),
        ("checkpoint-v041-publish-separate", true, "no push/fetch in allowed Git operations", "separate")
    };

    private static string ValidateAcceptanceArtifact(string root, string artifactPath, WorkbenchAcceptanceReceipt acceptance)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath)) throw new InvalidDataException("Passing v0.41 acceptance artifact is missing.");
        var full = Path.GetFullPath(artifactPath);
        var allowed = Path.GetFullPath(Path.Combine(root, "artifacts", "acceptance")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Acceptance artifact must be under Workbench/artifacts/acceptance.");
        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.41 acceptance artifact could not be parsed.");
        if (!parsed.Passed || parsed.Schema != "matawaka.workbench-acceptance-receipt/v0.41" || parsed.Version != Version ||
            parsed.RunId != acceptance.RunId || !parsed.AppExecutableSha256.Equals(acceptance.AppExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance artifact does not match the passing in-memory v0.41 Self-test receipt.");
        return full;
    }

    private static (string Path, string Sha256) ValidateBuildSourceManifest(string root, string predecessor, IReadOnlyList<string> changed)
    {
        var dir = Path.Combine(root, "artifacts", "checkpoints");
        var path = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "v0.41.0-source-manifest*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (path is null) throw new InvalidDataException("v0.41 build source manifest is missing.");
        var manifest = JsonSerializer.Deserialize<BuildSourceManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.41 build source manifest could not be parsed.");
        if (manifest.Schema != "matawaka.workbench-build-source-manifest/v0.41" || manifest.Version != Version ||
            !manifest.PredecessorGitSha.Equals(predecessor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected v0.41 build source manifest identity/predecessor.");
        var current = changed.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var bound = manifest.Files.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        if (!bound.Select(x => x.Path).SequenceEqual(current, StringComparer.Ordinal))
            throw new InvalidDataException("Changed-file set differs from v0.41 build source manifest.");
        foreach (var item in bound)
        {
            var full = Path.GetFullPath(Path.Combine(root, item.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(full) || !HashFile(full).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Build-bound source file drift: {item.Path}");
        }
        return (path, HashFile(path));
    }

    private static void VerifyRunningExecutable(string expected)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !HashFile(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Running executable digest does not match v0.41 Self-test receipt.");
    }

    private static IReadOnlyList<string> ParseStatusPaths(string text)
        => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length >= 4 ? line[3..].Trim() : line.Trim())
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static bool IsForbiddenCheckpointPath(string path)
    {
        var n = path.Replace('\\', '/');
        return n.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase) ||
               n.StartsWith(".workbench/", StringComparison.OrdinalIgnoreCase) || n.Contains("../", StringComparison.Ordinal);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RequireSha(string value, string role)
    {
        var sha = value.Trim();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException($"{role} is not a Git SHA-1: {sha}");
        return sha.ToLowerInvariant();
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static Task<GitResult> RunGitAsync(string root, CancellationToken ct, params string[] args)
        => RunGitAsync(root, ct, false, args);

    private static async Task<GitResult> RunGitAsync(string root, CancellationToken ct, bool allowFailure, params string[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(GitTimeout);
        var psi = new ProcessStartInfo
        {
            FileName = "git", WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed v0.41 checkpoint Git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { }
            throw new InvalidDataException("v0.41 checkpoint Git operation timed out.");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 && !allowFailure)
            throw new InvalidDataException($"v0.41 checkpoint Git operation failed: {stderr.Trim()}");
        return new GitResult(process.ExitCode, stdout, stderr);
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
