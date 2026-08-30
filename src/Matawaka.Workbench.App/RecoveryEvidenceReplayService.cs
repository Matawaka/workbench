using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RecoveryEvidenceReplayItem(
    string Role,
    string RelativePath,
    string Sha256,
    string Schema,
    string Version,
    bool Verified);

public sealed record RecoveryEvidenceReplayReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    bool Replayed,
    string Status,
    string MainRepositoryRoot,
    string CurrentHead,
    IReadOnlyList<string> CurrentTags,
    bool WorkingTreeClean,
    string SourceClosureArtifactPath,
    string SourceClosureArtifactSha256,
    string SourceClosureSchema,
    string SourceClosureVersion,
    string SourceEvidenceEnvelopeDigest,
    string ReplayCapsuleRoot,
    IReadOnlyList<RecoveryEvidenceReplayItem> PortableEvidence,
    string ReplayedEvidenceEnvelopeDigest,
    bool ClosureDigestReproduced,
    bool PortableCopiesVerified,
    bool ReplayUsedOnlyPortableCopies,
    bool HistoricalAbsolutePathsDereferencedDuringReplay,
    bool OriginalFixtureRootsRequiredForReplay,
    bool OriginalEvidenceArtifactsRequiredAfterCapsuleCreation,
    bool PositiveRecoveryDrillReplayed,
    bool RecoveryCapabilityAdmissionReplayed,
    bool NegativeControlMatrixReplayed,
    bool AdmissionToDrillBindingReplayed,
    bool NegativeRefusalSemanticsReplayed,
    bool BoundedRecoveryCapabilityPreserved,
    bool CrossMachinePortabilityProven,
    bool ProductionMainRepositoryRecoveryProven,
    bool GeneralFailureRecoveryClaimAllowed,
    bool AutomaticRecoveryAuthorized,
    bool RecoveryExecutionAuthorized,
    bool RollbackAuthorized,
    bool DeletionAuthorized,
    bool SourceMutationAuthorized,
    bool BuildAuthorized,
    bool CheckpointAuthorized,
    bool NetworkAccessAuthorized,
    bool CatalogMutationAuthorized,
    bool AgentExecuteAuthorized,
    bool StableCorePromotionAuthorized,
    IReadOnlyList<string> ReplayScope,
    IReadOnlyList<string> ReplayLimitations,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Creates a retained local replay capsule from the already-closed v0.22
/// recovery evidence envelope, then replays the envelope from the copied JSON
/// bytes only. Historical absolute paths embedded inside the receipts are
/// treated as evidence fields and are not dereferenced during replay. This is
/// an evidence portability/replay check, not live recovery authority.
/// </summary>
public sealed class RecoveryEvidenceReplayService
{
    public const string Version = "0.23.0";
    public const string ReceiptSchema = "matawaka.workbench-recovery-evidence-replay/v0.23";
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<(RecoveryEvidenceReplayReceipt Receipt, string ArtifactPath)> ReplayAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var currentHead = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var currentTags = SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD").ConfigureAwait(false));
        var status = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all").ConfigureAwait(false);
        var dirtyPaths = ParseStatusPaths(status);

        if (dirtyPaths.Count != 0)
            throw new InvalidDataException("Recovery evidence replay requires a clean accepted main Workbench repository.");
        if (!currentTags.Contains("workbench-v0.23-accepted", StringComparer.Ordinal))
            throw new InvalidDataException("Recovery evidence replay is enabled only after workbench-v0.23-accepted points at the current HEAD.");

        var closurePath = FindLatestClosureArtifact(repositoryRoot);
        var closureBytes = await File.ReadAllBytesAsync(closurePath, cancellationToken).ConfigureAwait(false);
        var closureSha = HashBytes(closureBytes);
        var sourceClosure = DeserializeBytes<RecoveryEvidenceClosureReceipt>(closureBytes, "v0.22 recovery evidence closure");
        if (!sourceClosure.Closed || !string.Equals(sourceClosure.Status, "CLOSED_BOUNDED_RECOVERY_EVIDENCE_ENVELOPE", StringComparison.Ordinal))
            throw new InvalidDataException("The retained v0.22 recovery evidence closure is not closed.");

        var expectedRoles = new[] { "positive-isolated-drill", "bounded-capability-admission", "negative-control-matrix" };
        var evidenceByRole = sourceClosure.Evidence.ToDictionary(x => x.Role, StringComparer.Ordinal);
        if (evidenceByRole.Count != expectedRoles.Length || expectedRoles.Any(role => !evidenceByRole.ContainsKey(role)))
            throw new InvalidDataException("The retained v0.22 closure does not contain the exact three replay evidence roles.");

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var replayRoot = Path.Combine(repositoryRoot, "artifacts", "recovery-replays", $"replay-capsule-v0.23-{timestamp}");
        var replayEvidenceRoot = Path.Combine(replayRoot, "evidence");
        Directory.CreateDirectory(replayEvidenceRoot);

