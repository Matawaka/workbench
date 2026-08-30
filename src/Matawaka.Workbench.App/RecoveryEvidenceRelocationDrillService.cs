using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RecoveryRelocationFileReceipt(
    string RelativePath,
    string Sha256,
    long Bytes,
    bool Verified);

public sealed record RecoveryEvidenceRelocationDrillReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    bool Passed,
    string Status,
    string MainRepositoryRoot,
    string MainHeadBefore,
    IReadOnlyList<string> MainTagsBefore,
    IReadOnlyList<string> MainDirtyPathsBefore,
    string MainHeadAfter,
    IReadOnlyList<string> MainTagsAfter,
    IReadOnlyList<string> MainDirtyPathsAfter,
    bool MainRepositoryUnchanged,
    string SourceReplayCapsuleRoot,
    string SourceReplayReceiptSha256,
    string SourceClosureSha256,
    string SourceEvidenceEnvelopeDigest,
    string SourceCapsuleManifestDigest,
    string RelocationRoot,
    string RelocatedCapsuleRoot,
    IReadOnlyList<RecoveryRelocationFileReceipt> RelocatedFiles,
    string RelocatedCapsuleManifestDigest,
    string RelocatedEvidenceEnvelopeDigest,
    bool ExactSourceCapsuleFileSetVerified,
    bool RelocatedCopiesVerified,
    bool RelocationRootSeparatedFromSourceReplayRoot,
    bool CapsuleManifestDigestReproduced,
    bool RelocatedEvidenceEnvelopeDigestReproduced,
    bool SourceReplayStatusVerified,
    bool PositiveRecoveryDrillReplayed,
    bool RecoveryCapabilityAdmissionReplayed,
    bool NegativeControlMatrixReplayed,
    bool AdmissionToDrillBindingReplayed,
    bool NegativeRefusalSemanticsReplayed,
    bool ReplayAuthorityLimitationsPreserved,
    bool ReplayUsedOnlyRelocatedCopies,
    bool OriginalReplayCapsuleDereferencedDuringRelocatedReplay,
    bool OriginalEvidenceArtifactsDereferencedDuringRelocatedReplay,
    bool HistoricalFixtureRootsDereferencedDuringRelocatedReplay,
    bool LocalRootRelocationDemonstrated,
    bool CrossMachinePortabilityProven,
    bool CrossOsPortabilityProven,
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
    IReadOnlyList<string> Scope,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Post-acceptance local relocation drill over a retained v0.23 replay capsule.
/// The service copies the exact five JSON capsule files from artifacts/recovery-replays
/// into a disjoint .workbench relocation root, then replays only from the relocated
/// copies. Absolute paths retained inside historical receipts are treated only as
/// evidence fields and are never dereferenced during the relocated replay.
/// This proves a local-root relocation property, not cross-machine portability and
/// not live recovery authority.
/// </summary>
public sealed class RecoveryEvidenceRelocationDrillService
{
    public const string Version = "0.24.0";
    public const string ReceiptSchema = "matawaka.workbench-recovery-evidence-relocation-drill/v0.24";
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string[] ExpectedRelativeFiles =
    {
        "evidence/bounded-capability-admission.json",
        "evidence/negative-control-matrix.json",
        "evidence/positive-isolated-drill.json",
        "replay-receipt.json",
        "source-closure.json"
    };

    public async Task<(RecoveryEvidenceRelocationDrillReceipt Receipt, string ArtifactPath)> RunAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var before = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        if (before.DirtyPaths.Count != 0)
            throw new InvalidDataException("Recovery evidence relocation drill requires a clean accepted main Workbench repository.");
        if (!before.Tags.Contains("workbench-v0.24-accepted", StringComparer.Ordinal))
            throw new InvalidDataException("Recovery evidence relocation drill is enabled only after workbench-v0.24-accepted points at the current HEAD.");

        var sourceReplayRoot = FindLatestReplayCapsule(repositoryRoot);
        var sourceFiles = EnumerateRelativeFiles(sourceReplayRoot);
        var exactFileSet = sourceFiles.SequenceEqual(ExpectedRelativeFiles, StringComparer.Ordinal);
        if (!exactFileSet)
            throw new InvalidDataException($"The retained v0.23 replay capsule file set is not exact. Expected: {string.Join(", ", ExpectedRelativeFiles)}; actual: {string.Join(", ", sourceFiles)}");

