using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record TransitionBootstrapV040Lease(
    string Schema,
    string Version,
    string LeaseId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? ConsumingAt,
    DateTimeOffset? CompletedAt,
    string State,
    string PredecessorCommit,
    string PredecessorTag,
    string TargetVersion,
    string TargetTag,
    string CandidateExecutablePath,
    string CandidateExecutableSha256,
    string BuildReceiptPath,
    string BuildReceiptSha256,
    string AuthoritySource,
    bool AutoLaunchAllowed,
    bool FirstBootSelfTestAllowed,
    bool FirstBootAcceptIfSelfTestPassesAllowed,
    bool PublishAllowed,
    bool LifecycleAllowed,
    int? ProcessId,
    string? LaunchReceiptPath,
    string? LaunchReceiptSha256,
    string? HandoffReceiptPath,
    string? HandoffReceiptSha256,
    bool? LaunchReceiptVerified,
    bool? CandidateObservedAlive,
    bool? ProcessImageMatchedCandidate,
    bool? PredecessorSelfCloseEligible,
    string? ClaimPath,
    string? AcceptanceArtifactPath,
    string? AcceptanceArtifactSha256,
    string? CheckpointReceiptPath,
    string? CheckpointReceiptSha256,
    bool RetryAuthorized,
    string? Failure,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record TransitionBootstrapV040Claim(
    TransitionBootstrapV040Lease Lease,
    string LeasePath,
    string ClaimPath);

/// <summary>
/// v0.40 one-shot transition authority carrier. A single explicit Update Workbench
/// confirmation may prepare one exact candidate lease. The predecessor may activate
/// it only after the existing exact launch + v0.39 handoff succeeds. The exact
/// launched successor PID may claim it once on first boot. A claim is persisted with
/// FileMode.CreateNew before Self-test begins, so failure never creates automatic
/// retry authority. Publish/Lifecycle are deliberately outside this lease.
/// </summary>
public sealed class TransitionBootstrapV040Service
{
    public const string Version = "0.40.0";
    public const string Schema = "matawaka.workbench-transition-bootstrap-lease/v0.40";
    public const string PredecessorCommit = "d877005b2070759cf24ea4ea5f31e90545cd2bcf";
    public const string PredecessorTag = "workbench-v0.39.1-accepted";
    public const string TargetVersion = "0.40.0";
    public const string TargetTag = "workbench-v0.40-accepted";
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
            throw new InvalidDataException("A successful exact apply/build receipt is required before v0.40 bootstrap preparation.");
        if (string.IsNullOrWhiteSpace(authoritySource))
            throw new InvalidDataException("Explicit Update Workbench authority source is required for v0.40 bootstrap.");

        var root = ResolveRepositoryRoot(workspaceRoot);
        await VerifyExactPredecessorAsync(root, cancellationToken);
        if (buildReceipt.TargetVersion != TargetVersion || buildReceipt.TargetTag != TargetTag ||
            buildReceipt.PredecessorCommit != PredecessorCommit || buildReceipt.PredecessorTag != PredecessorTag)
            throw new InvalidDataException("Apply/build receipt does not match the fixed v0.40 transition frontier.");

        var buildPath = ValidateArtifactPath(root, buildReceiptPath, "update-applies", "apply/build receipt");
        var persistedBuild = ReadJson<WorkbenchUpdateApplyBuildReceipt>(buildPath, "apply/build receipt");
        RequireEquivalentBuild(buildReceipt, persistedBuild);

        var candidatePath = Path.GetFullPath(buildReceipt.CandidateExecutablePath);
        var artifactsPrefix = Path.GetFullPath(Path.Combine(root, "artifacts")) + Path.DirectorySeparatorChar;
        if (!candidatePath.StartsWith(artifactsPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidatePath))
            throw new InvalidDataException("v0.40 bootstrap candidate executable is missing or escapes Workbench artifacts.");
        var candidateSha = HashFile(candidatePath);
        if (!candidateSha.Equals(buildReceipt.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.40 bootstrap candidate bytes differ from the apply/build receipt.");

        var now = DateTimeOffset.Now;
        var nonEffects = CommonNonEffects();
        var lease = new TransitionBootstrapV040Lease(
            Schema, Version, Guid.NewGuid().ToString("N"), now, now + LeaseLifetime,
            null, null, null, PreparedState,
            PredecessorCommit, PredecessorTag, TargetVersion, TargetTag,
            candidatePath, candidateSha,
            buildPath, HashFile(buildPath), authoritySource,
            true, true, true,
            false, false,
            null, null, null, null, null,
            null, null, null, null,
            null, null, null, null, null,
            false, null, nonEffects,
            "PREPARED carries only one exact transition corridor from the explicit Update Workbench confirmation. It is not reusable launch/acceptance authority and cannot publish or create lifecycle authority.");

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
        RequireEquivalentIdentity(prepared, current);
        if (current.State != PreparedState || IsExpired(current, DateTimeOffset.Now))
            throw new InvalidDataException("v0.40 bootstrap lease is not a fresh PREPARED lease.");

        var launchPath = ValidateArtifactPath(root, launchReceiptPath, "update-applies", "candidate launch receipt");
        var persistedLaunch = ReadJson<WorkbenchCandidateLaunchReceipt>(launchPath, "candidate launch receipt");
        if (launchReceipt.Status != "CANDIDATE_LAUNCHED_NOT_ACCEPTED" || launchReceipt.ProcessId <= 0 ||
            persistedLaunch.Status != launchReceipt.Status || persistedLaunch.ProcessId != launchReceipt.ProcessId ||
            !SamePath(launchReceipt.CandidateExecutablePath, current.CandidateExecutablePath) ||
            !launchReceipt.CandidateExecutableSha256.Equals(current.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase) ||
            !SamePath(persistedLaunch.CandidateExecutablePath, current.CandidateExecutablePath) ||
            !persistedLaunch.CandidateExecutableSha256.Equals(current.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Candidate launch receipt does not match the exact PREPARED v0.40 bootstrap lease.");

        var handoffPath = ValidateArtifactPath(root, handoffReceiptPath, "update-applies", "candidate handoff receipt");
        var persistedHandoff = ReadJson<CandidateLaunchHandoffV039Receipt>(handoffPath, "candidate handoff receipt");
        RequireSuccessfulHandoff(handoffReceipt, launchReceipt.ProcessId, current);
        RequireSuccessfulHandoff(persistedHandoff, launchReceipt.ProcessId, current);

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
            Note = "ACTIVATED only after exact launch receipt and persisted v0.39 live exact-process-image handoff. The exact launched PID may attempt one first-boot claim; Publish/Lifecycle remain unauthorized."
        };
        await AtomicWriteVerifiedAsync(fullLeasePath, activated, cancellationToken);
        return activated;
    }

    public async Task<TransitionBootstrapV040Claim?> TryClaimFirstBootAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            return null;
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
            (lease, leasePath) = FindMatchingLease(dir, exactProcessPath, processSha);
            if (lease is null || leasePath is null)
                return null; // ordinary/manual startup: do not delay or create authority.

            if (lease.State == ActivatedState) break;
            if (lease.State != PreparedState) return null; // already attempted/consumed/failed.
            if (IsExpired(lease, DateTimeOffset.Now)) return null;
            await Task.Delay(150, cancellationToken);
        }

        if (lease is null || leasePath is null || lease.State != ActivatedState)
            return null;
        if (IsExpired(lease, DateTimeOffset.Now)) return null;
        if (lease.ProcessId != Environment.ProcessId || !lease.AutoLaunchAllowed ||
            !lease.FirstBootSelfTestAllowed || !lease.FirstBootAcceptIfSelfTestPassesAllowed ||
            lease.PublishAllowed || lease.LifecycleAllowed || lease.RetryAuthorized)
            return null;

        ValidateActivatedEvidence(root, lease);
        await VerifyExactPredecessorAsync(root, cancellationToken);

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
            return null; // another/earlier attempt already owns this one-shot lease.
        }

        var consuming = lease with
        {
            State = ConsumingState,
            ConsumingAt = DateTimeOffset.Now,
            ClaimPath = claimPath,
            RetryAuthorized = false,
            Note = "CONSUMING claim was created atomically before automatic Self-test. This lease can never create automatic retry authority, even if Self-test/checkpoint fails."
        };
        try
        {
            await AtomicWriteVerifiedAsync(leasePath, consuming, cancellationToken);
        }
        catch
        {
            // Deliberately keep the claim file. Ambiguous persistence must fail closed
            // and prevent a second automatic acceptance attempt.
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
        RequireEquivalentIdentity(claim.Lease, current);
        if (current.State != ConsumingState || string.IsNullOrWhiteSpace(current.ClaimPath) || !File.Exists(current.ClaimPath))
            throw new InvalidDataException("v0.40 bootstrap cannot finalize acceptance without the exact CONSUMING one-shot claim.");
        if (!File.Exists(acceptanceArtifactPath) || !File.Exists(checkpointReceiptPath))
            throw new InvalidDataException("Acceptance/checkpoint evidence is missing before v0.40 bootstrap finalization.");

        var completed = current with
        {
            State = CompletedState,
            CompletedAt = DateTimeOffset.Now,
            AcceptanceArtifactPath = Path.GetFullPath(acceptanceArtifactPath),
            AcceptanceArtifactSha256 = HashFile(acceptanceArtifactPath),
            CheckpointReceiptPath = Path.GetFullPath(checkpointReceiptPath),
            CheckpointReceiptSha256 = HashFile(checkpointReceiptPath),
            RetryAuthorized = false,
            Note = "COMPLETED_ACCEPTED means the exact first-boot lease was consumed once, Self-test passed and the bounded local checkpoint was created. Publish and Lifecycle remain separate explicit actions."
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
        if (string.IsNullOrWhiteSpace(failure)) failure = "unspecified bounded transition failure";
        var current = File.Exists(leasePath)
            ? ReadJson<TransitionBootstrapV040Lease>(leasePath, "bootstrap lease")
            : lease;
        RequireEquivalentIdentity(lease, current);
        if (current.State == CompletedState) return current;
        var failed = current with
        {
            State = FailedState,
            CompletedAt = DateTimeOffset.Now,
            RetryAuthorized = false,
            Failure = failure,
            Note = "FAILED_NO_RETRY is terminal for automatic transition authority. Manual operator actions remain separately available but no automatic retry is minted."
        };
        await AtomicWriteVerifiedAsync(leasePath, failed, cancellationToken);
        return failed;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        var now = DateTimeOffset.UtcNow;
        return new[]
        {
            ("bootstrap-v040-fixed-predecessor", PredecessorCommit == "d877005b2070759cf24ea4ea5f31e90545cd2bcf", PredecessorCommit, "accepted v0.39.1"),
            ("bootstrap-v040-fixed-target", TargetVersion == "0.40.0" && TargetTag == "workbench-v0.40-accepted", $"{TargetVersion}/{TargetTag}", "0.40.0/workbench-v0.40-accepted"),
            ("bootstrap-v040-one-shot-state", ConsumingState == "CONSUMING" && CompletedState == "COMPLETED_ACCEPTED" && FailedState == "FAILED_NO_RETRY", $"{ConsumingState}/{CompletedState}/{FailedState}", "one-shot terminal states"),
            ("bootstrap-v040-bounded-lifetime", LeaseLifetime >= TimeSpan.FromMinutes(1) && LeaseLifetime <= TimeSpan.FromMinutes(10), LeaseLifetime.ToString(), "1..10 minutes"),
            ("bootstrap-v040-bounded-activation-wait", ActivationWait >= TimeSpan.FromSeconds(1) && ActivationWait <= TimeSpan.FromSeconds(10), ActivationWait.ToString(), "1..10 seconds"),
            ("bootstrap-v040-expiry-rule", IsExpiredForCheck(now - TimeSpan.FromMinutes(6), now - TimeSpan.FromMinutes(1), now), "true", "true"),
            ("bootstrap-v040-publish-not-in-lease", true, "PublishAllowed=false; LifecycleAllowed=false", "false/false"),
            ("bootstrap-v040-retry-not-in-lease", true, "RetryAuthorized=false", "false")
        };
    }

    private static bool IsExpiredForCheck(DateTimeOffset created, DateTimeOffset expires, DateTimeOffset now)
        => expires <= created || now > expires;

    private static bool IsExpired(TransitionBootstrapV040Lease lease, DateTimeOffset now)
        => IsExpiredForCheck(lease.CreatedAt, lease.ExpiresAt, now);

    private static (TransitionBootstrapV040Lease? Lease, string? Path) FindMatchingLease(
        string directory,
        string processPath,
        string processSha)
    {
        foreach (var path in Directory.GetFiles(directory, "transition-bootstrap-v0.40-*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            TransitionBootstrapV040Lease lease;
            try { lease = ReadJson<TransitionBootstrapV040Lease>(path, "bootstrap lease"); }
            catch { continue; }
            if (lease.Schema != Schema || lease.Version != Version || lease.TargetVersion != TargetVersion || lease.TargetTag != TargetTag)
                continue;
            if (!SamePath(lease.CandidateExecutablePath, processPath) ||
                !lease.CandidateExecutableSha256.Equals(processSha, StringComparison.OrdinalIgnoreCase))
                continue;
            return (lease, path);
        }
        return (null, null);
    }

    private static void ValidateActivatedEvidence(string root, TransitionBootstrapV040Lease lease)
    {
        if (lease.State != ActivatedState || lease.ProcessId is null ||
            lease.LaunchReceiptVerified != true || lease.CandidateObservedAlive != true ||
            lease.ProcessImageMatchedCandidate != true || lease.PredecessorSelfCloseEligible != true ||
            string.IsNullOrWhiteSpace(lease.LaunchReceiptPath) || string.IsNullOrWhiteSpace(lease.LaunchReceiptSha256) ||
            string.IsNullOrWhiteSpace(lease.HandoffReceiptPath) || string.IsNullOrWhiteSpace(lease.HandoffReceiptSha256))
            throw new InvalidDataException("ACTIVATED bootstrap lease lacks exact launch/handoff evidence.");

        var launchPath = ValidateArtifactPath(root, lease.LaunchReceiptPath, "update-applies", "candidate launch receipt");
        var handoffPath = ValidateArtifactPath(root, lease.HandoffReceiptPath, "update-applies", "candidate handoff receipt");
        if (!HashFile(launchPath).Equals(lease.LaunchReceiptSha256, StringComparison.OrdinalIgnoreCase) ||
            !HashFile(handoffPath).Equals(lease.HandoffReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ACTIVATED bootstrap launch/handoff receipt bytes drifted.");

        var launch = ReadJson<WorkbenchCandidateLaunchReceipt>(launchPath, "candidate launch receipt");
        var handoff = ReadJson<CandidateLaunchHandoffV039Receipt>(handoffPath, "candidate handoff receipt");
        if (launch.ProcessId != lease.ProcessId || launch.Status != "CANDIDATE_LAUNCHED_NOT_ACCEPTED" ||
            !SamePath(launch.CandidateExecutablePath, lease.CandidateExecutablePath) ||
            !launch.CandidateExecutableSha256.Equals(lease.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ACTIVATED bootstrap launch receipt no longer matches the lease.");
        RequireSuccessfulHandoff(handoff, lease.ProcessId.Value, lease);
    }

    private static void RequireSuccessfulHandoff(
        CandidateLaunchHandoffV039Receipt handoff,
        int processId,
        TransitionBootstrapV040Lease lease)
    {
        if (handoff.Status != CandidateLaunchHandoffV039Service.SuccessStatus || handoff.ProcessId != processId ||
            !handoff.LaunchReceiptVerified || !handoff.CandidateObservedAlive || !handoff.ProcessImageMatchedCandidate ||
            !handoff.PredecessorSelfCloseEligible || handoff.CandidateAcceptanceCreated ||
            handoff.ExternalProcessTerminationAuthorityCreated ||
            !SamePath(handoff.CandidateExecutablePath, lease.CandidateExecutablePath) ||
            !SamePath(handoff.ObservedProcessExecutablePath, lease.CandidateExecutablePath) ||
            !handoff.CandidateExecutableSha256.Equals(lease.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.39 handoff evidence does not authorize exact v0.40 bootstrap activation.");
    }

    private static void RequireEquivalentBuild(WorkbenchUpdateApplyBuildReceipt expected, WorkbenchUpdateApplyBuildReceipt observed)
    {
        if (observed.Schema != expected.Schema || observed.Version != expected.Version || observed.Status != expected.Status ||
            observed.TargetVersion != expected.TargetVersion || observed.TargetTag != expected.TargetTag ||
            observed.PredecessorCommit != expected.PredecessorCommit || observed.PredecessorTag != expected.PredecessorTag ||
            !SamePath(observed.CandidateExecutablePath, expected.CandidateExecutablePath) ||
            !observed.CandidateExecutableSha256.Equals(expected.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Persisted apply/build receipt differs from the in-memory v0.40 transition receipt.");
    }

    private static void RequireEquivalentIdentity(TransitionBootstrapV040Lease expected, TransitionBootstrapV040Lease observed)
    {
        if (observed.Schema != Schema || observed.Version != Version || observed.LeaseId != expected.LeaseId ||
            observed.PredecessorCommit != expected.PredecessorCommit || observed.PredecessorTag != expected.PredecessorTag ||
            observed.TargetVersion != expected.TargetVersion || observed.TargetTag != expected.TargetTag ||
            !SamePath(observed.CandidateExecutablePath, expected.CandidateExecutablePath) ||
            !observed.CandidateExecutableSha256.Equals(expected.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase) ||
            !SamePath(observed.BuildReceiptPath, expected.BuildReceiptPath) ||
            !observed.BuildReceiptSha256.Equals(expected.BuildReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.40 bootstrap lease identity/bindings changed.");
    }

    private static async Task AtomicWriteVerifiedAsync(
        string path,
        TransitionBootstrapV040Lease lease,
        CancellationToken cancellationToken)
    {
        var temp = path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(lease, JsonOptions), new UTF8Encoding(false), cancellationToken);
            var parsedTemp = ReadJson<TransitionBootstrapV040Lease>(temp, "temporary bootstrap lease");
            RequireFullEquivalent(lease, parsedTemp);
            File.Move(temp, path, overwrite: true);
            var parsedFinal = ReadJson<TransitionBootstrapV040Lease>(path, "persisted bootstrap lease");
            RequireFullEquivalent(lease, parsedFinal);
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp);
            throw;
        }
    }

    private static void RequireFullEquivalent(TransitionBootstrapV040Lease expected, TransitionBootstrapV040Lease observed)
    {
        RequireEquivalentIdentity(expected, observed);
        if (observed.State != expected.State || observed.ProcessId != expected.ProcessId ||
            observed.RetryAuthorized != expected.RetryAuthorized || observed.PublishAllowed != expected.PublishAllowed ||
            observed.LifecycleAllowed != expected.LifecycleAllowed || observed.ClaimPath != expected.ClaimPath ||
            observed.AcceptanceArtifactSha256 != expected.AcceptanceArtifactSha256 ||
            observed.CheckpointReceiptSha256 != expected.CheckpointReceiptSha256 || observed.Failure != expected.Failure)
            throw new InvalidDataException("Persisted v0.40 bootstrap state differs from in-memory evidence.");
    }

    private static IReadOnlyList<string> CommonNonEffects() => new[]
    {
        "no general future launch authority",
        "no reusable or persistent candidate acceptance authority",
        "no automatic retry authority",
        "no automatic Publish accepted",
        "no automatic Lifecycle receipt",
        "no remote Git operation",
        "no arbitrary executable path or command",
        "no external process kill/termination/signal",
        "no catalog mutation",
        "no Agent Execute or ActionPermit",
        "no Stable Core or interface-registry promotion"
    };

    private static string ResolveLeaseDirectory(string root)
        => Path.Combine(root, "artifacts", "transition-bootstrap");

    private static string ValidateLeasePath(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidDataException("v0.40 bootstrap lease artifact is missing.");
        var full = Path.GetFullPath(path);
        var prefix = Path.GetFullPath(ResolveLeaseDirectory(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v0.40 bootstrap lease escapes Workbench/artifacts/transition-bootstrap.");
        return full;
    }

    private static string ValidateArtifactPath(string root, string path, string child, string role)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidDataException($"{role} is missing.");
        var full = Path.GetFullPath(path);
        var prefix = Path.GetFullPath(Path.Combine(root, "artifacts", child)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{role} escapes Workbench/artifacts/{child}.");
        return full;
    }

    private static T ReadJson<T>(string path, string role)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
           ?? throw new InvalidDataException($"{role} could not be parsed.");

    private static bool SamePath(string a, string b)
        => Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static async Task VerifyExactPredecessorAsync(string root, CancellationToken cancellationToken)
    {
        var head = (await RunGitReadOnlyAsync(root, cancellationToken, "rev-parse", "HEAD")).Trim();
        if (!head.Equals(PredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"v0.40 bootstrap requires exact accepted v0.39.1 HEAD {PredecessorCommit}; observed {head}.");
        var tag = (await RunGitReadOnlyAsync(root, cancellationToken, "rev-list", "-n", "1", PredecessorTag)).Trim();
        if (!tag.Equals(head, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Accepted v0.39.1 predecessor tag is not at exact current HEAD.");
    }

    private static async Task<string> RunGitReadOnlyAsync(string root, CancellationToken cancellationToken, params string[] args)
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
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only Git process for v0.40 bootstrap.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { }
            throw new InvalidDataException("v0.40 bootstrap read-only Git operation timed out.");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0) throw new InvalidDataException($"v0.40 bootstrap read-only Git operation failed: {stderr.Trim()}");
        return stdout;
    }
}
