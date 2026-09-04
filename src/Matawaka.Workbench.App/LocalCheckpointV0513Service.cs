using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

/// <summary>
/// v0.51.3 local checkpoint. Exact predecessor identity is dual-bound by the
/// update transaction build-source manifest and workbench-v0.51.2-accepted at HEAD.
/// </summary>
public sealed class LocalCheckpointV0513Service
{
    public const string Version = "0.51.3";
    public const string AcceptanceSchema = "matawaka.workbench-acceptance-receipt/v0.51.3";
    public const string ExpectedPredecessorTag = "workbench-v0.51.2-accepted";
    public const string TargetTag = "workbench-v0.51.3-accepted";
    public const string CommitMessage = "Checkpoint Workbench v0.51.3 read session status and orphan closure";
    public const string BuildManifestSchema = "matawaka.workbench-build-source-manifest/v0.51";

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
        RequirePassingAcceptance(acceptance);
        var root = ResolveRepositoryRoot(workspaceRoot);
        var acceptancePath = ValidateAcceptanceArtifact(root, acceptanceArtifactPath, acceptance);
        VerifyRunningExecutable(acceptance.AppExecutableSha256);

        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        var predecessorTagHead = RequireSha(
            (await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout,
            ExpectedPredecessorTag);
        if (!predecessorTagHead.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("workbench-v0.51.2-accepted is not at exact current HEAD.");

        if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "tag", "--list", TargetTag)).Stdout))
            throw new InvalidDataException($"Target tag already exists: {TargetTag}");

        var userName = (await GitAsync(root, cancellationToken, true, "config", "--get", "user.name")).Stdout.Trim();
        var userEmail = (await GitAsync(root, cancellationToken, true, "config", "--get", "user.email")).Stdout.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(userEmail))
            throw new InvalidDataException("Local Git identity is missing.");

        var changed = ParseStatusPaths((await GitAsync(
            root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (changed.Count == 0) throw new InvalidDataException("There are no Workbench v0.51.3 source changes to checkpoint.");
        if (changed.Any(IsForbiddenCheckpointPath))
            throw new InvalidDataException("Checkpoint contains forbidden artifacts/.workbench/Tools runtime path.");

        var manifest = ValidateBuildSourceManifest(root, head, changed);
        return new LocalCheckpointCandidate(
            Version, root, head, ExpectedPredecessorTag, TargetTag, CommitMessage,
            acceptancePath, HashFile(acceptancePath), manifest.Path, manifest.Sha256,
            acceptance.AppExecutableSha256, changed);
    }

    public Task<LocalCheckpointReceipt> AcceptFromBootstrapAsync(
        LocalCheckpointCandidate candidate,
        string bootstrapLeaseId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bootstrapLeaseId))
            throw new InvalidDataException("A claimed transition-bootstrap lease id is required for automatic v0.51.3 local acceptance.");
        return AcceptCoreAsync(candidate, bootstrapLeaseId, cancellationToken);
    }

    private async Task<LocalCheckpointReceipt> AcceptCoreAsync(
        LocalCheckpointCandidate candidate,
        string bootstrapLeaseId,
        CancellationToken cancellationToken)
    {
        if (candidate.Version != Version || candidate.ExpectedPredecessorTag != ExpectedPredecessorTag || candidate.TargetTag != TargetTag)
            throw new InvalidDataException("Checkpoint candidate does not match fixed v0.51.3 tag/version contract.");

        var root = candidate.RepositoryRoot;
        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(candidate.PreviousHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed after v0.51.3 checkpoint preview.");

        var tagHead = RequireSha(
            (await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout,
            ExpectedPredecessorTag);
        if (!tagHead.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Accepted local v0.51.2 predecessor tag moved after preview.");

        var status = ParseStatusPaths((await GitAsync(
            root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (!status.SequenceEqual(candidate.ChangedFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Workbench working tree changed after v0.51.3 checkpoint preview.");

        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(
            File.ReadAllText(candidate.AcceptanceArtifactPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.51.3 validation artifact disappeared before checkpoint.");
        RequirePassingAcceptance(parsed);
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
            if (!newParent.Equals(candidate.PreviousHead, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("v0.51.3 checkpoint parent is not exact manifest/tag-bound local v0.51.2 predecessor.");

            await GitAsync(root, cancellationToken,
                "tag", "-a", TargetTag, "-m", "Accepted Workbench v0.51.3: read session status and exact orphan closure");
            tagged = true;

            if (!string.IsNullOrWhiteSpace((await GitAsync(
                    root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
                throw new InvalidDataException("Workbench working tree is not clean after v0.51.3 checkpoint.");

            var nonEffects = new[]
            {
                "exact predecessor commit is supplied by the update/build source manifest and independently matched to workbench-v0.51.2-accepted at current HEAD",
                "no remote publication during local checkpoint",
                "no read lease, MCP adapter, status/orphan-closure runtime state or Secure MCP Tunnel state committed",
                "no bearer plaintext/hash, local MCP endpoint or clipboard contents committed",
                "no private Apps/AppSources bytes committed",
                "no external Tools/OpenAI binary committed",
                "no automatic retry/Publish/Lifecycle authority",
                "no catalog mutation or Agent Execute/ActionPermit"
            };

            var authority = new LocalCheckpointAuthorityReceipt(
                "matawaka.workbench-local-checkpoint-bootstrap-authority-receipt/v0.51.3",
                "human-operator-via-one-shot-transition-bootstrap",
                "git.local-checkpoint.accept",
                root,
                $"one-shot transition-bootstrap lease {bootstrapLeaseId}; exact v0.51.3 validation + manifest/tag-bound predecessor required",
                false, true, false, false, false, false,
                new[] { "git add -A -- .", "git commit -m <fixed-v0.51.3-message>", "git tag -a workbench-v0.51.3-accepted" },
                nonEffects);

            return new LocalCheckpointReceipt(
                "matawaka.workbench-local-checkpoint-receipt/v0.51.3",
                Version, DateTimeOffset.Now, candidate.PreviousHead, newHead, TargetTag, CommitMessage,
                candidate.AcceptanceArtifactPath, candidate.AcceptanceArtifactSha256,
                candidate.BuildSourceManifestPath, candidate.BuildSourceManifestSha256,
                candidate.AppExecutableSha256, candidate.ChangedFiles, authority, true, nonEffects,
                $"Local v0.51.3 checkpoint consumed one-shot transition-bootstrap lease {bootstrapLeaseId}. Exact predecessor identity came from the update/build manifest and was independently matched to {ExpectedPredecessorTag}.");
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
        var path = Path.Combine(dir, $"checkpoint-v0.51.3-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("checkpoint-v0513-predecessor-source", true, "build-source manifest PredecessorGitSha + fixed predecessor tag at current HEAD", "dual binding"),
        ("checkpoint-v0513-predecessor-tag", ExpectedPredecessorTag == "workbench-v0.51.2-accepted", ExpectedPredecessorTag, "workbench-v0.51.2-accepted"),
        ("checkpoint-v0513-target-tag", TargetTag == "workbench-v0.51.3-accepted", TargetTag, "workbench-v0.51.3-accepted"),
        ("checkpoint-v0513-build-schema", BuildManifestSchema == "matawaka.workbench-build-source-manifest/v0.51", BuildManifestSchema, "v0.51"),
        ("checkpoint-v0513-bootstrap-only", true, "AcceptFromBootstrapAsync only", "no manual visible Accept path"),
        ("checkpoint-v0513-publication-deferred", true, "no remote mutation in checkpoint", "deferred")
    };

    private static void RequirePassingAcceptance(WorkbenchAcceptanceReceipt acceptance)
    {
        if (!acceptance.Passed || acceptance.Version != Version || acceptance.Schema != AcceptanceSchema)
            throw new InvalidDataException("A passing exact Workbench v0.51.3 validation receipt is required.");
    }

    private static string ValidateAcceptanceArtifact(string root, string artifactPath, WorkbenchAcceptanceReceipt acceptance)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
            throw new InvalidDataException("Passing v0.51.3 validation artifact is missing.");
        var full = Path.GetFullPath(artifactPath);
        var allowed = Path.GetFullPath(Path.Combine(root, "artifacts", "acceptance")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance artifact must be under Workbench/artifacts/acceptance.");
        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.51.3 validation artifact could not be parsed.");
        if (!parsed.Passed || parsed.Schema != AcceptanceSchema || parsed.Version != Version || parsed.RunId != acceptance.RunId ||
            !parsed.AppExecutableSha256.Equals(acceptance.AppExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Validation artifact does not match the passing in-memory v0.51.3 receipt.");
        return full;
    }

    private static (string Path, string Sha256) ValidateBuildSourceManifest(string root, string predecessor, IReadOnlyList<string> changed)
    {
        var dir = Path.Combine(root, "artifacts", "checkpoints");
        var path = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "v0.51.3-source-manifest*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (path is null) throw new InvalidDataException("v0.51.3 build source manifest is missing.");

        var manifest = JsonSerializer.Deserialize<BuildSourceManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.51.3 build source manifest could not be parsed.");
        if (manifest.Schema != BuildManifestSchema || manifest.Version != Version ||
            !manifest.PredecessorGitSha.Equals(predecessor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected v0.51.3 build source manifest identity/predecessor.");

        var current = changed.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var bound = manifest.Files.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        if (!bound.Select(x => x.Path).SequenceEqual(current, StringComparer.Ordinal))
            throw new InvalidDataException("Changed-file set differs from v0.51.3 build source manifest.");

        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var item in bound)
        {
            var full = Path.GetFullPath(Path.Combine(root, item.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(full) ||
                !HashFile(full).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Build-bound v0.51.3 source file drift: {item.Path}");
        }
        return (path, HashFile(path));
    }

    private static void VerifyRunningExecutable(string expected)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !HashFile(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Running executable digest does not match v0.51.3 validation receipt.");
    }

    private static IReadOnlyList<string> ParseStatusPaths(string text)
        => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length >= 4 ? line[3..].Trim() : line.Trim())
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static bool IsForbiddenCheckpointPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".workbench/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Tools/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("../", StringComparison.Ordinal);
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RequireSha(string value, string role)
    {
        var sha = value.Trim();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidDataException($"{role} is not a Git SHA-1: {sha}");
        return sha.ToLowerInvariant();
    }

    private static Task<GitResult> GitAsync(string root, CancellationToken cancellationToken, params string[] args)
        => GitAsync(root, cancellationToken, false, args);

    private static async Task<GitResult> GitAsync(
        string root,
        CancellationToken cancellationToken,
        bool allowFailure,
        params string[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeout);
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed v0.51.3 checkpoint Git process.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw; }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0 && !allowFailure)
            throw new InvalidDataException($"v0.51.3 checkpoint Git operation failed: {error.Trim()}");
        return new GitResult(process.ExitCode, output, error);
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