        var sourceBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var sourceHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relative in ExpectedRelativeFiles)
        {
            var full = ResolveCapsuleFile(sourceReplayRoot, relative);
            var bytes = await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false);
            sourceBytes[relative] = bytes;
            sourceHashes[relative] = HashBytes(bytes);
        }

        var sourceReplay = DeserializeBytes<RecoveryEvidenceReplayReceipt>(sourceBytes["replay-receipt.json"], "source v0.23 replay receipt");
        var sourceClosure = DeserializeBytes<RecoveryEvidenceClosureReceipt>(sourceBytes["source-closure.json"], "source v0.22 closure copy");

        var sourceReplayVerified = VerifyReplayReceipt(sourceReplay, sourceClosure, sourceHashes);
        if (!sourceReplayVerified)
            throw new InvalidDataException("The retained v0.23 replay capsule does not satisfy its replay/binding contract.");

        var sourceManifestDigest = HashCapsuleManifest(sourceHashes);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var relocationRoot = Path.Combine(repositoryRoot, ".workbench", "recovery-replay-relocations", $"v0.24-{stamp}");
        var relocatedCapsuleRoot = Path.Combine(relocationRoot, "capsule");
        Directory.CreateDirectory(relocatedCapsuleRoot);

        var sourceFull = Path.GetFullPath(sourceReplayRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var relocatedFull = Path.GetFullPath(relocatedCapsuleRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var separated = !relocatedFull.StartsWith(sourceFull, StringComparison.OrdinalIgnoreCase) &&
                        !sourceFull.StartsWith(relocatedFull, StringComparison.OrdinalIgnoreCase);
        if (!separated)
            throw new InvalidDataException("Relocation root must be disjoint from the source replay capsule root.");

        foreach (var relative in ExpectedRelativeFiles)
        {
            var destination = ResolveCapsuleFile(relocatedCapsuleRoot, relative, requireExists: false);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, sourceBytes[relative], cancellationToken).ConfigureAwait(false);
        }

        var relocatedHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var relocatedFiles = new List<RecoveryRelocationFileReceipt>();
        foreach (var relative in ExpectedRelativeFiles)
        {
            var full = ResolveCapsuleFile(relocatedCapsuleRoot, relative);
            var info = new FileInfo(full);
            var sha = HashFile(full);
            relocatedHashes[relative] = sha;
            var verified = string.Equals(sha, sourceHashes[relative], StringComparison.OrdinalIgnoreCase) && info.Length == sourceBytes[relative].LongLength;
            relocatedFiles.Add(new RecoveryRelocationFileReceipt(relative, sha, info.Length, verified));
        }
        var relocatedCopiesVerified = relocatedFiles.All(x => x.Verified);
        if (!relocatedCopiesVerified)
            throw new InvalidDataException("One or more relocated capsule copies differ from the source replay capsule bytes.");

        // Relocated replay begins here. From this point forward only relocated paths are read.
        var relocatedReplayPath = ResolveCapsuleFile(relocatedCapsuleRoot, "replay-receipt.json");
        var relocatedClosurePath = ResolveCapsuleFile(relocatedCapsuleRoot, "source-closure.json");
        var relocatedDrillPath = ResolveCapsuleFile(relocatedCapsuleRoot, "evidence/positive-isolated-drill.json");
        var relocatedAdmissionPath = ResolveCapsuleFile(relocatedCapsuleRoot, "evidence/bounded-capability-admission.json");
        var relocatedMatrixPath = ResolveCapsuleFile(relocatedCapsuleRoot, "evidence/negative-control-matrix.json");

        var replay = DeserializeFile<RecoveryEvidenceReplayReceipt>(relocatedReplayPath, "relocated replay receipt");
        var closure = DeserializeFile<RecoveryEvidenceClosureReceipt>(relocatedClosurePath, "relocated source closure");
        var drill = DeserializeFile<IsolatedRecoveryDrillReceipt>(relocatedDrillPath, "relocated positive drill");
        var admission = DeserializeFile<RecoveryCapabilityAdmissionReceipt>(relocatedAdmissionPath, "relocated recovery admission");
        var matrix = DeserializeFile<RecoveryNegativeControlMatrixReceipt>(relocatedMatrixPath, "relocated negative matrix");

        var relocatedManifestDigest = HashCapsuleManifest(relocatedHashes);
        var capsuleManifestDigestReproduced = string.Equals(sourceManifestDigest, relocatedManifestDigest, StringComparison.OrdinalIgnoreCase);
        var evidenceDigest = HashEvidenceEnvelope(closure, relocatedHashes);
        var evidenceDigestReproduced =
            string.Equals(evidenceDigest, closure.EvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(evidenceDigest, replay.SourceEvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(evidenceDigest, replay.ReplayedEvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase);

        var positiveReplay = VerifyPositiveDrill(drill);
        var admissionBinding = VerifyAdmission(admission, drill, relocatedHashes["evidence/positive-isolated-drill.json"]);
        var matrixReplay = VerifyNegativeMatrix(matrix);
        var negativeRefusalReplay = VerifyNegativeRefusals(matrix);
        var replayLimitationsPreserved = VerifyAuthorityLimitations(replay, closure, admission);

        var after = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        var mainUnchanged = string.Equals(before.Head, after.Head, StringComparison.OrdinalIgnoreCase) &&
                            before.Tags.SequenceEqual(after.Tags, StringComparer.Ordinal) &&
                            before.DirtyPaths.SequenceEqual(after.DirtyPaths, StringComparer.Ordinal);

        var passed = exactFileSet && sourceReplayVerified && relocatedCopiesVerified && separated &&
                     capsuleManifestDigestReproduced && evidenceDigestReproduced && positiveReplay &&
                     admissionBinding && matrixReplay && negativeRefusalReplay && replayLimitationsPreserved && mainUnchanged;

        var scope = new[]
        {
            "locate one retained passing v0.23 replay capsule under Workbench/artifacts/recovery-replays",
            "require the exact five-file replay capsule JSON set and SHA-bind every file before relocation",
            "copy those exact bytes into a disjoint Workbench/.workbench/recovery-replay-relocations root",
            "recompute both capsule-manifest and v0.22 evidence-envelope digests from relocated copies",
            "replay positive drill, admission binding and negative-refusal semantics using relocated JSON only",
            "preserve the v0.22/v0.23 authority limitations and leave the main Workbench Git state unchanged"
        };
        var limitations = new[]
        {
            "the relocation drill runs on the same machine and filesystem family; it does not prove cross-machine or cross-OS portability",
            "the source v0.23 replay capsule must exist and match its own byte bindings before relocation begins",
            "absolute paths retained inside historical receipts remain informational fields and are not rewritten or dereferenced during relocated replay",
            "the drill does not authenticate the original evidence producer beyond retained SHA-256 and cross-evidence bindings",
            "the drill does not prove arbitrary future schema compatibility or canonical UU-AAP conformance",
            "the drill does not promote recovery interfaces to Stable Core or the interface registry"
        };
        var nonEffects = new[]
        {
            "no main Workbench source mutation",
            "no source restore or rollback",
            "no deletion or modification of the source v0.23 replay capsule or original evidence",
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
            "no cross-machine portability claim",
            "no Stable Core or interface-registry promotion",
            "writes are limited to relocated capsule copies under Workbench/.workbench/recovery-replay-relocations and one drill receipt under Workbench/artifacts/recovery-relocation-drills"
        };

        var receipt = new RecoveryEvidenceRelocationDrillReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            passed,
            passed ? "RELOCATED_LOCAL_REPLAY_CAPSULE_VERIFIED" : "RELOCATION_REPLAY_BINDING_FAILED",
            repositoryRoot,
            before.Head,
            before.Tags,
            before.DirtyPaths,
            after.Head,
            after.Tags,
            after.DirtyPaths,
            mainUnchanged,
            sourceReplayRoot,
            sourceHashes["replay-receipt.json"],
            sourceHashes["source-closure.json"],
            sourceReplay.SourceEvidenceEnvelopeDigest,
            sourceManifestDigest,
            relocationRoot,
            relocatedCapsuleRoot,
            relocatedFiles,
            relocatedManifestDigest,
            evidenceDigest,
            exactFileSet,
            relocatedCopiesVerified,
            separated,
            capsuleManifestDigestReproduced,
            evidenceDigestReproduced,
            sourceReplayVerified,
            positiveReplay,
            admissionBinding,
            matrixReplay,
            admissionBinding,
            negativeRefusalReplay,
            replayLimitationsPreserved,
            true,
            false,
            false,
            false,
            passed,
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
            false,
            scope,
            limitations,
            nonEffects,
            "v0.24 proves that one accepted v0.23 bounded recovery replay capsule can be byte-identically relocated to a separate local Workbench root and replayed from those relocated copies only. This is local retained-evidence relocation, not cross-machine portability, live recovery authority, production-main recovery proof, a general recovery claim, automatic recovery authority, canonical UU-AAP conformance, or Stable Core promotion.");

        var artifactDirectory = Path.Combine(repositoryRoot, "artifacts", "recovery-relocation-drills");
        Directory.CreateDirectory(artifactDirectory);
        var artifactPath = Path.Combine(artifactDirectory, $"recovery-relocation-drill-v0.24-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(receipt, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return (receipt, artifactPath);
    }

    private static bool VerifyReplayReceipt(
        RecoveryEvidenceReplayReceipt replay,
        RecoveryEvidenceClosureReceipt closure,
        IReadOnlyDictionary<string, string> hashes)
    {
        if (!string.Equals(replay.Schema, "matawaka.workbench-recovery-evidence-replay/v0.23", StringComparison.Ordinal) ||
            !string.Equals(replay.Version, "0.23.0", StringComparison.Ordinal) ||
            !replay.Replayed || !string.Equals(replay.Status, "REPLAYED_PORTABLE_BOUNDED_RECOVERY_EVIDENCE_ENVELOPE", StringComparison.Ordinal) ||
            !replay.WorkingTreeClean || !replay.ClosureDigestReproduced || !replay.PortableCopiesVerified || !replay.ReplayUsedOnlyPortableCopies ||
            replay.HistoricalAbsolutePathsDereferencedDuringReplay || replay.OriginalFixtureRootsRequiredForReplay || replay.OriginalEvidenceArtifactsRequiredAfterCapsuleCreation ||
            !replay.PositiveRecoveryDrillReplayed || !replay.RecoveryCapabilityAdmissionReplayed || !replay.NegativeControlMatrixReplayed ||
            !replay.AdmissionToDrillBindingReplayed || !replay.NegativeRefusalSemanticsReplayed || !replay.BoundedRecoveryCapabilityPreserved ||
            replay.CrossMachinePortabilityProven || replay.ProductionMainRepositoryRecoveryProven || replay.GeneralFailureRecoveryClaimAllowed || replay.AutomaticRecoveryAuthorized ||
            replay.RecoveryExecutionAuthorized || replay.RollbackAuthorized || replay.DeletionAuthorized || replay.SourceMutationAuthorized || replay.BuildAuthorized ||
            replay.CheckpointAuthorized || replay.NetworkAccessAuthorized || replay.CatalogMutationAuthorized || replay.AgentExecuteAuthorized || replay.StableCorePromotionAuthorized)
            return false;

        if (!string.Equals(replay.SourceClosureArtifactSha256, hashes["source-closure.json"], StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(replay.SourceEvidenceEnvelopeDigest, closure.EvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(replay.ReplayedEvidenceEnvelopeDigest, closure.EvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase))
            return false;

        var portable = replay.PortableEvidence.ToDictionary(x => x.Role, StringComparer.Ordinal);
        return portable.Count == 3 &&
               PortableMatches(portable, "positive-isolated-drill", "evidence/positive-isolated-drill.json", hashes) &&
               PortableMatches(portable, "bounded-capability-admission", "evidence/bounded-capability-admission.json", hashes) &&
               PortableMatches(portable, "negative-control-matrix", "evidence/negative-control-matrix.json", hashes);
    }

    private static bool PortableMatches(
        IReadOnlyDictionary<string, RecoveryEvidenceReplayItem> portable,
        string role,
        string relative,
        IReadOnlyDictionary<string, string> hashes)
        => portable.TryGetValue(role, out var item) && item.Verified &&
           string.Equals(item.RelativePath.Replace('\\', '/'), relative, StringComparison.Ordinal) &&
           string.Equals(item.Sha256, hashes[relative], StringComparison.OrdinalIgnoreCase);

    private static bool VerifyPositiveDrill(IsolatedRecoveryDrillReceipt drill)
        => string.Equals(drill.Schema, "matawaka.workbench-isolated-recovery-drill/v0.19", StringComparison.Ordinal) &&
           string.Equals(drill.Version, "0.19.0", StringComparison.Ordinal) && drill.Passed && drill.MainRepositoryUnchanged &&
           string.Equals(drill.MainHeadBefore, drill.MainHeadAfter, StringComparison.OrdinalIgnoreCase) &&
           drill.MainDirtyPathsBefore.Count == 0 && drill.MainDirtyPathsAfter.Count == 0 &&
           drill.CandidateDirtyPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "fixture/new.txt", "fixture/tracked.txt" }, StringComparer.Ordinal) &&
           string.Equals(drill.PreRecoveryClassification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) &&
           string.Equals(drill.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) &&
           string.Equals(drill.RecoveryExecutionStatus, "RECOVERED_TO_CURRENT_ACCEPTED_HEAD_FRESH_ASSESSMENT_REQUIRED", StringComparison.Ordinal) &&
           string.Equals(drill.PostRecoveryClassification, "CLEAN_ACCEPTED", StringComparison.Ordinal) && drill.PostRecoveryWorkingTreeClean &&
           drill.TrackedFileRestored && drill.UntrackedAdditionRemoved && drill.FixtureHeadUnchanged && drill.FixtureTagsUnchanged &&
           drill.Authority.ExplicitUiConfirmationRequired && !drill.Authority.MainRepositoryMutationAllowed && !drill.Authority.BuildAllowed &&
           !drill.Authority.CheckpointAllowed && !drill.Authority.NetworkAccessAllowed && !drill.Authority.CatalogMutationAllowed && !drill.Authority.AgentExecuteAllowed;

    private static bool VerifyAdmission(RecoveryCapabilityAdmissionReceipt admission, IsolatedRecoveryDrillReceipt drill, string drillSha)
        => string.Equals(admission.Schema, "matawaka.workbench-recovery-capability-admission/v0.20", StringComparison.Ordinal) &&
           string.Equals(admission.Version, "0.20.0", StringComparison.Ordinal) && admission.Admitted &&
           string.Equals(admission.Status, "ADMITTED_ISOLATED_BOUNDED_RECOVERY_CAPABILITY", StringComparison.Ordinal) &&
           admission.BoundedRecoveryCapabilityAdmitted && string.Equals(admission.EvidenceArtifactSha256, drillSha, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(admission.EvidenceSchema, drill.Schema, StringComparison.Ordinal) && string.Equals(admission.EvidenceVersion, drill.Version, StringComparison.Ordinal) &&
           string.Equals(admission.EvidenceMainHead, drill.MainHeadAfter, StringComparison.OrdinalIgnoreCase) &&
           admission.EvidenceMainTags.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(drill.MainTagsAfter.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal) &&
           !admission.ProductionMainRepositoryRecoveryProven && !admission.GeneralFailureRecoveryClaimAllowed && !admission.AutomaticRecoveryAuthorized &&
           !admission.RecoveryExecutionAuthorized && !admission.RollbackAuthorized && !admission.DeletionAuthorized && !admission.SourceMutationAuthorized &&
           !admission.BuildAuthorized && !admission.CheckpointAuthorized && !admission.NetworkAccessAuthorized && !admission.CatalogMutationAuthorized &&
           !admission.AgentExecuteAuthorized && !admission.StableCorePromotionAuthorized;

    private static bool VerifyNegativeMatrix(RecoveryNegativeControlMatrixReceipt matrix)
        => string.Equals(matrix.Schema, "matawaka.workbench-recovery-negative-control-matrix/v0.21", StringComparison.Ordinal) &&
           string.Equals(matrix.Version, "0.21.0", StringComparison.Ordinal) && matrix.Passed && matrix.MainRepositoryUnchanged &&
           matrix.MainDirtyPathsBefore.Count == 0 && matrix.MainDirtyPathsAfter.Count == 0 &&
           matrix.UnknownDirtyRefused && matrix.ByteDriftAfterPlanRefused && matrix.PathSetDriftAfterPlanRefused && matrix.AllRecoveryAttemptsRefusedBeforeAuthority &&
           matrix.Scenarios.Count == 3 && matrix.Scenarios.All(x => x.Passed && x.ExecutionAttempted && x.ExecutionRejected &&
               !x.RecoveryAuthorityArtifactCreated && !x.RecoveryExecutionArtifactCreated && x.CandidateStatePreservedAfterRefusal && x.FixtureHeadUnchanged && x.FixtureTagsUnchanged) &&
           matrix.Authority.ExplicitUiConfirmationRequired && !matrix.Authority.MainRepositoryMutationAllowed && !matrix.Authority.ExpectedRecoveryMutationAllowed &&
           !matrix.Authority.BuildAllowed && !matrix.Authority.CheckpointAllowed && !matrix.Authority.NetworkAccessAllowed &&
           !matrix.Authority.CatalogMutationAllowed && !matrix.Authority.AgentExecuteAllowed;

    private static bool VerifyNegativeRefusals(RecoveryNegativeControlMatrixReceipt matrix)
    {
        var unknown = matrix.Scenarios.SingleOrDefault(x => string.Equals(x.Id, "unknown-dirty-refused", StringComparison.Ordinal));
        var bytes = matrix.Scenarios.SingleOrDefault(x => string.Equals(x.Id, "candidate-byte-drift-after-plan-refused", StringComparison.Ordinal));
        var paths = matrix.Scenarios.SingleOrDefault(x => string.Equals(x.Id, "dirty-path-set-drift-after-plan-refused", StringComparison.Ordinal));
        return unknown is not null && bytes is not null && paths is not null &&
               string.Equals(unknown.AssessmentClassification, "UNKNOWN_DIRTY_WORKTREE", StringComparison.Ordinal) &&
               string.Equals(unknown.RecoveryPlanStatus, "REFUSED_UNBOUNDED_RECOVERY_PLAN", StringComparison.Ordinal) && !unknown.SeparateRecoveryAuthorityEligible &&
               string.Equals(bytes.AssessmentClassification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) &&
               string.Equals(bytes.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) && bytes.SeparateRecoveryAuthorityEligible &&
               bytes.RejectionMessage.Contains("byte-bound", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(paths.AssessmentClassification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) &&
               string.Equals(paths.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) && paths.SeparateRecoveryAuthorityEligible &&
               paths.RejectionMessage.Contains("changed after", StringComparison.OrdinalIgnoreCase);
    }

    private static bool VerifyAuthorityLimitations(
        RecoveryEvidenceReplayReceipt replay,
        RecoveryEvidenceClosureReceipt closure,
        RecoveryCapabilityAdmissionReceipt admission)
        => !replay.CrossMachinePortabilityProven && !replay.ProductionMainRepositoryRecoveryProven && !replay.GeneralFailureRecoveryClaimAllowed &&
           !replay.AutomaticRecoveryAuthorized && !replay.RecoveryExecutionAuthorized && !replay.RollbackAuthorized && !replay.DeletionAuthorized &&
           !replay.SourceMutationAuthorized && !replay.BuildAuthorized && !replay.CheckpointAuthorized && !replay.NetworkAccessAuthorized &&
           !replay.CatalogMutationAuthorized && !replay.AgentExecuteAuthorized && !replay.StableCorePromotionAuthorized &&
           !closure.ProductionMainRepositoryRecoveryProven && !closure.GeneralFailureRecoveryClaimAllowed && !closure.AutomaticRecoveryAuthorized &&
           !closure.RecoveryExecutionAuthorized && !closure.RollbackAuthorized && !closure.DeletionAuthorized && !closure.SourceMutationAuthorized &&
           !closure.BuildAuthorized && !closure.CheckpointAuthorized && !closure.NetworkAccessAuthorized && !closure.CatalogMutationAuthorized &&
           !closure.AgentExecuteAuthorized && !closure.StableCorePromotionAuthorized && admission.BoundedRecoveryCapabilityAdmitted &&
           !admission.ProductionMainRepositoryRecoveryProven && !admission.GeneralFailureRecoveryClaimAllowed && !admission.AutomaticRecoveryAuthorized &&
           !admission.RecoveryExecutionAuthorized && !admission.RollbackAuthorized && !admission.DeletionAuthorized && !admission.SourceMutationAuthorized &&
           !admission.BuildAuthorized && !admission.CheckpointAuthorized && !admission.NetworkAccessAuthorized && !admission.CatalogMutationAuthorized &&
           !admission.AgentExecuteAuthorized && !admission.StableCorePromotionAuthorized;

    private static string HashEvidenceEnvelope(RecoveryEvidenceClosureReceipt closure, IReadOnlyDictionary<string, string> relocatedHashes)
    {
        var relativeByRole = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["positive-isolated-drill"] = "evidence/positive-isolated-drill.json",
            ["bounded-capability-admission"] = "evidence/bounded-capability-admission.json",
            ["negative-control-matrix"] = "evidence/negative-control-matrix.json"
        };
        var canonical = string.Join("\n", closure.Evidence.OrderBy(x => x.Role, StringComparer.Ordinal).Select(x =>
        {
            if (!relativeByRole.TryGetValue(x.Role, out var relative))
                throw new InvalidDataException($"Unexpected v0.22 evidence role during relocated replay: {x.Role}");
            return $"{x.Role}|{relocatedHashes[relative]}|{x.Schema}|{x.Version}";
        })) + "\n";
        return HashBytes(Encoding.UTF8.GetBytes(canonical));
    }

    private static string HashCapsuleManifest(IReadOnlyDictionary<string, string> hashes)
    {
        var canonical = string.Join("\n", hashes.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}|{x.Value}")) + "\n";
        return HashBytes(Encoding.UTF8.GetBytes(canonical));
    }

    private static string FindLatestReplayCapsule(string repositoryRoot)
    {
        var root = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "recovery-replays"));
        if (!Directory.Exists(root)) throw new InvalidDataException("No retained v0.23 replay capsule is available for relocation.");
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var directory in Directory.GetDirectories(root, "replay-capsule-v0.23-*", SearchOption.TopDirectoryOnly).OrderByDescending(Directory.GetLastWriteTimeUtc))
        {
            try
            {
                var full = Path.GetFullPath(directory);
                if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var receiptPath = Path.Combine(full, "replay-receipt.json");
                if (!File.Exists(receiptPath)) continue;
                var replay = DeserializeFile<RecoveryEvidenceReplayReceipt>(receiptPath, "candidate retained v0.23 replay receipt");
                if (replay.Replayed && string.Equals(replay.Status, "REPLAYED_PORTABLE_BOUNDED_RECOVERY_EVIDENCE_ENVELOPE", StringComparison.Ordinal))
                    return full;
            }
            catch
            {
                // Unreadable replay evidence cannot support relocation; continue to older capsule.
            }
        }
        throw new InvalidDataException("No passing retained v0.23 replay capsule is available for relocation.");
    }

    private static IReadOnlyList<string> EnumerateRelativeFiles(string capsuleRoot)
    {
        var root = Path.GetFullPath(capsuleRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Directory.GetFiles(capsuleRoot, "*", SearchOption.AllDirectories)
            .Select(file =>
            {
                var full = Path.GetFullPath(file);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Replay capsule file escapes the capsule root.");
                return Path.GetRelativePath(capsuleRoot, full).Replace('\\', '/');
            })
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveCapsuleFile(string capsuleRoot, string relative, bool requireExists = true)
    {
        if (Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsafe replay capsule relative path: {relative}");
        var root = Path.GetFullPath(capsuleRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(capsuleRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Replay capsule path escapes root: {relative}");
        if (requireExists && !File.Exists(full))
            throw new InvalidDataException($"Replay capsule file is missing: {relative}");
        return full;
    }

    private static T DeserializeFile<T>(string path, string label)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
           ?? throw new InvalidDataException($"{label} could not be parsed.");

    private static T DeserializeBytes<T>(byte[] bytes, string label)
        => JsonSerializer.Deserialize<T>(bytes, JsonOptions)
           ?? throw new InvalidDataException($"{label} could not be parsed.");

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository not found: {root}");
        return root;
    }

    private sealed record GitState(string Head, IReadOnlyList<string> Tags, IReadOnlyList<string> DirtyPaths);

    private static async Task<GitState> ObserveGitStateAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var head = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var tags = SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD").ConfigureAwait(false));
        var status = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all").ConfigureAwait(false);
        return new GitState(head, tags, ParseStatusPaths(status));
    }

    private static IReadOnlyList<string> ParseStatusPaths(string output)
    {
        var paths = new List<string>();
        foreach (var raw in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4) throw new InvalidDataException($"Unexpected git status porcelain line in recovery relocation drill: {raw}");
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
            throw new InvalidDataException($"Non-allowlisted read-only Git operation in recovery relocation drill: {args[0]}");

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
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only Git recovery-relocation process.");
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
            throw new TimeoutException($"Read-only Git recovery-relocation operation timed out after {GitTimeout.TotalSeconds:0} seconds.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Read-only Git recovery-relocation operation failed: {string.Join(' ', args)} :: {stderr.Trim()}");
        return stdout;
    }

    private static string HashFile(string path) => HashBytes(File.ReadAllBytes(path));
    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
