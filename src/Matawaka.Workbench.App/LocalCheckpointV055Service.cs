using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalCheckpointCandidateV055(
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

public sealed record LocalCheckpointReceiptV055(
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
    bool WorkingTreeCleanAfterCommit,
    bool RemotePushAllowed,
    bool NetworkAccessAllowed,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed class LocalCheckpointV055Service
{
    public const string Version = "0.55";
    public const string AcceptanceSchema = "matawaka.workbench-acceptance-receipt/v0.55";
    public const string ExpectedPredecessorTag = "workbench-v0.54.2-accepted";
    public const string ExpectedPredecessorCommit = "65b0b49a513a6b782760a7626d6b768bf7bb7f91";
    public const string TargetTag = "workbench-v0.55-accepted";
    public const string CommitMessage = "Checkpoint Workbench v0.55 bounded one-shot local-model invocation lease";
    public const string BuildManifestSchema = "matawaka.workbench-build-source-manifest/v0.55";
    public const string BuildManifestPattern = "v0.55-source-manifest*.json";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public async Task<LocalCheckpointCandidateV055> PreviewAsync(
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
        if (!head.Equals(ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"v0.55 exact predecessor mismatch: expected {ExpectedPredecessorCommit}, observed {head}.");
        var tagHead = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout, ExpectedPredecessorTag);
        if (!tagHead.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("workbench-v0.54.2-accepted is not at exact current HEAD.");
        if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "tag", "--list", TargetTag)).Stdout))
            throw new InvalidDataException($"Target tag already exists: {TargetTag}");

        var changed = ParseStatusPaths((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (changed.Count == 0) throw new InvalidDataException("There are no v0.55 source changes to checkpoint.");
        if (changed.Any(IsForbiddenCheckpointPath))
            throw new InvalidDataException("v0.55 checkpoint contains forbidden runtime/CI/test/package paths.");
        var manifest = ValidateBuildSourceManifest(root, head, changed);

        return new LocalCheckpointCandidateV055(
            Version, root, head, ExpectedPredecessorTag, TargetTag, CommitMessage,
            acceptancePath, HashFile(acceptancePath), manifest.Path, manifest.Sha256,
            acceptance.AppExecutableSha256, changed);
    }

    public async Task<LocalCheckpointReceiptV055> AcceptFromBootstrapAsync(
        LocalCheckpointCandidateV055 candidate,
        string bootstrapLeaseId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bootstrapLeaseId))
            throw new InvalidDataException("A claimed transition-bootstrap lease id is required for v0.55 automatic local acceptance.");
        if (candidate.Version != Version || candidate.ExpectedPredecessorTag != ExpectedPredecessorTag || candidate.TargetTag != TargetTag)
            throw new InvalidDataException("Checkpoint candidate does not match fixed v0.55 contract.");

        var root = candidate.RepositoryRoot;
        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(candidate.PreviousHead, StringComparison.OrdinalIgnoreCase) || !head.Equals(ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed after v0.55 checkpoint preview.");
        var tagHead = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout, ExpectedPredecessorTag);
        if (!tagHead.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Accepted local v0.54.2 predecessor tag moved after preview.");
        var status = ParseStatusPaths((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (!status.SequenceEqual(candidate.ChangedFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Workbench working tree changed after v0.55 checkpoint preview.");

        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(candidate.AcceptanceArtifactPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.55 acceptance artifact disappeared before checkpoint.");
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
            if (!newParent.Equals(ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("v0.55 checkpoint parent is not exact accepted/public v0.54.2.");
            await GitAsync(root, cancellationToken, "tag", "-a", TargetTag, "-m", "Accepted Workbench v0.55: bounded one-shot local-model invocation lease");
            tagged = true;
            if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
                throw new InvalidDataException("Workbench working tree is not clean after v0.55 checkpoint.");

            return new LocalCheckpointReceiptV055(
                "matawaka.workbench-local-checkpoint-receipt/v0.55", Version, DateTimeOffset.Now,
                candidate.PreviousHead, newHead, TargetTag, CommitMessage,
                candidate.AcceptanceArtifactPath, candidate.AcceptanceArtifactSha256,
                candidate.BuildSourceManifestPath, candidate.BuildSourceManifestSha256,
                candidate.AppExecutableSha256, candidate.ChangedFiles, true, false, false, NonEffects(),
                $"Local v0.55 checkpoint consumed one-shot transition-bootstrap lease {bootstrapLeaseId}; publication and real-model authority remain separate.");
        }
        catch
        {
            if (tagged) await GitAsync(root, CancellationToken.None, true, "tag", "-d", TargetTag);
            if (committed) await GitAsync(root, CancellationToken.None, true, "reset", "--mixed", candidate.PreviousHead);
            else await GitAsync(root, CancellationToken.None, true, "reset");
            throw;
        }
    }

    public static async Task<string> WriteReceiptAsync(string workspaceRoot, LocalCheckpointReceiptV055 receipt, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(ResolveRepositoryRoot(workspaceRoot), "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"checkpoint-v0.55-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("checkpoint-v055-version", Version == "0.55", Version, "0.55"),
        ("checkpoint-v055-predecessor", ExpectedPredecessorCommit == "65b0b49a513a6b782760a7626d6b768bf7bb7f91", ExpectedPredecessorCommit, "exact published v0.54.2"),
        ("checkpoint-v055-predecessor-tag", ExpectedPredecessorTag == "workbench-v0.54.2-accepted", ExpectedPredecessorTag, "workbench-v0.54.2-accepted"),
        ("checkpoint-v055-target-tag", TargetTag == "workbench-v0.55-accepted", TargetTag, "workbench-v0.55-accepted"),
        ("checkpoint-v055-build-manifest", BuildManifestSchema == "matawaka.workbench-build-source-manifest/v0.55", BuildManifestSchema, "v0.55"),
        ("checkpoint-v055-publication", true, "RemotePushAllowed=false", "separate post-realhost admission decision")
    };

    private static IReadOnlyList<string> NonEffects() => new[]
    {
        "exact predecessor is published workbench-v0.54.2-accepted at 65b0b49a513a6b782760a7626d6b768bf7bb7f91",
        "local checkpoint performs no remote publication or network access",
        "v0.52 acquisition, v0.53 execution and v0.54 materialization primitives are unchanged",
        "v0.55 acceptance performs no model invocation",
        "local acceptance does not authorize real LM1/llama/CUDA acquisition or KONTUR inference",
        "no benchmark/game/display/send/Agent Execute/ActionPermit authority"
    };

    private static void RequirePassingAcceptance(WorkbenchAcceptanceReceipt acceptance)
    {
        if (!acceptance.Passed || acceptance.Version != Version || acceptance.Schema != AcceptanceSchema)
            throw new InvalidDataException("A passing exact Workbench v0.55 acceptance receipt is required.");
    }

    private static string ValidateAcceptanceArtifact(string root, string artifactPath, WorkbenchAcceptanceReceipt acceptance)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath)) throw new InvalidDataException("Passing v0.55 acceptance artifact is missing.");
        var full = Path.GetFullPath(artifactPath);
        var allowed = Path.GetFullPath(Path.Combine(root, "artifacts", "acceptance")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Acceptance artifact must be under Workbench/artifacts/acceptance.");
        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.55 acceptance artifact could not be parsed.");
        if (!parsed.Passed || parsed.Schema != AcceptanceSchema || parsed.Version != Version || parsed.RunId != acceptance.RunId ||
            !parsed.AppExecutableSha256.Equals(acceptance.AppExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance artifact does not match passing in-memory v0.55 receipt.");
        return full;
    }

    private static (string Path, string Sha256) ValidateBuildSourceManifest(string root, string predecessor, IReadOnlyList<string> changed)
    {
        var dir = Path.Combine(root, "artifacts", "checkpoints");
        var path = Directory.Exists(dir) ? Directory.GetFiles(dir, BuildManifestPattern).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (path is null) throw new InvalidDataException("v0.55 build source manifest is missing.");
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var obj = doc.RootElement;
        if (obj.GetProperty("Schema").GetString() != BuildManifestSchema || obj.GetProperty("Version").GetString() != Version ||
            !string.Equals(obj.GetProperty("PredecessorGitSha").GetString(), predecessor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected v0.55 build source manifest identity/predecessor.");
        var bound = obj.GetProperty("Files").EnumerateArray()
            .Select(x => (Path: x.GetProperty("Path").GetString() ?? "", Sha256: x.GetProperty("Sha256").GetString() ?? ""))
            .OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var current = changed.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!bound.Select(x => x.Path).SequenceEqual(current, StringComparer.Ordinal))
            throw new InvalidDataException("Changed-file set differs from v0.55 build source manifest.");
        foreach (var item in bound)
        {
            var full = Path.GetFullPath(Path.Combine(root, item.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(full) || !HashFile(full).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Build-bound v0.55 source file drift: {item.Path}");
        }
        return (path, HashFile(path));
    }

    private static void VerifyRunningExecutable(string expected)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !HashFile(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Running executable digest does not match v0.55 acceptance receipt.");
    }

    private static IReadOnlyList<string> ParseStatusPaths(string text)
        => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length >= 4 ? line[3..].Trim() : line.Trim())
            .Select(path => path.Contains(" -> ", StringComparison.Ordinal) ? path.Split(" -> ", StringSplitOptions.None)[^1].Trim() : path)
            .Select(path => path.Trim('"').Replace('\\', '/'))
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static bool IsForbiddenCheckpointPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".workbench/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Tools/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".github/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Apps/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("AppSources/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetFullPath(workspaceRoot.Trim()), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository is missing: {root}");
        return root;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RequireSha(string value, string role)
    {
        var trimmed = value.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^[0-9a-fA-F]{40}$"))
            throw new InvalidDataException($"Unexpected Git SHA for {role}: {trimmed}");
        return trimmed.ToLowerInvariant();
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> GitAsync(
        string root,
        CancellationToken cancellationToken,
        bool allowFailure,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start local Git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeout);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (!allowFailure && process.ExitCode != 0)
            throw new InvalidDataException($"Git command failed: git {string.Join(' ', args)}\n{stderr}\n{stdout}");
        return (process.ExitCode, stdout, stderr);
    }

    private static Task<(int ExitCode, string Stdout, string Stderr)> GitAsync(
        string root,
        CancellationToken cancellationToken,
        params string[] args)
        => GitAsync(root, cancellationToken, false, args);
}
