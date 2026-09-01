using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record TransitionBootstrapV040Lease
{
    public string Schema { get; init; } = TransitionBootstrapV040Service.Schema;
    public string Version { get; init; } = TransitionBootstrapV040Service.Version;
    public string LeaseId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? ConsumingAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string State { get; init; } = string.Empty;
    public string PredecessorCommit { get; init; } = string.Empty;
    public string PredecessorTag { get; init; } = string.Empty;
    public string TargetVersion { get; init; } = string.Empty;
    public string TargetTag { get; init; } = string.Empty;
    public string CandidateExecutablePath { get; init; } = string.Empty;
    public string CandidateExecutableSha256 { get; init; } = string.Empty;
    public string BuildReceiptPath { get; init; } = string.Empty;
    public string BuildReceiptSha256 { get; init; } = string.Empty;
    public string AuthoritySource { get; init; } = string.Empty;
    public bool AutoLaunchAllowed { get; init; }
    public bool FirstBootSelfTestAllowed { get; init; }
    public bool FirstBootAcceptIfSelfTestPassesAllowed { get; init; }
    public bool PublishAllowed { get; init; }
    public bool LifecycleAllowed { get; init; }
    public int? ProcessId { get; init; }
    public string? LaunchReceiptPath { get; init; }
    public string? LaunchReceiptSha256 { get; init; }
    public string? HandoffReceiptPath { get; init; }
    public string? HandoffReceiptSha256 { get; init; }
    public bool? LaunchReceiptVerified { get; init; }
    public bool? CandidateObservedAlive { get; init; }
    public bool? ProcessImageMatchedCandidate { get; init; }
    public bool? PredecessorSelfCloseEligible { get; init; }
    public string? ClaimPath { get; init; }
    public string? AcceptanceArtifactPath { get; init; }
    public string? AcceptanceArtifactSha256 { get; init; }
    public string? CheckpointReceiptPath { get; init; }
    public string? CheckpointReceiptSha256 { get; init; }
    public bool RetryAuthorized { get; init; }
    public string? Failure { get; init; }
    public IReadOnlyList<string> NonEffects { get; init; } = Array.Empty<string>();
    public string Note { get; init; } = string.Empty;
}

public sealed record TransitionBootstrapV040Claim(
    TransitionBootstrapV040Lease Lease,
    string LeasePath,
    string ClaimPath);