        var closureCopyPath = Path.Combine(replayRoot, "source-closure.json");
        await File.WriteAllBytesAsync(closureCopyPath, closureBytes, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(HashFile(closureCopyPath), closureSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Copied recovery closure bytes do not match the retained source closure.");

        var roleToName = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["positive-isolated-drill"] = "positive-isolated-drill.json",
            ["bounded-capability-admission"] = "bounded-capability-admission.json",
            ["negative-control-matrix"] = "negative-control-matrix.json"
        };
        var roleToRoot = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["positive-isolated-drill"] = Path.Combine("artifacts", "recovery-drills"),
            ["bounded-capability-admission"] = Path.Combine("artifacts", "recovery-admissions"),
            ["negative-control-matrix"] = Path.Combine("artifacts", "recovery-negative-controls")
        };

        foreach (var role in expectedRoles)
        {
            var item = evidenceByRole[role];
            var sourcePath = ValidateEvidencePath(repositoryRoot, item.ArtifactPath, roleToRoot[role], role);
            var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(HashBytes(bytes), item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Retained replay evidence SHA-256 mismatch for role {role}.");
            var copyPath = Path.Combine(replayEvidenceRoot, roleToName[role]);
            await File.WriteAllBytesAsync(copyPath, bytes, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(HashFile(copyPath), item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Portable replay copy SHA-256 mismatch for role {role}.");
        }

        // Replay begins here. From this point forward only capsule-local copies are read.
        var replayClosure = DeserializeFile<RecoveryEvidenceClosureReceipt>(closureCopyPath, "portable copied recovery closure");
        var drillCopyPath = Path.Combine(replayEvidenceRoot, roleToName["positive-isolated-drill"]);
        var admissionCopyPath = Path.Combine(replayEvidenceRoot, roleToName["bounded-capability-admission"]);
        var matrixCopyPath = Path.Combine(replayEvidenceRoot, roleToName["negative-control-matrix"]);
        var drill = DeserializeFile<IsolatedRecoveryDrillReceipt>(drillCopyPath, "portable copied positive recovery drill");
        var admission = DeserializeFile<RecoveryCapabilityAdmissionReceipt>(admissionCopyPath, "portable copied capability admission");
        var matrix = DeserializeFile<RecoveryNegativeControlMatrixReceipt>(matrixCopyPath, "portable copied negative-control matrix");

        var portableEvidence = new[]
        {
            CreateReplayItem("positive-isolated-drill", Path.Combine("evidence", roleToName["positive-isolated-drill"]), drillCopyPath, drill.Schema, drill.Version, evidenceByRole["positive-isolated-drill"]),
            CreateReplayItem("bounded-capability-admission", Path.Combine("evidence", roleToName["bounded-capability-admission"]), admissionCopyPath, admission.Schema, admission.Version, evidenceByRole["bounded-capability-admission"]),
            CreateReplayItem("negative-control-matrix", Path.Combine("evidence", roleToName["negative-control-matrix"]), matrixCopyPath, matrix.Schema, matrix.Version, evidenceByRole["negative-control-matrix"])
        };

        var replayDigest = HashEnvelope(portableEvidence);
        var closureDigestReproduced =
            string.Equals(replayClosure.Schema, "matawaka.workbench-recovery-evidence-closure/v0.22", StringComparison.Ordinal) &&
            string.Equals(replayClosure.Version, "0.22.0", StringComparison.Ordinal) &&
            replayClosure.Closed &&
            string.Equals(replayClosure.Status, "CLOSED_BOUNDED_RECOVERY_EVIDENCE_ENVELOPE", StringComparison.Ordinal) &&
            string.Equals(replayClosure.EvidenceEnvelopeDigest, replayDigest, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(sourceClosure.EvidenceEnvelopeDigest, replayDigest, StringComparison.OrdinalIgnoreCase);

        var positiveReplay =
            string.Equals(drill.Schema, "matawaka.workbench-isolated-recovery-drill/v0.19", StringComparison.Ordinal) &&
            string.Equals(drill.Version, "0.19.0", StringComparison.Ordinal) &&
            drill.Passed && drill.MainRepositoryUnchanged &&
            string.Equals(drill.MainHeadBefore, drill.MainHeadAfter, StringComparison.OrdinalIgnoreCase) &&
            drill.MainDirtyPathsBefore.Count == 0 && drill.MainDirtyPathsAfter.Count == 0 &&
            drill.CandidateDirtyPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "fixture/new.txt", "fixture/tracked.txt" }, StringComparer.Ordinal) &&
            string.Equals(drill.PreRecoveryClassification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) &&
            string.Equals(drill.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) &&
            string.Equals(drill.RecoveryExecutionStatus, "RECOVERED_TO_CURRENT_ACCEPTED_HEAD_FRESH_ASSESSMENT_REQUIRED", StringComparison.Ordinal) &&
            string.Equals(drill.PostRecoveryClassification, "CLEAN_ACCEPTED", StringComparison.Ordinal) &&
            drill.PostRecoveryWorkingTreeClean && drill.TrackedFileRestored && drill.UntrackedAdditionRemoved &&
            drill.FixtureHeadUnchanged && drill.FixtureTagsUnchanged &&
            drill.Authority.ExplicitUiConfirmationRequired && !drill.Authority.MainRepositoryMutationAllowed &&
            !drill.Authority.BuildAllowed && !drill.Authority.CheckpointAllowed && !drill.Authority.NetworkAccessAllowed &&
            !drill.Authority.CatalogMutationAllowed && !drill.Authority.AgentExecuteAllowed;

        var admissionBindingReplay =
            string.Equals(admission.EvidenceArtifactSha256, portableEvidence[0].Sha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(admission.EvidenceSchema, drill.Schema, StringComparison.Ordinal) &&
            string.Equals(admission.EvidenceVersion, drill.Version, StringComparison.Ordinal) &&
            string.Equals(admission.EvidenceMainHead, drill.MainHeadAfter, StringComparison.OrdinalIgnoreCase) &&
            admission.EvidenceMainTags.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(drill.MainTagsAfter.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);

        var admissionReplay =
            string.Equals(admission.Schema, "matawaka.workbench-recovery-capability-admission/v0.20", StringComparison.Ordinal) &&
            string.Equals(admission.Version, "0.20.0", StringComparison.Ordinal) && admission.Admitted &&
            string.Equals(admission.Status, "ADMITTED_ISOLATED_BOUNDED_RECOVERY_CAPABILITY", StringComparison.Ordinal) &&
            admission.BoundedRecoveryCapabilityAdmitted && admissionBindingReplay &&
            !admission.ProductionMainRepositoryRecoveryProven && !admission.GeneralFailureRecoveryClaimAllowed &&
            !admission.AutomaticRecoveryAuthorized && !admission.RecoveryExecutionAuthorized &&
            !admission.RollbackAuthorized && !admission.DeletionAuthorized && !admission.SourceMutationAuthorized &&
            !admission.BuildAuthorized && !admission.CheckpointAuthorized && !admission.NetworkAccessAuthorized &&
            !admission.CatalogMutationAuthorized && !admission.AgentExecuteAuthorized && !admission.StableCorePromotionAuthorized;

        var matrixScenariosReplay = matrix.Scenarios.Count == 3 && matrix.Scenarios.All(x =>
            x.Passed && x.ExecutionAttempted && x.ExecutionRejected &&
            !x.RecoveryAuthorityArtifactCreated && !x.RecoveryExecutionArtifactCreated &&
            x.CandidateStatePreservedAfterRefusal && x.FixtureHeadUnchanged && x.FixtureTagsUnchanged);
        var matrixReplay =
            string.Equals(matrix.Schema, "matawaka.workbench-recovery-negative-control-matrix/v0.21", StringComparison.Ordinal) &&
            string.Equals(matrix.Version, "0.21.0", StringComparison.Ordinal) && matrix.Passed &&
            matrix.MainRepositoryUnchanged && matrix.MainDirtyPathsBefore.Count == 0 && matrix.MainDirtyPathsAfter.Count == 0 &&
            matrix.UnknownDirtyRefused && matrix.ByteDriftAfterPlanRefused && matrix.PathSetDriftAfterPlanRefused &&
            matrix.AllRecoveryAttemptsRefusedBeforeAuthority && matrixScenariosReplay &&
            !matrix.Authority.MainRepositoryMutationAllowed && !matrix.Authority.ExpectedRecoveryMutationAllowed &&
            !matrix.Authority.BuildAllowed && !matrix.Authority.CheckpointAllowed && !matrix.Authority.NetworkAccessAllowed &&
            !matrix.Authority.CatalogMutationAllowed && !matrix.Authority.AgentExecuteAllowed;

        var negativeRefusalsReplay = matrixReplay &&
            matrix.Scenarios.Any(x => string.Equals(x.Id, "unknown-dirty-refused", StringComparison.Ordinal) && string.Equals(x.AssessmentClassification, "UNKNOWN_DIRTY_WORKTREE", StringComparison.Ordinal)) &&
            matrix.Scenarios.Any(x => string.Equals(x.Id, "candidate-byte-drift-after-plan-refused", StringComparison.Ordinal) && string.Equals(x.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal)) &&
            matrix.Scenarios.Any(x => string.Equals(x.Id, "dirty-path-set-drift-after-plan-refused", StringComparison.Ordinal) && string.Equals(x.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal));

        var closureScopePreserved =
            replayClosure.PositiveRecoveryDrillVerified && replayClosure.RecoveryCapabilityAdmissionVerified &&
            replayClosure.NegativeControlMatrixVerified && replayClosure.AdmissionToDrillBindingVerified &&
            replayClosure.CrossEvidenceBindingsVerified && replayClosure.BoundedRecoveryCapabilityPreserved &&
            !replayClosure.ProductionMainRepositoryRecoveryProven && !replayClosure.GeneralFailureRecoveryClaimAllowed &&
            !replayClosure.AutomaticRecoveryAuthorized && !replayClosure.RecoveryExecutionAuthorized &&
            !replayClosure.RollbackAuthorized && !replayClosure.DeletionAuthorized && !replayClosure.SourceMutationAuthorized &&
            !replayClosure.BuildAuthorized && !replayClosure.CheckpointAuthorized && !replayClosure.NetworkAccessAuthorized &&
            !replayClosure.CatalogMutationAuthorized && !replayClosure.AgentExecuteAuthorized && !replayClosure.StableCorePromotionAuthorized;

        var portableCopiesVerified = portableEvidence.All(x => x.Verified);
        var replayed = closureDigestReproduced && portableCopiesVerified && positiveReplay && admissionReplay && matrixReplay && negativeRefusalsReplay && closureScopePreserved;

        var replayScope = new[]
        {
            "copy exact retained v0.22 closure plus its three SHA-bound evidence receipts into one local replay capsule",
            "recompute the v0.22 EvidenceEnvelopeDigest from capsule-local role/SHA/schema/version bindings",
            "replay positive drill semantics from copied receipt fields without opening the historical fixture repository",
            "replay admission-to-drill binding from copied receipt hashes/schema/version/head/tag fields",
            "replay v0.21 refusal semantics from copied matrix fields without opening negative-control fixtures",
            "preserve all v0.22 authority limitations during replay"
        };
        var limitations = new[]
        {
            "replay proves independence from historical fixture-directory availability after capsule creation, not cross-machine portability",
            "source retained evidence artifacts must exist and match their v0.22 SHA-256 bindings when the capsule is first created",
            "absolute paths retained inside historical JSON are treated as informational fields and are not dereferenced during replay",
            "replay does not cryptographically authenticate the original producer beyond retained byte hashes and cross-evidence bindings",
            "replay does not prove arbitrary future schema compatibility, cross-OS serialization equivalence, or canonical UU-AAP conformance",
            "replay does not promote recovery interfaces to Stable Core or the interface registry"
        };
        var nonEffects = new[]
        {
            "no source file mutation",
            "no source restore or rollback",
            "no deletion of original evidence or fixture directories",
            "no dotnet restore/build/test/publish",
            "no git add/commit/tag",
            "no git fetch or push",
            "no remote creation/update",
            "no network access",
            "no Matawaka catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no automatic recovery authority",
            "no general recovery claim",
            "no production-main-repository recovery proof",
            "no Stable Core or interface-registry promotion",
            "writes are limited to copied evidence plus replay receipt under Workbench/artifacts/recovery-replays"
        };

        var receipt = new RecoveryEvidenceReplayReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            replayed,
            replayed ? "REPLAYED_PORTABLE_BOUNDED_RECOVERY_EVIDENCE_ENVELOPE" : "REPLAY_FAILED_EVIDENCE_BINDING_INCOMPLETE",
            repositoryRoot,
            currentHead,
            currentTags,
            true,
            closurePath,
            closureSha,
            sourceClosure.Schema,
            sourceClosure.Version,
            sourceClosure.EvidenceEnvelopeDigest,
            replayRoot,
            portableEvidence,
            replayDigest,
            closureDigestReproduced,
            portableCopiesVerified,
            true,
            false,
            false,
            false,
            positiveReplay,
            admissionReplay,
            matrixReplay,
            admissionBindingReplay,
            negativeRefusalsReplay,
            admission.BoundedRecoveryCapabilityAdmitted && closureScopePreserved && replayed,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            replayScope,
            limitations,
            nonEffects,
            "v0.23 replays the closed v0.22 bounded recovery evidence envelope from capsule-local copied receipts after their source bytes are SHA-verified. Historical fixture paths are not dereferenced during replay. Replay is retained evidence processing only: it is not live recovery authority, production-main recovery proof, a general recovery claim, automatic recovery authority, canonical UU-AAP conformance, or Stable Core promotion.");

        var artifactPath = Path.Combine(replayRoot, "replay-receipt.json");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(receipt, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return (receipt, artifactPath);
    }

    private static RecoveryEvidenceReplayItem CreateReplayItem(
        string role,
        string relativePath,
        string copyPath,
        string schema,
        string version,
        RecoveryEvidenceClosureItem source)
    {
        var sha = HashFile(copyPath);
        var verified = source.Verified &&
                       string.Equals(sha, source.Sha256, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(schema, source.Schema, StringComparison.Ordinal) &&
                       string.Equals(version, source.Version, StringComparison.Ordinal);
        return new RecoveryEvidenceReplayItem(role, relativePath.Replace('\\', '/'), sha, schema, version, verified);
    }

    private static string FindLatestClosureArtifact(string repositoryRoot)
    {
        var root = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "recovery-closures"));
        if (!Directory.Exists(root)) throw new InvalidDataException("No retained v0.22 recovery evidence closure is available for replay.");
        var rootPrefix = root + Path.DirectorySeparatorChar;
        foreach (var file in Directory.GetFiles(root, "recovery-evidence-closure-v0.22-*.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var full = Path.GetFullPath(file);
                if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var parsed = JsonSerializer.Deserialize<RecoveryEvidenceClosureReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions);
                if (parsed is not null && parsed.Closed && string.Equals(parsed.Status, "CLOSED_BOUNDED_RECOVERY_EVIDENCE_ENVELOPE", StringComparison.Ordinal))
                    return full;
            }
            catch
            {
                // Unreadable retained evidence cannot support replay; continue to older closure.
            }
        }
        throw new InvalidDataException("No closed retained v0.22 recovery evidence closure is available for replay.");
    }

