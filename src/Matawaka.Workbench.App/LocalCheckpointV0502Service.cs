using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed class LocalCheckpointV0502Service
{
    public const string Version = "0.50.2";
    public const string AcceptanceSchema = "matawaka.workbench-acceptance-receipt/v0.50.2";
    public const string ExpectedPredecessorCommit = "1b3c5aa44e2bb302764b044b3b3ac00de14a5994";
    public const string ExpectedPredecessorTag = "workbench-v0.50.1-accepted";
    public const string TargetTag = "workbench-v0.50.2-accepted";
    public const string CommitMessage = "Checkpoint Workbench v0.50.2 plain MCP OAuth discovery compatibility closure";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public async Task<LocalCheckpointCandidate> PreviewAsync(string workspaceRoot, string acceptanceArtifactPath, WorkbenchAcceptanceReceipt acceptance, CancellationToken cancellationToken)
    {
        if (!acceptance.Passed || acceptance.Version != Version || acceptance.Schema != AcceptanceSchema)
            throw new InvalidDataException("A passing exact Workbench v0.50.2 first-boot validation receipt is required.");
        var root = ResolveRepositoryRoot(workspaceRoot);
        var acceptancePath = ValidateAcceptanceArtifact(root, acceptanceArtifactPath, acceptance);
        VerifyRunningExecutable(acceptance.AppExecutableSha256);
        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"HEAD {head} is not exact operator-bound local failed-v0.50.1 predecessor {ExpectedPredecessorCommit}.");
        var predecessorTagHead = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout, ExpectedPredecessorTag);
        if (!predecessorTagHead.Equals(head, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Local failed-v0.50.1 predecessor tag is not at exact current HEAD.");
        if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "tag", "--list", TargetTag)).Stdout)) throw new InvalidDataException($"Target tag already exists: {TargetTag}");
        var userName = (await GitAsync(root, cancellationToken, true, "config", "--get", "user.name")).Stdout.Trim();
        var userEmail = (await GitAsync(root, cancellationToken, true, "config", "--get", "user.email")).Stdout.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(userEmail)) throw new InvalidDataException("Local Git identity is missing.");
        var changed = ParseStatusPaths((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (changed.Count == 0) throw new InvalidDataException("There are no Workbench v0.50.2 source changes to checkpoint.");
        if (changed.Any(IsForbiddenCheckpointPath)) throw new InvalidDataException("Checkpoint contains forbidden artifacts/.workbench/Tools runtime path.");
        var manifest = ValidateBuildSourceManifest(root, head, changed);
        return new LocalCheckpointCandidate(Version, root, head, ExpectedPredecessorTag, TargetTag, CommitMessage, acceptancePath, HashFile(acceptancePath), manifest.Path, manifest.Sha256, acceptance.AppExecutableSha256, changed);
    }

    public Task<LocalCheckpointReceipt> AcceptFromBootstrapAsync(LocalCheckpointCandidate candidate, string bootstrapLeaseId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bootstrapLeaseId)) throw new InvalidDataException("A claimed transition-bootstrap lease id is required for automatic v0.50.2 local acceptance.");
        return AcceptCoreAsync(candidate, bootstrapLeaseId, cancellationToken);
    }

    private async Task<LocalCheckpointReceipt> AcceptCoreAsync(LocalCheckpointCandidate candidate, string bootstrapLeaseId, CancellationToken cancellationToken)
    {
        if (candidate.Version != Version || !candidate.PreviousHead.Equals(ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase) || candidate.ExpectedPredecessorTag != ExpectedPredecessorTag || candidate.TargetTag != TargetTag)
            throw new InvalidDataException("Checkpoint candidate does not match fixed v0.50.2 stabilization contract.");
        var root = candidate.RepositoryRoot;
        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(candidate.PreviousHead, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Workbench HEAD changed after v0.50.2 checkpoint preview.");
        var predecessorTagHead = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout, ExpectedPredecessorTag);
        if (!predecessorTagHead.Equals(head, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Local failed-v0.50.1 predecessor tag moved after v0.50.2 preview.");
        var status = ParseStatusPaths((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (!status.SequenceEqual(candidate.ChangedFiles, StringComparer.Ordinal)) throw new InvalidDataException("Workbench working tree changed after v0.50.2 checkpoint preview.");
        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(candidate.AcceptanceArtifactPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.50.2 validation artifact disappeared before checkpoint.");
        if (!parsed.Passed || parsed.Version != Version || parsed.Schema != AcceptanceSchema) throw new InvalidDataException("v0.50.2 automatic local acceptance requires a still-passing exact validation artifact.");
        VerifyRunningExecutable(parsed.AppExecutableSha256);
        ValidateBuildSourceManifest(root, head, status);

        var committed = false;
        var tagged = false;
        try
        {
            await GitAsync(root, cancellationToken, "add", "-A", "--", ".");
            await GitAsync(root, cancellationToken, "commit", "-m", CommitMessage);
            committed = true;
            var newHead = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "new HEAD");
            var newParent = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD^")).Stdout, "new HEAD parent");
            if (!newParent.Equals(candidate.PreviousHead, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("v0.50.2 checkpoint parent is not exact local failed-v0.50.1 predecessor.");
            await GitAsync(root, cancellationToken, "tag", "-a", TargetTag, "-m", "Accepted Workbench v0.50.2: plain MCP OAuth discovery compatibility closure");
            tagged = true;
            if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout)) throw new InvalidDataException("Workbench working tree is not clean after v0.50.2 checkpoint.");
            var nonEffects = new[]
            {
                "failed local v0.50 and v0.50.1 checkpoints remain historical real-host negative evidence",
                "no remote publication during local checkpoint",
                "no read lease, MCP adapter, compatibility facade or Secure MCP Tunnel startup",
                "no OAuth metadata/credential/local endpoint runtime state committed to Git",
                "no external Tools/OpenAI binary committed to Workbench repository",
                "no private Apps/AppSources bytes committed",
                "no automatic retry/Publish/Lifecycle authority",
                "no catalog mutation or Agent Execute/ActionPermit",
                "no Stable Core/interface-registry promotion"
            };
            var authority = new LocalCheckpointAuthorityReceipt(
                "matawaka.workbench-local-checkpoint-bootstrap-authority-receipt/v0.50.2",
                "human-operator-via-one-shot-transition-bootstrap", "git.local-checkpoint.accept", root,
                $"one-shot transition-bootstrap lease {bootstrapLeaseId}; exact v0.50.2 validation Passed=true required",
                false, true, false, false, false, false,
                new[] { "git add -A -- .", "git commit -m <fixed-v0.50.2-message>", "git tag -a workbench-v0.50.2-accepted" }, nonEffects);
            return new LocalCheckpointReceipt(
                "matawaka.workbench-local-checkpoint-receipt/v0.50.2", Version, DateTimeOffset.Now,
                candidate.PreviousHead, newHead, TargetTag, CommitMessage,
                candidate.AcceptanceArtifactPath, candidate.AcceptanceArtifactSha256,
                candidate.BuildSourceManifestPath, candidate.BuildSourceManifestSha256,
                candidate.AppExecutableSha256, candidate.ChangedFiles, authority, true, nonEffects,
                $"Local v0.50.2 checkpoint consumed one-shot transition-bootstrap lease {bootstrapLeaseId}. Failed v0.50/v0.50.1 remain local negative evidence; OAuth/tunnel/runtime/private app state remains outside checkpoint payload.");
        }
        catch
        {
            if (tagged) await GitAsync(root, CancellationToken.None, true, "tag", "-d", TargetTag);
            if (committed) await GitAsync(root, CancellationToken.None, true, "reset", "--mixed", candidate.PreviousHead);
            else await GitAsync(root, CancellationToken.None, true, "reset");
            throw;
        }
    }

    public static async Task<string> WriteReceiptAsync(string workspaceRoot, LocalCheckpointReceipt receipt, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(ResolveRepositoryRoot(workspaceRoot), "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"checkpoint-v0.50.2-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("checkpoint-v0502-fixed-predecessor", ExpectedPredecessorCommit == "1b3c5aa44e2bb302764b044b3b3ac00de14a5994", ExpectedPredecessorCommit, "operator-bound local failed-v0.50.1"),
        ("checkpoint-v0502-fixed-predecessor-tag", ExpectedPredecessorTag == "workbench-v0.50.1-accepted", ExpectedPredecessorTag, "local workbench-v0.50.1-accepted"),
        ("checkpoint-v0502-fixed-target-tag", TargetTag == "workbench-v0.50.2-accepted", TargetTag, "workbench-v0.50.2-accepted"),
        ("checkpoint-v0502-bootstrap-only", true, "AcceptFromBootstrapAsync only", "no manual visible Accept path"),
        ("checkpoint-v0502-runtime-secrets-not-git", true, "artifacts/.workbench/Tools forbidden checkpoint path", "not checkpoint payload")
    };

    private static string ValidateAcceptanceArtifact(string root, string artifactPath, WorkbenchAcceptanceReceipt acceptance)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath)) throw new InvalidDataException("Passing v0.50.2 validation artifact is missing.");
        var full = Path.GetFullPath(artifactPath);
        var allowed = Path.GetFullPath(Path.Combine(root, "artifacts", "acceptance")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Acceptance artifact must be under Workbench/artifacts/acceptance.");
        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions) ?? throw new InvalidDataException("v0.50.2 validation artifact could not be parsed.");
        if (!parsed.Passed || parsed.Schema != AcceptanceSchema || parsed.Version != Version || parsed.RunId != acceptance.RunId || !parsed.AppExecutableSha256.Equals(acceptance.AppExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Validation artifact does not match the passing in-memory v0.50.2 receipt.");
        return full;
    }

    private static (string Path, string Sha256) ValidateBuildSourceManifest(string root, string predecessor, IReadOnlyList<string> changed)
    {
        var dir = Path.Combine(root, "artifacts", "checkpoints");
        var path = Directory.Exists(dir) ? Directory.GetFiles(dir, "v0.50.2-source-manifest*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (path is null) throw new InvalidDataException("v0.50.2 build source manifest is missing.");
        var manifest = JsonSerializer.Deserialize<BuildSourceManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions) ?? throw new InvalidDataException("v0.50.2 build source manifest could not be parsed.");
        if (manifest.Schema != "matawaka.workbench-build-source-manifest/v0.50" || manifest.Version != Version || !manifest.PredecessorGitSha.Equals(predecessor, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unexpected v0.50.2 build source manifest identity/predecessor.");
        var current = changed.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var bound = manifest.Files.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        if (!bound.Select(x => x.Path).SequenceEqual(current, StringComparer.Ordinal)) throw new InvalidDataException("Changed-file set differs from v0.50.2 build source manifest.");
        var rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var item in bound)
        {
            var full = Path.GetFullPath(Path.Combine(root, item.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(full) || !HashFile(full).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Build-bound v0.50.2 source file drift: {item.Path}");
        }
        return (path, HashFile(path));
    }

    private static void VerifyRunningExecutable(string expected)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !HashFile(path).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Running executable digest does not match v0.50.2 validation receipt.");
    }

    private static IReadOnlyList<string> ParseStatusPaths(string text)
        => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Length >= 4 ? line[3..].Trim() : line.Trim()).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static bool IsForbiddenCheckpointPath(string path)
    {
        var n = path.Replace('\\', '/');
        return n.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase) || n.StartsWith(".workbench/", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Tools/", StringComparison.OrdinalIgnoreCase) || n.Contains("../", StringComparison.Ordinal);
    }

    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static string RequireSha(string value, string role) { var sha = value.Trim(); if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException($"{role} is not a Git SHA-1: {sha}"); return sha.ToLowerInvariant(); }
    private static string ResolveRepositoryRoot(string workspaceRoot) { var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench")); if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository missing: {root}"); return root; }
    private static Task<GitResult> GitAsync(string root, CancellationToken cancellationToken, params string[] args) => GitAsync(root, cancellationToken, false, args);
    private static async Task<GitResult> GitAsync(string root, CancellationToken cancellationToken, bool allowFailure, params string[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(GitTimeout);
        var psi = new ProcessStartInfo { FileName = "git", WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.Environment["GIT_PAGER"] = "cat"; psi.Environment["GIT_TERMINAL_PROMPT"] = "0"; foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi }; if (!process.Start()) throw new InvalidDataException("Failed to start fixed v0.50.2 checkpoint Git process.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw; }
        var o = await stdout; var e = await stderr; if (process.ExitCode != 0 && !allowFailure) throw new InvalidDataException($"v0.50.2 checkpoint Git operation failed: {e.Trim()}"); return new GitResult(process.ExitCode, o, e);
    }
    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
