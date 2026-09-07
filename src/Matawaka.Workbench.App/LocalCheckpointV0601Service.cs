using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

internal sealed record LocalCheckpointCandidateV0601(
    string Version,
    string RepositoryRoot,
    string FirstParent,
    string SecondParent,
    string ExpectedPredecessorTag,
    string TargetTag,
    string CommitMessage,
    string AcceptanceArtifactPath,
    string AcceptanceArtifactSha256,
    string BuildReceiptPath,
    string BuildReceiptSha256,
    string BuildSourceManifestPath,
    string BuildSourceManifestSha256,
    string AppExecutableSha256,
    string PublicImportReceiptPath,
    string PublicImportReceiptSha256,
    IReadOnlyList<string> ChangedFiles);

internal sealed record LocalCheckpointReceiptV0601(
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
    string BuildReceiptPath,
    string BuildReceiptSha256,
    string BuildSourceManifestPath,
    string BuildSourceManifestSha256,
    string AppExecutableSha256,
    string PublicImportReceiptPath,
    string PublicImportReceiptSha256,
    IReadOnlyList<string> ChangedFiles,
    bool TwoParentCommitCreated,
    bool ParentOrderVerified,
    bool WorkingTreeCleanAfterCommit,
    bool RemotePushAllowed,
    bool NetworkAccessAllowed,
    bool AutomaticRetryAllowed,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Local-only v0.60.1 acceptance checkpoint. It converges the exact installed
/// v0.55.2 accepted parent with the exact separately imported public v0.60
/// implementation main object. It does not publish, fetch, push or reinterpret
/// either parent's authority semantics.
/// </summary>
internal sealed class LocalCheckpointV0601Service
{
    internal const string Version = "0.60.1";
    internal const string AcceptanceSchema = "matawaka.workbench-acceptance-receipt/v0.60.1";
    internal const string ExpectedPredecessorTag = "workbench-v0.55.2-accepted";
    internal const string FirstParentCommit = "ea852feeb0e8d92a8977bb251693e7e977913dca";
    internal const string SecondParentCommit = "6541dc32182c970c8e1a6ade426a6cee7086511b";
    internal const string TargetTag = "workbench-v0.60.1-accepted";
    internal const string HistoricalV060Tag = "workbench-v0.60-accepted";
    internal const string CommitMessage = "Checkpoint Workbench v0.60.1 operator acceptance over reviewed v0.60 and installed v0.55.2";
    internal const string BuildManifestSchema = "matawaka.workbench-build-source-manifest/v0.60";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    internal async Task<LocalCheckpointCandidateV0601> PreviewAsync(
        string workspaceRoot,
        string acceptanceArtifactPath,
        WorkbenchAcceptanceReceipt acceptance,
        TransitionBootstrapV040Lease bootstrapLease,
        CancellationToken cancellationToken)
    {
        RequirePassingAcceptance(acceptance);
        RequireBootstrapBinding(bootstrapLease);
        var root = ResolveRepositoryRoot(workspaceRoot);
        var acceptancePath = ValidateAcceptanceArtifact(root, acceptanceArtifactPath, acceptance);
        VerifyRunningExecutable(acceptance.AppExecutableSha256);

        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"v0.60.1 exact first-parent mismatch: expected={FirstParentCommit}; observed={head}");
        var predecessorTag = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout, ExpectedPredecessorTag);
        if (!predecessorTag.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("workbench-v0.55.2-accepted is not at exact current local HEAD.");
        if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "tag", "--list", TargetTag)).Stdout))
            throw new InvalidDataException($"v0.60.1 target tag already exists: {TargetTag}");
        if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "tag", "--list", HistoricalV060Tag)).Stdout))
            throw new InvalidDataException("Historical implementation-only v0.60 tag unexpectedly exists locally; v0.60 must not be retroactively accepted.");

        await RequireImportedSecondParentAsync(root, cancellationToken);
        var importEvidence = PublicMainImportReceiptVerifierV0601.FindExact(workspaceRoot);
        if (!importEvidence.ImportedCommit.Equals(SecondParentCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Public-main import evidence does not bind exact v0.60 second parent.");

        var userName = (await GitAsync(root, cancellationToken, true, "config", "--get", "user.name")).Stdout.Trim();
        var userEmail = (await GitAsync(root, cancellationToken, true, "config", "--get", "user.email")).Stdout.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(userEmail))
            throw new InvalidDataException("Local Git identity is missing.");

        var changed = ParseStatusPaths((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (changed.Count == 0)
            throw new InvalidDataException("There are no v0.60.1 source changes to checkpoint.");
        if (changed.Any(IsForbiddenCheckpointPath))
            throw new InvalidDataException("v0.60.1 checkpoint contains forbidden runtime/artifact/catalog paths.");

        var build = ValidateBuildReceipt(root, bootstrapLease);
        var manifest = ValidateBuildSourceManifest(root, build, head, changed);

        return new LocalCheckpointCandidateV0601(
            Version,
            root,
            head,
            SecondParentCommit,
            ExpectedPredecessorTag,
            TargetTag,
            CommitMessage,
            acceptancePath,
            HashFile(acceptancePath),
            bootstrapLease.BuildReceiptPath,
            bootstrapLease.BuildReceiptSha256,
            manifest.Path,
            manifest.Sha256,
            acceptance.AppExecutableSha256,
            importEvidence.ReceiptPath,
            importEvidence.ReceiptSha256,
            changed);
    }

    internal async Task<LocalCheckpointReceiptV0601> AcceptFromBootstrapAsync(
        LocalCheckpointCandidateV0601 candidate,
        TransitionBootstrapV040Lease bootstrapLease,
        CancellationToken cancellationToken)
    {
        RequireBootstrapBinding(bootstrapLease);
        if (string.IsNullOrWhiteSpace(bootstrapLease.LeaseId))
            throw new InvalidDataException("A claimed transition-bootstrap lease id is required for v0.60.1 acceptance.");
        if (candidate.Version != Version || candidate.TargetTag != TargetTag || candidate.ExpectedPredecessorTag != ExpectedPredecessorTag ||
            !candidate.FirstParent.Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase) ||
            !candidate.SecondParent.Equals(SecondParentCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Checkpoint candidate does not match fixed v0.60.1 convergence contract.");

        var root = candidate.RepositoryRoot;
        var head = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "HEAD");
        if (!head.Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed after v0.60.1 checkpoint preview.");
        var predecessorTag = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", ExpectedPredecessorTag)).Stdout, ExpectedPredecessorTag);
        if (!predecessorTag.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Accepted local v0.55.2 predecessor tag moved after preview.");
        await RequireImportedSecondParentAsync(root, cancellationToken);

        var status = ParseStatusPaths((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout);
        if (!status.SequenceEqual(candidate.ChangedFiles, StringComparer.Ordinal))
            throw new InvalidDataException("Workbench working tree changed after v0.60.1 checkpoint preview.");

        var parsedAcceptance = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(candidate.AcceptanceArtifactPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.60.1 acceptance artifact disappeared before checkpoint.");
        RequirePassingAcceptance(parsedAcceptance);
        VerifyRunningExecutable(parsedAcceptance.AppExecutableSha256);
        if (!parsedAcceptance.AppExecutableSha256.Equals(bootstrapLease.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.60.1 acceptance executable differs from the one-shot bootstrap candidate.");
        if (!HashFile(candidate.AcceptanceArtifactPath).Equals(candidate.AcceptanceArtifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.60.1 acceptance artifact bytes changed after preview.");

        var importEvidence = PublicMainImportReceiptVerifierV0601.FindExact(Directory.GetParent(root)?.FullName
            ?? throw new InvalidDataException("Workspace root unavailable."));
        if (!importEvidence.ReceiptSha256.Equals(candidate.PublicImportReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.60.1 public-main import receipt changed after preview.");

        var build = ValidateBuildReceipt(root, bootstrapLease);
        var manifest = ValidateBuildSourceManifest(root, build, head, status);
        if (!manifest.Sha256.Equals(candidate.BuildSourceManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.60.1 build source manifest changed after preview.");

        var refMoved = false;
        var tagged = false;
        string? newHead = null;
        try
        {
            await GitAsync(root, cancellationToken, "add", "-A", "--", ".");
            var tree = RequireSha((await GitAsync(root, cancellationToken, "write-tree")).Stdout, "checkpoint tree");
            newHead = RequireSha((await GitAsync(
                root,
                cancellationToken,
                "commit-tree",
                tree,
                "-p", FirstParentCommit,
                "-p", SecondParentCommit,
                "-m", CommitMessage)).Stdout, "v0.60.1 two-parent checkpoint commit");
            VerifyTwoParents(await GitAsync(root, cancellationToken, "rev-list", "--parents", "-n", "1", newHead));

            await GitAsync(root, cancellationToken, "update-ref", "HEAD", newHead, FirstParentCommit);
            refMoved = true;
            var observedHead = RequireSha((await GitAsync(root, cancellationToken, "rev-parse", "HEAD")).Stdout, "new HEAD");
            if (!observedHead.Equals(newHead, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("v0.60.1 checkpoint commit was not installed as current HEAD.");

            await GitAsync(root, cancellationToken, "tag", "-a", TargetTag, "-m", "Accepted Workbench v0.60.1: reviewed v0.60 plus bounded operator-host acceptance closure");
            tagged = true;
            var peeled = RequireSha((await GitAsync(root, cancellationToken, "rev-list", "-n", "1", TargetTag)).Stdout, TargetTag);
            if (!peeled.Equals(newHead, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("v0.60.1 accepted tag does not peel to exact local checkpoint.");
            if (!string.IsNullOrWhiteSpace((await GitAsync(root, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all")).Stdout))
                throw new InvalidDataException("Workbench working tree is not clean after v0.60.1 local checkpoint.");

            return new LocalCheckpointReceiptV0601(
                "matawaka.workbench-local-checkpoint-receipt/v0.60.1",
                Version,
                DateTimeOffset.Now,
                FirstParentCommit,
                SecondParentCommit,
                newHead,
                TargetTag,
                CommitMessage,
                candidate.AcceptanceArtifactPath,
                candidate.AcceptanceArtifactSha256,
                candidate.BuildReceiptPath,
                candidate.BuildReceiptSha256,
                candidate.BuildSourceManifestPath,
                candidate.BuildSourceManifestSha256,
                candidate.AppExecutableSha256,
                candidate.PublicImportReceiptPath,
                candidate.PublicImportReceiptSha256,
                candidate.ChangedFiles,
                true,
                true,
                true,
                false,
                false,
                false,
                NonEffects(),
                $"Local v0.60.1 acceptance consumed transition-bootstrap lease {bootstrapLease.LeaseId}. First parent remains exact installed accepted v0.55.2; second parent remains exact separately no-ref imported public v0.60 implementation main. Publication remains separate.");
        }
        catch
        {
            if (tagged)
                await GitAsync(root, CancellationToken.None, true, "tag", "-d", TargetTag);
            if (refMoved && newHead is not null)
                await GitAsync(root, CancellationToken.None, true, "update-ref", "HEAD", FirstParentCommit, newHead);
            await GitAsync(root, CancellationToken.None, true, "reset");
            throw;
        }
    }

    internal static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        LocalCheckpointReceiptV0601 receipt,
        CancellationToken cancellationToken)
    {
        var dir = Path.Combine(ResolveRepositoryRoot(workspaceRoot), "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"checkpoint-v0.60.1-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    internal static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("checkpoint-v0601-version", Version == "0.60.1", Version, "0.60.1"),
        ("checkpoint-v0601-first-parent", FirstParentCommit == V0601QualifiedSourceBindings.InstalledAcceptedPredecessor, FirstParentCommit, "exact installed accepted v0.55.2"),
        ("checkpoint-v0601-second-parent", SecondParentCommit == V0601QualifiedSourceBindings.PublicImplementationMain, SecondParentCommit, "exact imported public v0.60 main"),
        ("checkpoint-v0601-target-tag", TargetTag == "workbench-v0.60.1-accepted", TargetTag, "fresh successor tag"),
        ("checkpoint-v0601-no-v060-relabel", HistoricalV060Tag == "workbench-v0.60-accepted", HistoricalV060Tag, "historical implementation-only tag must remain absent"),
        ("checkpoint-v0601-two-parent", true, "commit-tree -p installed -p public; parent order reverified", "two exact ordered parents"),
        ("checkpoint-v0601-one-shot-process-binding", true, "CONSUMING + exact PID + exact process path/hash + claim file + verified launch/handoff", "checkpoint cannot rely only on caller sequencing"),
        ("checkpoint-v0601-publication", true, "RemotePushAllowed=false; NetworkAccessAllowed=false", "publication remains separate")
    };

    private static void RequireBootstrapBinding(TransitionBootstrapV040Lease lease)
    {
        if (lease.TargetVersion != Version || lease.TargetTag != TargetTag ||
            lease.PredecessorTag != ExpectedPredecessorTag ||
            !lease.PredecessorCommit.Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase) ||
            lease.State != TransitionBootstrapV040Service.ConsumingState ||
            lease.ProcessId != Environment.ProcessId ||
            !lease.AutoLaunchAllowed || !lease.FirstBootSelfTestAllowed || !lease.FirstBootAcceptIfSelfTestPassesAllowed ||
            lease.LaunchReceiptVerified != true || lease.CandidateObservedAlive != true ||
            lease.ProcessImageMatchedCandidate != true || lease.PredecessorSelfCloseEligible != true ||
            string.IsNullOrWhiteSpace(lease.ClaimPath) || !File.Exists(lease.ClaimPath) ||
            lease.PublishAllowed || lease.LifecycleAllowed || lease.RetryAuthorized)
            throw new InvalidDataException("Transition bootstrap lease does not match fixed one-shot v0.60.1 acceptance boundary.");

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath) ||
            !Path.GetFullPath(processPath).Equals(Path.GetFullPath(lease.CandidateExecutablePath), StringComparison.OrdinalIgnoreCase) ||
            !HashFile(processPath).Equals(lease.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Transition bootstrap candidate is not the exact current v0.60.1 process image.");

        var claimText = File.ReadAllText(lease.ClaimPath, Encoding.UTF8);
        var expectedLeaseLine = "lease=" + lease.LeaseId;
        var expectedPidLine = "pid=" + Environment.ProcessId;
        var claimLines = claimText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (claimLines.Length != 3 || !claimLines.Contains(expectedLeaseLine, StringComparer.Ordinal) ||
            !claimLines.Contains(expectedPidLine, StringComparer.Ordinal) ||
            claimLines.Count(line => line.StartsWith("claimed=", StringComparison.Ordinal)) != 1)
            throw new InvalidDataException("Transition bootstrap one-shot claim file does not match the current v0.60.1 lease/process.");
    }

    private static WorkbenchUpdateApplyBuildReceipt ValidateBuildReceipt(string root, TransitionBootstrapV040Lease lease)
    {
        var path = Path.GetFullPath(lease.BuildReceiptPath);
        var allowed = Path.GetFullPath(Path.Combine(root, "artifacts", "update-applies")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidDataException("v0.60.1 bootstrap build receipt path is missing or outside update-applies.");
        if (!HashFile(path).Equals(lease.BuildReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.60.1 bootstrap build receipt bytes changed.");
        var receipt = JsonSerializer.Deserialize<WorkbenchUpdateApplyBuildReceipt>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.60.1 apply/build receipt could not be parsed.");
        if (receipt.Status != "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED" ||
            receipt.TargetVersion != Version || receipt.TargetTag != TargetTag ||
            receipt.PredecessorTag != ExpectedPredecessorTag ||
            !receipt.PredecessorCommit.Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase) ||
            !receipt.CandidateExecutableSha256.Equals(lease.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.60.1 apply/build receipt identity differs from bootstrap lease.");
        return receipt;
    }

    private static (string Path, string Sha256) ValidateBuildSourceManifest(
        string root,
        WorkbenchUpdateApplyBuildReceipt build,
        string predecessor,
        IReadOnlyList<string> changed)
    {
        var path = Path.GetFullPath(build.BuildSourceManifestPath);
        var allowed = Path.GetFullPath(Path.Combine(root, "artifacts", "checkpoints")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidDataException("v0.60.1 build source manifest is missing or outside checkpoints.");
        if (!HashFile(path).Equals(build.BuildSourceManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.60.1 build source manifest SHA differs from apply/build receipt.");

        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var obj = doc.RootElement;
        if (obj.GetProperty("Schema").GetString() != BuildManifestSchema ||
            obj.GetProperty("Version").GetString() != Version ||
            !string.Equals(obj.GetProperty("PredecessorGitSha").GetString(), predecessor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected v0.60.1 build source manifest identity/predecessor.");
        var bound = obj.GetProperty("Files").EnumerateArray()
            .Select(x => (Path: x.GetProperty("Path").GetString() ?? string.Empty, Sha256: x.GetProperty("Sha256").GetString() ?? string.Empty))
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ToArray();
        var current = changed.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!bound.Select(x => x.Path).SequenceEqual(current, StringComparer.Ordinal))
            throw new InvalidDataException("Changed-file set differs from exact v0.60.1 build source manifest.");
        foreach (var item in bound)
        {
            var full = Path.GetFullPath(Path.Combine(root, item.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(full) || !HashFile(full).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Build-bound v0.60.1 source file drift: {item.Path}");
        }
        return (path, HashFile(path));
    }

    private static string ValidateAcceptanceArtifact(string root, string artifactPath, WorkbenchAcceptanceReceipt acceptance)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
            throw new InvalidDataException("Passing v0.60.1 acceptance artifact is missing.");
        var full = Path.GetFullPath(artifactPath);
        var allowed = Path.GetFullPath(Path.Combine(root, "artifacts", "acceptance")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.60.1 acceptance artifact must be under Workbench/artifacts/acceptance.");
        var parsed = JsonSerializer.Deserialize<WorkbenchAcceptanceReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("v0.60.1 acceptance artifact could not be parsed.");
        if (!parsed.Passed || parsed.Schema != AcceptanceSchema || parsed.Version != Version || parsed.RunId != acceptance.RunId ||
            !parsed.AppExecutableSha256.Equals(acceptance.AppExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance artifact does not match passing in-memory v0.60.1 receipt.");
        return full;
    }

    private static void RequirePassingAcceptance(WorkbenchAcceptanceReceipt acceptance)
    {
        if (!acceptance.Passed || acceptance.Version != Version || acceptance.Schema != AcceptanceSchema)
            throw new InvalidDataException("A passing exact Workbench v0.60.1 acceptance receipt is required.");
    }

    private static async Task RequireImportedSecondParentAsync(string root, CancellationToken cancellationToken)
    {
        var cat = await GitAsync(root, cancellationToken, true, "cat-file", "-e", SecondParentCommit + "^{commit}");
        if (cat.ExitCode != 0)
            throw new InvalidDataException("Exact public v0.60 main commit object is not locally available; fixed no-ref import is required first.");
    }

    private static void VerifyTwoParents((int ExitCode, string Stdout, string Stderr) result)
    {
        if (result.ExitCode != 0)
            throw new InvalidDataException("Unable to inspect v0.60.1 two-parent checkpoint commit.");
        var parts = result.Stdout.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !parts[1].Equals(FirstParentCommit, StringComparison.OrdinalIgnoreCase) ||
            !parts[2].Equals(SecondParentCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.60.1 checkpoint does not have exact ordered two-parent ancestry.");
    }

    private static void VerifyRunningExecutable(string expectedSha256)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !HashFile(path).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Running executable digest does not match v0.60.1 acceptance receipt.");
    }

    private static IReadOnlyList<string> NonEffects() => new[]
    {
        "local checkpoint performs no remote publication or network access",
        "v0.60 reviewed implementation bytes are evidence and are not retroactively tagged as accepted v0.60",
        "public v0.60 main object availability is not publication authority",
        "historical v0.55/v0.56/v0.60 receipts are not reinterpreted",
        "no artifact acquisition/materialization/runtime execution/model invocation/benchmark/game/display authority",
        "no ResponseAuthority, DisplayPermit, Agent Execute, ActionPermit or SuccessorPermit",
        "no automatic retry; bootstrap failure is terminal FAILED_NO_RETRY"
    };

    private static IReadOnlyList<string> ParseStatusPaths(string text)
        => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length >= 4 ? line[3..].Trim() : line.Trim())
            .Select(path => path.Contains(" -> ", StringComparison.Ordinal) ? path.Split(" -> ", StringSplitOptions.None)[^1].Trim() : path)
            .Select(path => path.Trim('"').Replace('\\', '/'))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

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
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(Path.GetFullPath(workspaceRoot.Trim()), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidDataException($"Workbench Git repository is missing: {root}");
        return root;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RequireSha(string text, string label)
    {
        var sha = text.Trim().ToLowerInvariant();
        if (sha.Length != 40 || sha.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidDataException($"Invalid Git SHA for {label}: {text.Trim()}");
        return sha;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> GitAsync(
        string root,
        CancellationToken cancellationToken,
        params string[] args)
        => await GitAsync(root, cancellationToken, false, args);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> GitAsync(
        string root,
        CancellationToken cancellationToken,
        bool allowFailure,
        params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidDataException("Unable to start fixed local Git checkpoint process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var wait = process.WaitForExitAsync(cancellationToken);
        if (await Task.WhenAny(wait, Task.Delay(GitTimeout, cancellationToken)) != wait)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("Fixed v0.60.1 Git checkpoint operation timed out.");
        }
        await wait;
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (!allowFailure && process.ExitCode != 0)
            throw new InvalidDataException("Fixed v0.60.1 Git checkpoint operation failed: " + stderr.Trim());
        return (process.ExitCode, stdout, stderr);
    }
}