    private static string ValidateEvidencePath(string repositoryRoot, string candidate, string relativeRoot, string label)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
            throw new InvalidDataException($"Retained {label} source evidence is missing while creating the replay capsule.");
        var root = Path.GetFullPath(Path.Combine(repositoryRoot, relativeRoot));
        var full = Path.GetFullPath(candidate);
        var rootPrefix = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Retained {label} source evidence escapes its allowed artifact root.");
        return full;
    }

    private static T DeserializeFile<T>(string path, string label)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
           ?? throw new InvalidDataException($"{label} could not be parsed.");

    private static T DeserializeBytes<T>(byte[] bytes, string label)
        => JsonSerializer.Deserialize<T>(bytes, JsonOptions)
           ?? throw new InvalidDataException($"{label} could not be parsed.");

    private static string HashEnvelope(IEnumerable<RecoveryEvidenceReplayItem> evidence)
    {
        var canonical = string.Join("\n", evidence.OrderBy(x => x.Role, StringComparer.Ordinal)
            .Select(x => $"{x.Role}|{x.Sha256}|{x.Schema}|{x.Version}")) + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository not found: {root}");
        return root;
    }

    private static IReadOnlyList<string> ParseStatusPaths(string output)
    {
        var paths = new List<string>();
        foreach (var raw in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4) throw new InvalidDataException($"Unexpected git status porcelain line in recovery evidence replay: {raw}");
            var path = raw[3..];
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) path = path[(arrow + 4)..];
            paths.Add(path.Trim('"').Replace('\\', '/').TrimStart('/'));
        }
        return paths.OrderBy(x => x, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> SplitLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static async Task<string> RunGitReadOnlyAsync(string repositoryRoot, CancellationToken cancellationToken, params string[] args)
    {
        if (args.Length == 0) throw new InvalidDataException("Git command is required.");
        if (!new[] { "rev-parse", "tag", "status" }.Contains(args[0], StringComparer.Ordinal))
            throw new InvalidDataException($"Non-allowlisted read-only Git operation in recovery evidence replay: {args[0]}");

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["PAGER"] = "cat";
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only Git recovery-replay process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Read-only Git recovery-replay operation timed out after {GitTimeout.TotalSeconds:0} seconds.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Read-only Git recovery-replay operation failed: {string.Join(' ', args)} :: {stderr.Trim()}");
        return stdout;
    }

    private static string HashFile(string path) => HashBytes(File.ReadAllBytes(path));
    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