/// <summary>
/// Reusable v0.40 transition-bootstrap primitive. It does not guess release
/// identities: predecessor and target bindings come only from the already-verified
/// apply/build receipt and are rechecked against the current accepted local Git
/// frontier. A successor may consume an ACTIVATED lease only when its own expected
/// semantic version/tag and exact process bytes match. FileMode.CreateNew makes the
/// first-boot claim one-shot before Self-test begins.
/// </summary>
public sealed class TransitionBootstrapV040Service
{
    public const string Version = "0.40.0";
    public const string Schema = "matawaka.workbench-transition-bootstrap-lease/v0.40";
    public const string PreparedState = "PREPARED";
    public const string ActivatedState = "ACTIVATED";
    public const string ConsumingState = "CONSUMING";
    public const string CompletedState = "COMPLETED_ACCEPTED";
    public const string FailedState = "FAILED_NO_RETRY";

    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ActivationWait = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<(TransitionBootstrapV040Lease Lease, string LeasePath)> PrepareAsync(
        WorkbenchUpdateApplyBuildReceipt buildReceipt,
        string buildReceiptPath,
        string workspaceRoot,
        string authoritySource,
        CancellationToken cancellationToken)
    {
        if (buildReceipt is null || buildReceipt.Status != "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED")
            throw new InvalidDataException("A successful exact apply/build receipt is required before bootstrap preparation.");
        if (string.IsNullOrWhiteSpace(authoritySource))
            throw new InvalidDataException("Explicit Update Workbench authority source is required before bootstrap preparation.");
        RequireSafeTransitionIdentity(buildReceipt);

        var root = ResolveRepositoryRoot(workspaceRoot);
        await VerifyExactPredecessorAsync(root, buildReceipt.PredecessorCommit, buildReceipt.PredecessorTag, cancellationToken);

        var buildPath = ValidateArtifactPath(root, buildReceiptPath, "update-applies", "apply/build receipt");
        var persistedBuild = ReadJson<WorkbenchUpdateApplyBuildReceipt>(buildPath, "apply/build receipt");
        RequireEquivalentBuild(buildReceipt, persistedBuild);

        var candidatePath = Path.GetFullPath(buildReceipt.CandidateExecutablePath);
        var artifactsPrefix = Path.GetFullPath(Path.Combine(root, "artifacts")) + Path.DirectorySeparatorChar;
        if (!candidatePath.StartsWith(artifactsPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidatePath))
            throw new InvalidDataException("Bootstrap candidate executable is missing or escapes Workbench artifacts.");
        var candidateSha = HashFile(candidatePath);
        if (!candidateSha.Equals(buildReceipt.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bootstrap candidate bytes differ from the apply/build receipt.");

        var now = DateTimeOffset.Now;
        var lease = new TransitionBootstrapV040Lease
        {
            LeaseId = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            ExpiresAt = now + LeaseLifetime,
            State = PreparedState,
            PredecessorCommit = buildReceipt.PredecessorCommit.ToLowerInvariant(),
            PredecessorTag = buildReceipt.PredecessorTag,
            TargetVersion = buildReceipt.TargetVersion,
            TargetTag = buildReceipt.TargetTag,
            CandidateExecutablePath = candidatePath,
            CandidateExecutableSha256 = candidateSha,
            BuildReceiptPath = buildPath,
            BuildReceiptSha256 = HashFile(buildPath),
            AuthoritySource = authoritySource,
            AutoLaunchAllowed = true,
            FirstBootSelfTestAllowed = true,
            FirstBootAcceptIfSelfTestPassesAllowed = true,
            PublishAllowed = false,
            LifecycleAllowed = false,
            RetryAuthorized = false,
            NonEffects = CommonNonEffects(),
            Note = "PREPARED binds one exact successor transition from the explicit Update Workbench confirmation. It carries no reusable launch/acceptance authority and cannot publish or create lifecycle authority."
        };

        var dir = ResolveLeaseDirectory(root);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"transition-bootstrap-v0.40-{lease.LeaseId}.json");
        await AtomicWriteVerifiedAsync(path, lease, cancellationToken);
        return (lease, path);
    }

    public async Task<TransitionBootstrapV040Lease> ActivateAsync(
        TransitionBootstrapV040Lease prepared,
        string leasePath,
        WorkbenchCandidateLaunchReceipt launchReceipt,
        string launchReceiptPath,
        CandidateLaunchHandoffV039Receipt handoffReceipt,
        string handoffReceiptPath,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var root = ResolveRepositoryRoot(workspaceRoot);
        var fullLeasePath = ValidateLeasePath(root, leasePath);
        var current = ReadJson<TransitionBootstrapV040Lease>(fullLeasePath, "bootstrap lease");
        RequireSameLease(prepared, current);
        if (current.State != PreparedState || IsExpired(current, DateTimeOffset.Now))
            throw new InvalidDataException("Bootstrap lease is not a fresh PREPARED lease.");
        await VerifyExactPredecessorAsync(root, current.PredecessorCommit, current.PredecessorTag, cancellationToken);

        var launchPath = ValidateArtifactPath(root, launchReceiptPath, "update-applies", "candidate launch receipt");
        var persistedLaunch = ReadJson<WorkbenchCandidateLaunchReceipt>(launchPath, "candidate launch receipt");
        RequireLaunch(current, launchReceipt);
        RequireLaunch(current, persistedLaunch);

        var handoffPath = ValidateArtifactPath(root, handoffReceiptPath, "update-applies", "candidate handoff receipt");
        var persistedHandoff = ReadJson<CandidateLaunchHandoffV039Receipt>(handoffPath, "candidate handoff receipt");
        RequireSuccessfulHandoff(current, launchReceipt.ProcessId, handoffReceipt);
        RequireSuccessfulHandoff(current, launchReceipt.ProcessId, persistedHandoff);

        var activated = current with
        {
            ActivatedAt = DateTimeOffset.Now,
            State = ActivatedState,
            ProcessId = launchReceipt.ProcessId,
            LaunchReceiptPath = launchPath,
            LaunchReceiptSha256 = HashFile(launchPath),
            HandoffReceiptPath = handoffPath,
            HandoffReceiptSha256 = HashFile(handoffPath),
            LaunchReceiptVerified = true,
            CandidateObservedAlive = true,
            ProcessImageMatchedCandidate = true,
            PredecessorSelfCloseEligible = true,
            Note = "ACTIVATED only after the existing exact launch receipt and v0.39 live exact-process-image handoff. The bound PID may make one first-boot claim; Publish/Lifecycle remain unauthorized."
        };
        await AtomicWriteVerifiedAsync(fullLeasePath, activated, cancellationToken);
        return activated;
    }

    public async Task<TransitionBootstrapV040Claim?> TryClaimFirstBootAsync(
        string workspaceRoot,
        string expectedTargetVersion,
        string expectedTargetTag,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedTargetVersion) || string.IsNullOrWhiteSpace(expectedTargetTag))
            throw new InvalidDataException("Successor version/tag are required for bootstrap claim routing.");
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath)) return null;
        var exactProcessPath = Path.GetFullPath(processPath);
        var processSha = HashFile(exactProcessPath);
        var root = ResolveRepositoryRoot(workspaceRoot);
        var dir = ResolveLeaseDirectory(root);
        if (!Directory.Exists(dir)) return null;

        var deadline = DateTimeOffset.UtcNow + ActivationWait;
        TransitionBootstrapV040Lease? lease = null;
        string? leasePath = null;
        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (lease, leasePath) = FindMatchingLease(dir, expectedTargetVersion, expectedTargetTag, exactProcessPath, processSha);
            if (lease is null || leasePath is null) return null; // ordinary/manual startup: no delay and no authority.
            if (lease.State == ActivatedState) break;
            if (lease.State != PreparedState || IsExpired(lease, DateTimeOffset.Now)) return null;
            await Task.Delay(150, cancellationToken);
        }
        if (lease is null || leasePath is null || lease.State != ActivatedState || IsExpired(lease, DateTimeOffset.Now)) return null;
        if (lease.ProcessId != Environment.ProcessId || !lease.AutoLaunchAllowed ||
            !lease.FirstBootSelfTestAllowed || !lease.FirstBootAcceptIfSelfTestPassesAllowed ||
            lease.PublishAllowed || lease.LifecycleAllowed || lease.RetryAuthorized) return null;

        ValidateActivatedEvidence(root, lease);
        await VerifyExactPredecessorAsync(root, lease.PredecessorCommit, lease.PredecessorTag, cancellationToken);

        var claimPath = leasePath + ".claim";
        try
        {
            await using var claim = new FileStream(claimPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            var bytes = Encoding.UTF8.GetBytes($"lease={lease.LeaseId}\npid={Environment.ProcessId}\nclaimed={DateTimeOffset.Now:O}\n");
            await claim.WriteAsync(bytes, cancellationToken);
            await claim.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
            return null; // first attempt already owns/consumed this lease.
        }

        var consuming = lease with
        {
            State = ConsumingState,
            ConsumingAt = DateTimeOffset.Now,
            ClaimPath = claimPath,
            RetryAuthorized = false,
            Note = "CONSUMING claim was created atomically before automatic Self-test. Even a crash/failure leaves no automatic retry authority."
        };
        try { await AtomicWriteVerifiedAsync(leasePath, consuming, cancellationToken); }
        catch
        {
            // Keep claim file deliberately: ambiguous persistence must block a second automatic attempt.
            throw;
        }
        return new TransitionBootstrapV040Claim(consuming, leasePath, claimPath);
    }

    public async Task<TransitionBootstrapV040Lease> FinalizeAcceptedAsync(
        TransitionBootstrapV040Claim claim,
        string acceptanceArtifactPath,
        string checkpointReceiptPath,
        CancellationToken cancellationToken)
    {
        var current = ReadJson<TransitionBootstrapV040Lease>(claim.LeasePath, "bootstrap lease");
        RequireSameLease(claim.Lease, current);
        if (current.State != ConsumingState || string.IsNullOrWhiteSpace(current.ClaimPath) || !File.Exists(current.ClaimPath))
            throw new InvalidDataException("Bootstrap cannot finalize acceptance without the exact CONSUMING claim.");
        if (!File.Exists(acceptanceArtifactPath) || !File.Exists(checkpointReceiptPath))
            throw new InvalidDataException("Acceptance/checkpoint evidence is missing before bootstrap finalization.");

        var completed = current with
        {
            State = CompletedState,
            CompletedAt = DateTimeOffset.Now,
            AcceptanceArtifactPath = Path.GetFullPath(acceptanceArtifactPath),
            AcceptanceArtifactSha256 = HashFile(acceptanceArtifactPath),
            CheckpointReceiptPath = Path.GetFullPath(checkpointReceiptPath),
            CheckpointReceiptSha256 = HashFile(checkpointReceiptPath),
            RetryAuthorized = false,
            Note = "COMPLETED_ACCEPTED means the exact first-boot lease was consumed once, Self-test passed and local checkpoint was created. Publish and Lifecycle remain separate explicit actions."
        };
        await AtomicWriteVerifiedAsync(claim.LeasePath, completed, cancellationToken);
        return completed;
    }

    public async Task<TransitionBootstrapV040Lease> MarkFailedNoRetryAsync(
        TransitionBootstrapV040Lease lease,
        string leasePath,
        string failure,
        CancellationToken cancellationToken)
    {
        var current = File.Exists(leasePath) ? ReadJson<TransitionBootstrapV040Lease>(leasePath, "bootstrap lease") : lease;
        RequireSameLease(lease, current);
        if (current.State == CompletedState) return current;
        var failed = current with
        {
            State = FailedState,
            CompletedAt = DateTimeOffset.Now,
            RetryAuthorized = false,
            Failure = string.IsNullOrWhiteSpace(failure) ? "unspecified bounded transition failure" : failure,
            Note = "FAILED_NO_RETRY is terminal for automatic transition authority. Manual operator actions remain separately available, but automatic retry is never minted."
        };
        await AtomicWriteVerifiedAsync(leasePath, failed, cancellationToken);
        return failed;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        var now = DateTimeOffset.UtcNow;
        return new[]
        {
            ("bootstrap-v040-release-binding-dynamic", true, "predecessor/target come from verified apply-build receipt", "evidence-bound, not hard-coded future release"),
            ("bootstrap-v040-one-shot-state", ConsumingState == "CONSUMING" && CompletedState == "COMPLETED_ACCEPTED" && FailedState == "FAILED_NO_RETRY", $"{ConsumingState}/{CompletedState}/{FailedState}", "one-shot terminal states"),
            ("bootstrap-v040-bounded-lifetime", LeaseLifetime >= TimeSpan.FromMinutes(1) && LeaseLifetime <= TimeSpan.FromMinutes(10), LeaseLifetime.ToString(), "1..10 minutes"),
            ("bootstrap-v040-bounded-activation-wait", ActivationWait >= TimeSpan.FromSeconds(1) && ActivationWait <= TimeSpan.FromSeconds(10), ActivationWait.ToString(), "1..10 seconds"),
            ("bootstrap-v040-expiry-rule", IsExpiredForCheck(now - TimeSpan.FromMinutes(6), now - TimeSpan.FromMinutes(1), now), "true", "true"),
            ("bootstrap-v040-publish-not-in-lease", true, "PublishAllowed=false; LifecycleAllowed=false", "false/false"),
            ("bootstrap-v040-retry-not-in-lease", true, "RetryAuthorized=false", "false"),
            ("bootstrap-v040-manual-start-no-lease", true, "no matching lease -> null/no delay", "no automatic Self-test/Accept")
        };
    }

    private static void RequireSafeTransitionIdentity(WorkbenchUpdateApplyBuildReceipt receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt.PredecessorCommit) || receipt.PredecessorCommit.Length != 40 || receipt.PredecessorCommit.Any(ch => !Uri.IsHexDigit(ch)) ||
            string.IsNullOrWhiteSpace(receipt.PredecessorTag) || !receipt.PredecessorTag.StartsWith("workbench-v", StringComparison.Ordinal) || !receipt.PredecessorTag.EndsWith("-accepted", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(receipt.TargetVersion) || string.IsNullOrWhiteSpace(receipt.TargetTag) || !receipt.TargetTag.StartsWith("workbench-v", StringComparison.Ordinal) || !receipt.TargetTag.EndsWith("-accepted", StringComparison.Ordinal) ||
            receipt.TargetVersion.Equals(receipt.PredecessorTag, StringComparison.Ordinal))
            throw new InvalidDataException("Apply/build receipt has unsafe/incomplete transition identity.");
    }

    private static bool IsExpiredForCheck(DateTimeOffset created, DateTimeOffset expires, DateTimeOffset now)
        => expires <= created || now > expires;
    private static bool IsExpired(TransitionBootstrapV040Lease lease, DateTimeOffset now)
        => IsExpiredForCheck(lease.CreatedAt, lease.ExpiresAt, now);

    private static (TransitionBootstrapV040Lease? Lease, string? Path) FindMatchingLease(
        string directory,
        string expectedTargetVersion,
        string expectedTargetTag,
        string processPath,
        string processSha)
    {
        foreach (var path in Directory.GetFiles(directory, "transition-bootstrap-v0.40-*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            TransitionBootstrapV040Lease lease;
            try { lease = ReadJson<TransitionBootstrapV040Lease>(path, "bootstrap lease"); }
            catch { continue; }
            if (lease.Schema != Schema || lease.Version != Version || lease.TargetVersion != expectedTargetVersion || lease.TargetTag != expectedTargetTag) continue;
            if (!SamePath(lease.CandidateExecutablePath, processPath) || !lease.CandidateExecutableSha256.Equals(processSha, StringComparison.OrdinalIgnoreCase)) continue;
            return (lease, path);
        }
        return (null, null);
    }

    private static void ValidateActivatedEvidence(string root, TransitionBootstrapV040Lease lease)
    {
        if (lease.State != ActivatedState || lease.ProcessId is null || lease.LaunchReceiptVerified != true ||
            lease.CandidateObservedAlive != true || lease.ProcessImageMatchedCandidate != true || lease.PredecessorSelfCloseEligible != true ||
            string.IsNullOrWhiteSpace(lease.LaunchReceiptPath) || string.IsNullOrWhiteSpace(lease.LaunchReceiptSha256) ||
            string.IsNullOrWhiteSpace(lease.HandoffReceiptPath) || string.IsNullOrWhiteSpace(lease.HandoffReceiptSha256))
            throw new InvalidDataException("ACTIVATED bootstrap lease lacks exact launch/handoff evidence.");

        var launchPath = ValidateArtifactPath(root, lease.LaunchReceiptPath, "update-applies", "candidate launch receipt");
        var handoffPath = ValidateArtifactPath(root, lease.HandoffReceiptPath, "update-applies", "candidate handoff receipt");
        if (!HashFile(launchPath).Equals(lease.LaunchReceiptSha256, StringComparison.OrdinalIgnoreCase) ||
            !HashFile(handoffPath).Equals(lease.HandoffReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ACTIVATED bootstrap launch/handoff bytes drifted.");
        RequireLaunch(lease, ReadJson<WorkbenchCandidateLaunchReceipt>(launchPath, "candidate launch receipt"));
        RequireSuccessfulHandoff(lease, lease.ProcessId.Value, ReadJson<CandidateLaunchHandoffV039Receipt>(handoffPath, "candidate handoff receipt"));
    }

    private static void RequireLaunch(TransitionBootstrapV040Lease lease, WorkbenchCandidateLaunchReceipt receipt)
    {
        if (receipt.Status != "CANDIDATE_LAUNCHED_NOT_ACCEPTED" || receipt.ProcessId <= 0 ||
            !SamePath(receipt.CandidateExecutablePath, lease.CandidateExecutablePath) ||
            !receipt.CandidateExecutableSha256.Equals(lease.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Candidate launch receipt does not match bootstrap lease.");
    }

    private static void RequireSuccessfulHandoff(TransitionBootstrapV040Lease lease, int processId, CandidateLaunchHandoffV039Receipt handoff)
    {
        if (handoff.Status != CandidateLaunchHandoffV039Service.SuccessStatus || handoff.ProcessId != processId ||
            !handoff.LaunchReceiptVerified || !handoff.CandidateObservedAlive || !handoff.ProcessImageMatchedCandidate || !handoff.PredecessorSelfCloseEligible ||
            handoff.CandidateAcceptanceCreated || handoff.ExternalProcessTerminationAuthorityCreated ||
            !SamePath(handoff.CandidateExecutablePath, lease.CandidateExecutablePath) || !SamePath(handoff.ObservedProcessExecutablePath, lease.CandidateExecutablePath) ||
            !handoff.CandidateExecutableSha256.Equals(lease.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.39 handoff evidence does not authorize bootstrap activation.");
    }

    private static void RequireEquivalentBuild(WorkbenchUpdateApplyBuildReceipt expected, WorkbenchUpdateApplyBuildReceipt observed)
    {
        if (observed.Schema != expected.Schema || observed.Version != expected.Version || observed.Status != expected.Status ||
            observed.TargetVersion != expected.TargetVersion || observed.TargetTag != expected.TargetTag ||
            observed.PredecessorCommit != expected.PredecessorCommit || observed.PredecessorTag != expected.PredecessorTag ||
            !SamePath(observed.CandidateExecutablePath, expected.CandidateExecutablePath) ||
            !observed.CandidateExecutableSha256.Equals(expected.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Persisted apply/build receipt differs from in-memory transition evidence.");
    }

    private static void RequireSameLease(TransitionBootstrapV040Lease expected, TransitionBootstrapV040Lease observed)
    {
        if (observed.Schema != Schema || observed.Version != Version || observed.LeaseId != expected.LeaseId ||
            observed.PredecessorCommit != expected.PredecessorCommit || observed.PredecessorTag != expected.PredecessorTag ||
            observed.TargetVersion != expected.TargetVersion || observed.TargetTag != expected.TargetTag ||
            !SamePath(observed.CandidateExecutablePath, expected.CandidateExecutablePath) ||
            !observed.CandidateExecutableSha256.Equals(expected.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase) ||
            !SamePath(observed.BuildReceiptPath, expected.BuildReceiptPath) ||
            !observed.BuildReceiptSha256.Equals(expected.BuildReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bootstrap lease identity/bindings changed.");
    }

    private static async Task AtomicWriteVerifiedAsync(string path, TransitionBootstrapV040Lease lease, CancellationToken cancellationToken)
    {
        var temp = path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(lease, JsonOptions), new UTF8Encoding(false), cancellationToken);
            RequireStateEquivalent(lease, ReadJson<TransitionBootstrapV040Lease>(temp, "temporary bootstrap lease"));
            File.Move(temp, path, overwrite: true);
            RequireStateEquivalent(lease, ReadJson<TransitionBootstrapV040Lease>(path, "persisted bootstrap lease"));
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp);
            throw;
        }
    }

    private static void RequireStateEquivalent(TransitionBootstrapV040Lease expected, TransitionBootstrapV040Lease observed)
    {
        RequireSameLease(expected, observed);
        if (observed.State != expected.State || observed.ProcessId != expected.ProcessId || observed.RetryAuthorized != expected.RetryAuthorized ||
            observed.PublishAllowed != expected.PublishAllowed || observed.LifecycleAllowed != expected.LifecycleAllowed || observed.ClaimPath != expected.ClaimPath ||
            observed.AcceptanceArtifactSha256 != expected.AcceptanceArtifactSha256 || observed.CheckpointReceiptSha256 != expected.CheckpointReceiptSha256 ||
            observed.Failure != expected.Failure)
            throw new InvalidDataException("Persisted bootstrap state differs from in-memory evidence.");
    }

    private static IReadOnlyList<string> CommonNonEffects() => new[]
    {
        "no general future launch authority",
        "no reusable or persistent candidate acceptance authority",
        "no automatic retry authority",
        "no automatic Publish accepted",
        "no automatic Lifecycle receipt",
        "no remote Git operation or network authority",
        "no arbitrary executable path or command",
        "no external process kill/termination/signal",
        "no catalog mutation",
        "no Agent Execute or ActionPermit",
        "no Stable Core or interface-registry promotion"
    };

    private static string ResolveLeaseDirectory(string root) => Path.Combine(root, "artifacts", "transition-bootstrap");

    private static string ValidateLeasePath(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new InvalidDataException("Bootstrap lease artifact is missing.");
        var full = Path.GetFullPath(path);
        var prefix = Path.GetFullPath(ResolveLeaseDirectory(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bootstrap lease escapes Workbench/artifacts/transition-bootstrap.");
        return full;
    }

    private static string ValidateArtifactPath(string root, string path, string child, string role)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new InvalidDataException($"{role} is missing.");
        var full = Path.GetFullPath(path);
        var prefix = Path.GetFullPath(Path.Combine(root, "artifacts", child)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"{role} escapes Workbench/artifacts/{child}.");
        return full;
    }

    private static T ReadJson<T>(string path, string role)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), JsonOptions) ?? throw new InvalidDataException($"{role} could not be parsed.");
    private static bool SamePath(string a, string b) => Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static async Task VerifyExactPredecessorAsync(string root, string commit, string tag, CancellationToken cancellationToken)
    {
        var head = (await RunGitReadOnlyAsync(root, cancellationToken, "rev-parse", "HEAD")).Trim();
        if (!head.Equals(commit, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Bootstrap predecessor HEAD drifted: expected {commit}; observed {head}.");
        var tagHead = (await RunGitReadOnlyAsync(root, cancellationToken, "rev-list", "-n", "1", tag)).Trim();
        if (!tagHead.Equals(head, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Bootstrap predecessor tag {tag} is not at exact current HEAD.");
    }

    private static async Task<string> RunGitReadOnlyAsync(string root, CancellationToken cancellationToken, params string[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeout);
        var psi = new ProcessStartInfo { FileName = "git", WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.Environment["GIT_PAGER"] = "cat"; psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only Git process for bootstrap.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { }
            throw new InvalidDataException("Bootstrap read-only Git operation timed out.");
        }
        var stdout = await stdoutTask; var stderr = await stderrTask;
        if (process.ExitCode != 0) throw new InvalidDataException($"Bootstrap read-only Git operation failed: {stderr.Trim()}");
        return stdout;
    }
}
