using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalCheckpointCandidateV0542(
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

public sealed record LocalCheckpointReceiptV0542(
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

public sealed class LocalCheckpointV0542Service
{
    public const string Version = "0.54.2";
    public const string AcceptanceSchema = "matawaka.workbench-acceptance-receipt/v0.54.2";
    public const string ExpectedPredecessorTag = "workbench-v0.54.1-accepted";
    public const string ExpectedPredecessorCommit = "d483ceacc2b490357555794c0403cc16a22e193c";
    public const string TargetTag = "workbench-v0.54.2-accepted";
    public const string CommitMessage = "Checkpoint Workbench v0.54.2 real-host materialization admission and fixed publication closure";
    // The accepted v0.54.1 updater emits target 0.54.2 in schema family major.minor = v0.54.
    public const string BuildManifestSchema = "matawaka.workbench-build-source-manifest/v0.54";
    public const string BuildManifestPattern = "v0.54.2-source-manifest*.json";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public async Task<LocalCheckpointCandidateV0542> PreviewAsync(string workspaceRoot, string acceptanceArtifactPath,
        WorkbenchAcceptanceReceipt acceptance, CancellationToken cancellationToken)
    {
        RequirePassingAcceptance(acceptance);
        var root = ResolveRepositoryRoot(workspaceRoot);
        var acceptancePath = ValidateAcceptanceArtifact(root, acceptanceArtifactPath, acceptance);
        VerifyRunningExecutable(acceptance.AppExecutableSha256);

        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"v0.54.2 exact local predecessor mismatch: expected {ExpectedPredecessorCommit}, observed {head}.");
        var tagHead = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout, ExpectedPredecessorTag);
        if (!tagHead.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("workbench-v0.54.1-accepted is not at exact current HEAD.");
        if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "tag", "--list", TargetTag)).Stdout))
            throw new InvalidDataException($"Target tag already exists: {TargetTag}");

        var userName = (await GitAsync(root, cancellationToken, true, "config", "--get", "user.name")).Stdout.Trim();
        var userEmail = (await GitAsync(root, cancellationToken, true, "config", "--get", "user.email")).Stdout.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(userEmail))
            throw new InvalidDataException("Local Git identity is missing.");

        var changed = ParseStatusPaths((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (changed.Count == 0) throw new InvalidDataException("There are no Workbench v0.54.2 source changes to checkpoint.");
        if (changed.Any(IsForbiddenCheckpointPath))
            throw new InvalidDataException("Checkpoint contains forbidden artifacts/.workbench/Tools/.github runtime or CI path.");
        var manifest = ValidateBuildSourceManifest(root, head, changed);
        return new LocalCheckpointCandidateV0542(
            Version, root, head, ExpectedPredecessorTag, TargetTag, CommitMessage,
            acceptancePath, HashFile(acceptancePath), manifest.Path, manifest.Sha256,
            acceptance.AppExecutableSha256, changed);
    }

    public async Task<LocalCheckpointReceiptV0542> AcceptFromBootstrapAsync(LocalCheckpointCandidateV0542 candidate,
        string bootstrapLeaseId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bootstrapLeaseId))
            throw new InvalidDataException("A claimed transition-bootstrap lease id is required for automatic v0.54.2 local acceptance.");
        if (candidate.Version != Version || candidate.ExpectedPredecessorTag != ExpectedPredecessorTag || candidate.TargetTag != TargetTag)
            throw new InvalidDataException("Checkpoint candidate does not match fixed v0.54.2 contract.");

        var root = candidate.RepositoryRoot;
        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(candidate.PreviousHead, StringComparison.OrdinalIgnoreCase) || !head.Equals(ExpectedPredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed after v0.54.2 checkpoint preview.");
        var tagHead = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout, ExpectedPredecessorTag);
        if (!tagHead.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Accepted local v0.54.1 predecessor tag moved after preview.");
        var status = ParseStatusPaths((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (!status.SequenceEqual(candidate.ChangedFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Workbench working tree changed after v0.54.2 checkpoint preview.");

        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(candidate.AcceptanceArtifactPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.54.2 acceptance artifact disappeared before checkpoint.");
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
                throw new InvalidDataException("v0.54.2 checkpoint parent is not the exact local v0.54.1 accepted predecessor.");
            await GitAsync(root, cancellationToken, "tag", "-a", TargetTag, "-m", "Accepted Workbench v0.54.2: real-host materialization admission and fixed publication closure");
            tagged = true;
            if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
                throw new InvalidDataException("Workbench working tree is not clean after v0.54.2 checkpoint.");

            return new LocalCheckpointReceiptV0542(
                "matawaka.workbench-local-checkpoint-receipt/v0.54.2", Version, DateTimeOffset.Now,
                candidate.PreviousHead, newHead, TargetTag, CommitMessage,
                candidate.AcceptanceArtifactPath, candidate.AcceptanceArtifactSha256,
                candidate.BuildSourceManifestPath, candidate.BuildSourceManifestSha256,
                candidate.AppExecutableSha256, candidate.ChangedFiles, true, false, false, NonEffects(),
                $"Local v0.54.2 publication-closure checkpoint consumed one-shot transition-bootstrap lease {bootstrapLeaseId}; exact predecessor was {ExpectedPredecessorCommit} / {ExpectedPredecessorTag}.");
        }
        catch
        {
            if (tagged) await GitAsync(root, CancellationToken.None, true, "tag", "-d", TargetTag);
            if (committed) await GitAsync(root, CancellationToken.None, true, "reset", "--mixed", candidate.PreviousHead);
            else await GitAsync(root, CancellationToken.None, true, "reset");
            throw;
        }
    }

    public static async Task<string> WriteReceiptAsync(string workspaceRoot, LocalCheckpointReceiptV0542 receipt, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(ResolveRepositoryRoot(workspaceRoot), "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"checkpoint-v0.54.2-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("checkpoint-v0542-version", Version == "0.54.2", Version, "0.54.2"),
        ("checkpoint-v0542-predecessor", ExpectedPredecessorCommit == "d483ceacc2b490357555794c0403cc16a22e193c", ExpectedPredecessorCommit, "exact accepted local v0.54.1"),
        ("checkpoint-v0542-predecessor-tag", ExpectedPredecessorTag == "workbench-v0.54.1-accepted", ExpectedPredecessorTag, "workbench-v0.54.1-accepted"),
        ("checkpoint-v0542-target-tag", TargetTag == "workbench-v0.54.2-accepted", TargetTag, "workbench-v0.54.2-accepted"),
        ("checkpoint-v0542-build-manifest", BuildManifestSchema == "matawaka.workbench-build-source-manifest/v0.54" && BuildManifestPattern == "v0.54.2-source-manifest*.json", BuildManifestSchema, "predecessor writer major.minor schema + exact target filename"),
        ("checkpoint-v0542-publication", true, "RemotePushAllowed=false during checkpoint", "separate explicit Publish accepted confirmation")
    };

    private static IReadOnlyList<string> NonEffects() => new[]
    {
        "exact predecessor is workbench-v0.54.1-accepted at d483ceacc2b490357555794c0403cc16a22e193c",
        "local checkpoint performs no remote publication or network access",
        "real-host materialization evidence is read-only admission evidence",
        "v0.52 acquisition, v0.53 execution and v0.54 materialization primitives are not widened",
        "no artifact acquisition/materialization/process start/stop/benchmark/model/game authority",
        "no catalog mutation or Agent Execute/ActionPermit"
    };

    private static void RequirePassingAcceptance(WorkbenchAcceptanceReceipt acceptance)
    {
        if (!acceptance.Passed || acceptance.Version != Version || acceptance.Schema != AcceptanceSchema)
            throw new InvalidDataException("A passing exact Workbench v0.54.2 acceptance receipt is required.");
    }

    private static string ValidateAcceptanceArtifact(string root, string artifactPath, WorkbenchAcceptanceReceipt acceptance)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath)) throw new InvalidDataException("Passing v0.54.2 acceptance artifact is missing.");
        var full = Path.GetFullPath(artifactPath);
        var allowed = Path.GetFullPath(Path.Combine(root, "artifacts", "acceptance")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Acceptance artifact must be under Workbench/artifacts/acceptance.");
        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.54.2 acceptance artifact could not be parsed.");
        if (!parsed.Passed || parsed.Schema != AcceptanceSchema || parsed.Version != Version || parsed.RunId != acceptance.RunId ||
            !parsed.AppExecutableSha256.Equals(acceptance.AppExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance artifact does not match the passing in-memory v0.54.2 receipt.");
        return full;
    }

    private static (string Path, string Sha256) ValidateBuildSourceManifest(string root, string predecessor, IReadOnlyList<string> changed)
    {
        var dir = Path.Combine(root, "artifacts", "checkpoints");
        var path = Directory.Exists(dir) ? Directory.GetFiles(dir, BuildManifestPattern).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (path is null) throw new InvalidDataException("v0.54.2 build source manifest is missing.");
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var obj = doc.RootElement;
        if (obj.GetProperty("Schema").GetString() != BuildManifestSchema || obj.GetProperty("Version").GetString() != Version ||
            !string.Equals(obj.GetProperty("PredecessorGitSha").GetString(), predecessor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected v0.54.2 build source manifest identity/predecessor.");
        var bound = obj.GetProperty("Files").EnumerateArray()
            .Select(x => (Path: x.GetProperty("Path").GetString() ?? "", Sha256: x.GetProperty("Sha256").GetString() ?? ""))
            .OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var current = changed.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!bound.Select(x => x.Path).SequenceEqual(current, StringComparer.Ordinal))
            throw new InvalidDataException("Changed-file set differs from v0.54.2 build source manifest.");
        foreach (var item in bound)
        {
            var full = Path.GetFullPath(Path.Combine(root, item.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(full) || !HashFile(full).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Build-bound v0.54.2 source file drift: {item.Path}");
        }
        return (path, HashFile(path));
    }

    private static void VerifyRunningExecutable(string expected)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !HashFile(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Running executable digest does not match v0.54.2 acceptance receipt.");
    }

    private static IReadOnlyList<string> ParseStatusPaths(string text)
        => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length >= 4 ? line[3..].Trim() : line.Trim())
            .Select(path => path.Contains(" -> ", StringComparison.Ordinal) ? path.Split(" -> ", StringSplitOptions.None)[^1].Trim() : path)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static bool IsForbiddenCheckpointPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(".workbench/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Tools/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(".github/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Apps/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("AppSources/", StringComparison.OrdinalIgnoreCase);
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
        var sha = value.Trim();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException($"Invalid Git SHA for {role}: {value}");
        return sha.ToLowerInvariant();
    }

    private static async Task<GitResult> GitAsync(string root, CancellationToken cancellationToken, params string[] args)
        => await GitAsync(root, cancellationToken, false, args);
    private static async Task<GitResult> GitAsync(string root, CancellationToken cancellationToken, bool allowFailure, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start fixed local git invocation.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var result = new GitResult(process.ExitCode, await stdoutTask, await stderrTask);
        if (!allowFailure && result.ExitCode != 0) throw new InvalidDataException($"Fixed local git invocation failed ({string.Join(' ', args)}): {result.Stderr.Trim()}");
        return result;
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
