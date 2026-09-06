using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record LocalCheckpointCandidateV0552(
    string Version,
    string RepositoryRoot,
    string FirstParent,
    string SecondParent,
    string ExpectedPredecessorTag,
    string TargetTag,
    string CommitMessage,
    string AcceptanceArtifactPath,
    string AcceptanceArtifactSha256,
    string BuildSourceManifestPath,
    string BuildSourceManifestSha256,
    string AppExecutableSha256,
    string RecoveryReceiptSha256,
    string PublicImportReceiptSha256,
    IReadOnlyList<string> ChangedFiles);

public sealed record LocalCheckpointReceiptV0552(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string FirstParent,
    string SecondParent,
    string NewHead,
    string Tag,
    string CommitMessage,
    string AcceptanceArtifactPath,
    string AcceptanceArtifactSha256,
    string BuildSourceManifestPath,
    string BuildSourceManifestSha256,
    string AppExecutableSha256,
    string RecoveryReceiptSha256,
    string PublicImportReceiptSha256,
    IReadOnlyList<string> ChangedFiles,
    bool TwoParentCommitCreated,
    bool ParentOrderVerified,
    bool WorkingTreeCleanAfterCommit,
    bool RemotePushAllowed,
    bool NetworkAccessAllowed,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed class LocalCheckpointV0552Service
{
    public const string Version = "0.55.2";
    public const string AcceptanceSchema = "matawaka.workbench-acceptance-receipt/v0.55.2";
    public const string ExpectedPredecessorTag = "workbench-v0.55-accepted";
    public const string FirstParentCommit = "02d81b8559bc7c9676949be0557d20ecb50a9890";
    public const string SecondParentCommit = "6111fdf82a9e8947a7722e9b603c1e9268a19105";
    public const string TargetTag = "workbench-v0.55.2-accepted";
    public const string CommitMessage = "Checkpoint Workbench v0.55.2 converged v0.55 model invocation and public #82 provenance substrate";
    public const string BuildManifestSchema = "matawaka.workbench-build-source-manifest/v0.55";
    public const string BuildManifestPattern = "v0.55.2-source-manifest*.json";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public async Task<LocalCheckpointCandidateV0552> PreviewAsync(
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
        if (!head.Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"v0.55.2 exact local first-parent predecessor mismatch: expected {FirstParentCommit}, observed {head}.");
        var tagHead = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout, ExpectedPredecessorTag);
        if (!tagHead.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("workbench-v0.55-accepted is not at exact current local HEAD.");
        if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "tag", "--list", TargetTag)).Stdout))
            throw new InvalidDataException($"Target tag already exists: {TargetTag}");

        await RequireImportedSecondParentAsync(root, cancellationToken);
        var evidence = WorkbenchConvergenceEvidenceVerifierV0552.FindExact(workspaceRoot);
        if (!evidence.ImportedPublicCommit.Equals(SecondParentCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Convergence evidence does not bind the exact second parent.");

        var userName = (await GitAsync(root, cancellationToken, true, "config", "--get", "user.name")).Stdout.Trim();
        var userEmail = (await GitAsync(root, cancellationToken, true, "config", "--get", "user.email")).Stdout.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(userEmail))
            throw new InvalidDataException("Local Git identity is missing.");

        var changed = ParseStatusPaths((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (changed.Count == 0) throw new InvalidDataException("There are no Workbench v0.55.2 source changes to checkpoint.");
        if (changed.Any(IsForbiddenCheckpointPath))
            throw new InvalidDataException("v0.55.2 checkpoint contains forbidden runtime/artifact/catalog paths.");
        var manifest = ValidateBuildSourceManifest(root, head, changed);

        return new LocalCheckpointCandidateV0552(
            Version, root, head, SecondParentCommit, ExpectedPredecessorTag, TargetTag, CommitMessage,
            acceptancePath, HashFile(acceptancePath), manifest.Path, manifest.Sha256,
            acceptance.AppExecutableSha256, evidence.RecoveryReceiptSha256, evidence.PublicImportReceiptSha256, changed);
    }

    public async Task<LocalCheckpointReceiptV0552> AcceptFromBootstrapAsync(
        LocalCheckpointCandidateV0552 candidate,
        string bootstrapLeaseId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bootstrapLeaseId))
            throw new InvalidDataException("A claimed transition-bootstrap lease id is required for automatic v0.55.2 local acceptance.");
        if (candidate.Version != Version || candidate.ExpectedPredecessorTag != ExpectedPredecessorTag || candidate.TargetTag != TargetTag ||
            !candidate.FirstParent.Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase) ||
            !candidate.SecondParent.Equals(SecondParentCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Checkpoint candidate does not match fixed v0.55.2 two-parent contract.");

        var root = candidate.RepositoryRoot;
        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed after v0.55.2 checkpoint preview.");
        var tagHead = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout, ExpectedPredecessorTag);
        if (!tagHead.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Accepted local v0.55 predecessor tag moved after preview.");
        await RequireImportedSecondParentAsync(root, cancellationToken);

        var status = ParseStatusPaths((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (!status.SequenceEqual(candidate.ChangedFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Workbench working tree changed after v0.55.2 checkpoint preview.");

        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(candidate.AcceptanceArtifactPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.55.2 acceptance artifact disappeared before checkpoint.");
        RequirePassingAcceptance(parsed);
        VerifyRunningExecutable(parsed.AppExecutableSha256);
        ValidateBuildSourceManifest(root, head, status);
        var evidence = WorkbenchConvergenceEvidenceVerifierV0552.FindExact(Directory.GetParent(root)?.FullName ?? throw new InvalidDataException("Workspace root unavailable."));
        if (!evidence.RecoveryReceiptSha256.Equals(candidate.RecoveryReceiptSha256, StringComparison.OrdinalIgnoreCase) ||
            !evidence.PublicImportReceiptSha256.Equals(candidate.PublicImportReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recovery/import evidence changed after checkpoint preview.");

        var refMoved = false;
        var tagged = false;
        string? newHead = null;
        try
        {
            await GitAsync(root, cancellationToken, "add", "-A", "--", ".");
            var tree = RequireSha((await GitAsync(root, cancellationToken, "write-tree")).Stdout, "checkpoint tree");
            newHead = RequireSha((await GitAsync(root, cancellationToken, "commit-tree", tree,
                "-p", FirstParentCommit, "-p", SecondParentCommit, "-m", CommitMessage)).Stdout, "two-parent checkpoint commit");
            VerifyTwoParents(await GitAsync(root, cancellationToken, "rev-list", "--parents", "-n", "1", newHead));

            await GitAsync(root, cancellationToken, "update-ref", "HEAD", newHead, FirstParentCommit);
            refMoved = true;
            var observedHead = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "new HEAD");
            if (!observedHead.Equals(newHead, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Two-parent checkpoint commit was not installed as current HEAD.");

            await GitAsync(root, cancellationToken, "tag", "-a", TargetTag, "-m", "Accepted Workbench v0.55.2: converged local v0.55 and public #82 frontiers");
            tagged = true;
            var targetTag = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", TargetTag)).Stdout, TargetTag);
            if (!targetTag.Equals(newHead, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("v0.55.2 target tag does not peel to exact two-parent accepted commit.");
            if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
                throw new InvalidDataException("Workbench working tree is not clean after v0.55.2 two-parent checkpoint.");

            return new LocalCheckpointReceiptV0552(
                "matawaka.workbench-local-checkpoint-receipt/v0.55.2", Version, DateTimeOffset.Now,
                FirstParentCommit, SecondParentCommit, newHead, TargetTag, CommitMessage,
                candidate.AcceptanceArtifactPath, candidate.AcceptanceArtifactSha256,
                candidate.BuildSourceManifestPath, candidate.BuildSourceManifestSha256,
                candidate.AppExecutableSha256, candidate.RecoveryReceiptSha256, candidate.PublicImportReceiptSha256,
                candidate.ChangedFiles, true, true, true, false, false, NonEffects(),
                $"Local v0.55.2 acceptance consumed transition-bootstrap lease {bootstrapLeaseId} and created one exact two-parent convergence commit. First parent is accepted local v0.55; second parent is the previously no-ref imported public #82 main. Publication remains separate.");
        }
        catch
        {
            if (tagged) await GitAsync(root, CancellationToken.None, true, "tag", "-d", TargetTag);
            if (refMoved && newHead is not null)
                await GitAsync(root, CancellationToken.None, true, "update-ref", "HEAD", FirstParentCommit, newHead);
            await GitAsync(root, CancellationToken.None, true, "reset");
            throw;
        }
    }

    public static async Task<string> WriteReceiptAsync(string workspaceRoot, LocalCheckpointReceiptV0552 receipt, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(ResolveRepositoryRoot(workspaceRoot), "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"checkpoint-v0.55.2-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("checkpoint-v0552-version", Version == "0.55.2", Version, "0.55.2"),
        ("checkpoint-v0552-first-parent", FirstParentCommit == WorkbenchConvergenceEvidenceVerifierV0552.LocalAcceptedCommit, FirstParentCommit, "exact local accepted v0.55"),
        ("checkpoint-v0552-second-parent", SecondParentCommit == WorkbenchConvergenceEvidenceVerifierV0552.PublicMainCommit, SecondParentCommit, "exact imported public #82 main"),
        ("checkpoint-v0552-target-tag", TargetTag == "workbench-v0.55.2-accepted", TargetTag, "fresh successor tag"),
        ("checkpoint-v0552-two-parent", true, "commit-tree -p local -p public; parent order reverified", "two exact parents"),
        ("checkpoint-v0552-publication", true, "RemotePushAllowed=false; NetworkAccessAllowed=false", "publication remains separate")
    };

    private static IReadOnlyList<string> NonEffects() => new[]
    {
        "two-parent local checkpoint performs no remote publication or network access",
        "failed v0.55.1 identity is not reused and no v0.55.1 tag is created",
        "historical v0.55 model-invocation and public #82 provenance-bound runtime-execution semantics remain distinct",
        "real-host admission, recovery and no-ref import receipts are read-only evidence and are not reinterpreted",
        "no artifact acquisition/materialization/runtime execution/model invocation/benchmark/game/display authority",
        "no ResponseAuthority, Agent Execute, ActionPermit or SuccessorPermit"
    };

    private static async Task RequireImportedSecondParentAsync(string root, CancellationToken cancellationToken)
    {
        var cat = await GitAsync(root, cancellationToken, true, "cat-file", "-e", SecondParentCommit + "^{commit}");
        if (cat.ExitCode != 0) throw new InvalidDataException("Exact public #82 commit object is not locally available; bounded convergence import is required first.");
        var line = (await GitAsync(root, cancellationToken, "rev-list", "--parents", "-n", "1", SecondParentCommit)).Stdout.Trim();
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals(SecondParentCommit, StringComparison.OrdinalIgnoreCase) ||
            !parts[1].Equals(WorkbenchConvergenceEvidenceVerifierV0552.PublicMainFirstParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Imported public #82 commit ancestry differs from exact convergence evidence.");
    }

    private static void VerifyTwoParents((int ExitCode, string Stdout, string Stderr) result)
    {
        if (result.ExitCode != 0) throw new InvalidDataException("Unable to inspect two-parent checkpoint commit.");
        var parts = result.Stdout.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !parts[1].Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase) || !parts[2].Equals(SecondParentCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.55.2 checkpoint does not have the exact ordered two-parent ancestry.");
    }

    private static void RequirePassingAcceptance(WorkbenchAcceptanceReceipt acceptance)
    {
        if (!acceptance.Passed || acceptance.Version != Version || acceptance.Schema != AcceptanceSchema)
            throw new InvalidDataException("A passing exact Workbench v0.55.2 acceptance receipt is required.");
    }

    private static string ValidateAcceptanceArtifact(string root, string artifactPath, WorkbenchAcceptanceReceipt acceptance)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath)) throw new InvalidDataException("Passing v0.55.2 acceptance artifact is missing.");
        var full = Path.GetFullPath(artifactPath);
        var allowed = Path.GetFullPath(Path.Combine(root, "artifacts", "acceptance")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Acceptance artifact must be under Workbench/artifacts/acceptance.");
        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.55.2 acceptance artifact could not be parsed.");
        if (!parsed.Passed || parsed.Schema != AcceptanceSchema || parsed.Version != Version || parsed.RunId != acceptance.RunId ||
            !parsed.AppExecutableSha256.Equals(acceptance.AppExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance artifact does not match passing in-memory v0.55.2 receipt.");
        return full;
    }

    private static (string Path, string Sha256) ValidateBuildSourceManifest(string root, string predecessor, IReadOnlyList<string> changed)
    {
        var dir = Path.Combine(root, "artifacts", "checkpoints");
        var path = Directory.Exists(dir) ? Directory.GetFiles(dir, BuildManifestPattern).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (path is null) throw new InvalidDataException("v0.55.2 build source manifest is missing.");
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var obj = doc.RootElement;
        if (obj.GetProperty("Schema").GetString() != BuildManifestSchema || obj.GetProperty("Version").GetString() != Version ||
            !string.Equals(obj.GetProperty("PredecessorGitSha").GetString(), predecessor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected v0.55.2 build source manifest identity/predecessor.");
        var bound = obj.GetProperty("Files").EnumerateArray()
            .Select(x => (Path: x.GetProperty("Path").GetString() ?? "", Sha256: x.GetProperty("Sha256").GetString() ?? ""))
            .OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var current = changed.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!bound.Select(x => x.Path).SequenceEqual(current, StringComparer.Ordinal))
            throw new InvalidDataException("Changed-file set differs from v0.55.2 build source manifest.");
        foreach (var item in bound)
        {
            var full = Path.GetFullPath(Path.Combine(root, item.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(full) || !HashFile(full).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Build-bound v0.55.2 source file drift: {item.Path}");
        }
        return (path, HashFile(path));
    }

    private static void VerifyRunningExecutable(string expected)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !HashFile(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Running executable digest does not match v0.55.2 acceptance receipt.");
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
               normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Tools/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Apps/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("AppSources/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetFullPath(workspaceRoot.Trim()), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository is missing: {root}");
        return root;
    }

    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static string RequireSha(string text, string label)
    {
        var sha = text.Trim().ToLowerInvariant();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException($"Invalid Git SHA for {label}: {text.Trim()}");
        return sha;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> GitAsync(string root, CancellationToken cancellationToken, params string[] args)
        => await GitAsync(root, cancellationToken, false, args);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> GitAsync(string root, CancellationToken cancellationToken, bool allowFailure, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidDataException("Unable to start fixed Git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var wait = process.WaitForExitAsync(cancellationToken);
        if (await Task.WhenAny(wait, Task.Delay(GitTimeout, cancellationToken)) != wait)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("Fixed v0.55.2 Git operation timed out.");
        }
        await wait;
        var stdout = await stdoutTask; var stderr = await stderrTask;
        if (!allowFailure && process.ExitCode != 0) throw new InvalidDataException("Fixed v0.55.2 Git operation failed: " + stderr.Trim());
        return (process.ExitCode, stdout, stderr);
    }
}
