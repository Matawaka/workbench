using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RecoveryEvidenceClosureItem(
    string Role,
    string ArtifactPath,
    string Sha256,
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    bool Verified);

public sealed record RecoveryEvidenceClosureReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    bool Closed,
    string Status,
    string MainRepositoryRoot,
    string CurrentHead,
    IReadOnlyList<string> CurrentTags,
    bool WorkingTreeClean,
    IReadOnlyList<RecoveryEvidenceClosureItem> Evidence,
    string EvidenceEnvelopeDigest,
    bool PositiveRecoveryDrillVerified,
    bool RecoveryCapabilityAdmissionVerified,
    bool NegativeControlMatrixVerified,
    bool AdmissionToDrillBindingVerified,
    bool CrossEvidenceBindingsVerified,
    bool PositiveRecoveryShapeVerified,
    bool UnknownDirtyRefusalVerified,
    bool CandidateByteDriftRefusalVerified,
    bool DirtyPathSetDriftRefusalVerified,
    bool AllNegativeRecoveryAttemptsRefusedBeforeAuthority,
    bool MainRepositoryUnchangedAcrossFixtureEvidence,
    bool BoundedRecoveryCapabilityPreserved,
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
    IReadOnlyList<string> ClosedScope,
    IReadOnlyList<string> EvidenceLimitations,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Read-only closure over retained v0.19 positive drill evidence, v0.20
/// capability admission, and the v0.21 negative-control matrix. Closure binds
/// exact evidence bytes and verifies cross-evidence relationships. It does not
/// execute recovery and does not turn evidence into broader authority.
/// </summary>
public sealed class RecoveryEvidenceClosureService
{
    public const string Version = "0.22.0";
    public const string ReceiptSchema = "matawaka.workbench-recovery-evidence-closure/v0.22";
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<(RecoveryEvidenceClosureReceipt Receipt, string ArtifactPath)> CloseAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var currentHead = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var currentTags = SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD").ConfigureAwait(false));
        var status = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all").ConfigureAwait(false);
        var dirtyPaths = ParseStatusPaths(status);

        if (dirtyPaths.Count != 0)
            throw new InvalidDataException("Recovery evidence closure requires a clean accepted main Workbench repository.");
        if (!currentTags.Contains("workbench-v0.22-accepted", StringComparer.Ordinal))
            throw new InvalidDataException("Recovery evidence closure is enabled only after workbench-v0.22-accepted points at the current HEAD.");

        var admissionPath = FindLatestAdmissionArtifact(repositoryRoot);
        var admissionSha = HashFile(admissionPath);
        var admission = DeserializeFile<RecoveryCapabilityAdmissionReceipt>(admissionPath, "recovery capability admission");

        var matrixPath = FindLatestNegativeMatrixArtifact(repositoryRoot);
        var matrixSha = HashFile(matrixPath);
        var matrix = DeserializeFile<RecoveryNegativeControlMatrixReceipt>(matrixPath, "recovery negative-control matrix");

        var drillPath = ValidateEvidencePath(
            repositoryRoot,
            admission.EvidenceArtifactPath,
            Path.Combine("artifacts", "recovery-drills"),
            "recovery drill evidence");
        var drillSha = HashFile(drillPath);
        var drill = DeserializeFile<IsolatedRecoveryDrillReceipt>(drillPath, "isolated recovery drill");

        var positiveDrillVerified =
            string.Equals(drill.Schema, "matawaka.workbench-isolated-recovery-drill/v0.19", StringComparison.Ordinal) &&
            string.Equals(drill.Version, "0.19.0", StringComparison.Ordinal) &&
            drill.Passed &&
            string.Equals(Path.GetFullPath(drill.MainRepositoryRoot), repositoryRoot, StringComparison.OrdinalIgnoreCase) &&
            drill.MainRepositoryUnchanged &&
            string.Equals(drill.MainHeadBefore, drill.MainHeadAfter, StringComparison.OrdinalIgnoreCase) &&
            drill.MainDirtyPathsBefore.Count == 0 &&
            drill.MainDirtyPathsAfter.Count == 0 &&
            drill.MainTagsBefore.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(drill.MainTagsAfter.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal) &&
            drill.MainTagsAfter.Contains("workbench-v0.19-accepted", StringComparer.Ordinal) &&
            drill.CandidateDirtyPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "fixture/new.txt", "fixture/tracked.txt" }, StringComparer.Ordinal) &&
            string.Equals(drill.PreRecoveryClassification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) &&
            string.Equals(drill.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) &&
            string.Equals(drill.RecoveryExecutionStatus, "RECOVERED_TO_CURRENT_ACCEPTED_HEAD_FRESH_ASSESSMENT_REQUIRED", StringComparison.Ordinal) &&
            string.Equals(drill.PostRecoveryClassification, "CLEAN_ACCEPTED", StringComparison.Ordinal) &&
            drill.PostRecoveryWorkingTreeClean &&
            drill.TrackedFileRestored && drill.UntrackedAdditionRemoved &&
            drill.FixtureHeadUnchanged && drill.FixtureTagsUnchanged &&
            drill.Authority.ExplicitUiConfirmationRequired &&
            !drill.Authority.MainRepositoryMutationAllowed &&
            !drill.Authority.BuildAllowed && !drill.Authority.CheckpointAllowed &&
            !drill.Authority.NetworkAccessAllowed && !drill.Authority.CatalogMutationAllowed &&
            !drill.Authority.AgentExecuteAllowed;

        var admissionToDrillBindingVerified =
            string.Equals(Path.GetFullPath(admission.EvidenceArtifactPath), drillPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(admission.EvidenceArtifactSha256, drillSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(admission.EvidenceSchema, drill.Schema, StringComparison.Ordinal) &&
            string.Equals(admission.EvidenceVersion, drill.Version, StringComparison.Ordinal) &&
            string.Equals(admission.EvidenceMainHead, drill.MainHeadAfter, StringComparison.OrdinalIgnoreCase) &&
            admission.EvidenceMainTags.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(drill.MainTagsAfter.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);

        var admissionVerified =
            string.Equals(admission.Schema, "matawaka.workbench-recovery-capability-admission/v0.20", StringComparison.Ordinal) &&
            string.Equals(admission.Version, "0.20.0", StringComparison.Ordinal) &&
            admission.Admitted &&
            string.Equals(admission.Status, "ADMITTED_ISOLATED_BOUNDED_RECOVERY_CAPABILITY", StringComparison.Ordinal) &&
            admission.BoundedRecoveryCapabilityAdmitted &&
            !admission.ProductionMainRepositoryRecoveryProven &&
            !admission.GeneralFailureRecoveryClaimAllowed &&
            !admission.AutomaticRecoveryAuthorized &&
            !admission.RecoveryExecutionAuthorized && !admission.RollbackAuthorized && !admission.DeletionAuthorized &&
            !admission.SourceMutationAuthorized && !admission.BuildAuthorized && !admission.CheckpointAuthorized &&
            !admission.NetworkAccessAuthorized && !admission.CatalogMutationAuthorized && !admission.AgentExecuteAuthorized &&
            !admission.StableCorePromotionAuthorized &&
            admissionToDrillBindingVerified;

        var matrixScenariosVerified = matrix.Scenarios.Count == 3 && matrix.Scenarios.All(x =>
            x.Passed && x.ExecutionAttempted && x.ExecutionRejected &&
            !x.RecoveryAuthorityArtifactCreated && !x.RecoveryExecutionArtifactCreated &&
            x.CandidateStatePreservedAfterRefusal && x.FixtureHeadUnchanged && x.FixtureTagsUnchanged);

        var negativeMatrixVerified =
            string.Equals(matrix.Schema, "matawaka.workbench-recovery-negative-control-matrix/v0.21", StringComparison.Ordinal) &&
            string.Equals(matrix.Version, "0.21.0", StringComparison.Ordinal) &&
            matrix.Passed && matrix.MainRepositoryUnchanged &&
            string.Equals(matrix.MainHeadBefore, matrix.MainHeadAfter, StringComparison.OrdinalIgnoreCase) &&
            matrix.MainDirtyPathsBefore.Count == 0 && matrix.MainDirtyPathsAfter.Count == 0 &&
            matrix.MainTagsBefore.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(matrix.MainTagsAfter.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal) &&
            matrix.MainTagsAfter.Contains("workbench-v0.21-accepted", StringComparer.Ordinal) &&
            matrix.UnknownDirtyRefused && matrix.ByteDriftAfterPlanRefused && matrix.PathSetDriftAfterPlanRefused &&
            matrix.AllRecoveryAttemptsRefusedBeforeAuthority && matrixScenariosVerified &&
            matrix.Authority.ExplicitUiConfirmationRequired && !matrix.Authority.MainRepositoryMutationAllowed &&
            !matrix.Authority.ExpectedRecoveryMutationAllowed && !matrix.Authority.BuildAllowed && !matrix.Authority.CheckpointAllowed &&
            !matrix.Authority.NetworkAccessAllowed && !matrix.Authority.CatalogMutationAllowed && !matrix.Authority.AgentExecuteAllowed;

        var unknownScenario = matrix.Scenarios.SingleOrDefault(x => string.Equals(x.Id, "unknown-dirty-refused", StringComparison.Ordinal));
        var byteDriftScenario = matrix.Scenarios.SingleOrDefault(x => string.Equals(x.Id, "candidate-byte-drift-after-plan-refused", StringComparison.Ordinal));
        var pathDriftScenario = matrix.Scenarios.SingleOrDefault(x => string.Equals(x.Id, "dirty-path-set-drift-after-plan-refused", StringComparison.Ordinal));

        var unknownDirtyVerified = unknownScenario is not null &&
            string.Equals(unknownScenario.AssessmentClassification, "UNKNOWN_DIRTY_WORKTREE", StringComparison.Ordinal) &&
            string.Equals(unknownScenario.RecoveryPlanStatus, "REFUSED_UNBOUNDED_RECOVERY_PLAN", StringComparison.Ordinal) &&
            !unknownScenario.SeparateRecoveryAuthorityEligible && unknownScenario.ExecutionRejected &&
            !unknownScenario.RecoveryAuthorityArtifactCreated && !unknownScenario.RecoveryExecutionArtifactCreated;

        var byteDriftVerified = byteDriftScenario is not null &&
            string.Equals(byteDriftScenario.AssessmentClassification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) &&
            string.Equals(byteDriftScenario.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) &&
            byteDriftScenario.SeparateRecoveryAuthorityEligible && byteDriftScenario.ExecutionRejected &&
            !byteDriftScenario.RecoveryAuthorityArtifactCreated && !byteDriftScenario.RecoveryExecutionArtifactCreated;

        var pathDriftVerified = pathDriftScenario is not null &&
            string.Equals(pathDriftScenario.AssessmentClassification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) &&
            string.Equals(pathDriftScenario.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) &&
            pathDriftScenario.SeparateRecoveryAuthorityEligible && pathDriftScenario.ExecutionRejected &&
            !pathDriftScenario.RecoveryAuthorityArtifactCreated && !pathDriftScenario.RecoveryExecutionArtifactCreated &&
            string.Equals(pathDriftScenario.PostControlClassification, "UNKNOWN_DIRTY_WORKTREE", StringComparison.Ordinal);

        var mainEvidenceUnchanged = drill.MainRepositoryUnchanged && matrix.MainRepositoryUnchanged;
        var crossBindingsVerified = positiveDrillVerified && admissionVerified && negativeMatrixVerified &&
            admissionToDrillBindingVerified && unknownDirtyVerified && byteDriftVerified && pathDriftVerified && mainEvidenceUnchanged;

        var evidence = new[]
        {
            new RecoveryEvidenceClosureItem("positive-isolated-drill", drillPath, drillSha, drill.Schema, drill.Version, drill.ObservedAt, positiveDrillVerified),
            new RecoveryEvidenceClosureItem("bounded-capability-admission", admissionPath, admissionSha, admission.Schema, admission.Version, admission.ObservedAt, admissionVerified),
            new RecoveryEvidenceClosureItem("negative-control-matrix", matrixPath, matrixSha, matrix.Schema, matrix.Version, matrix.ObservedAt, negativeMatrixVerified)
        };
        var envelopeDigest = HashEnvelope(evidence);
        var closed = crossBindingsVerified;

        var closedScope = new[]
        {
            "one positive nested-fixture recovery shape: exact tracked Replace plus exact untracked Add",
            "one read-only admission that preserves the bounded scope of the positive drill",
            "unknown dirty state is refused before separate recovery authority",
            "candidate byte drift after READY plan is refused before recovery authority artifact creation",
            "dirty path-set drift after READY plan is refused before recovery authority artifact creation",
            "main Workbench repository remains unchanged by positive and negative fixture evidence"
        };
        var limitations = new[]
        {
            "evidence is isolated-fixture evidence, not production-main-repository recovery proof",
            "positive evidence covers one tracked Replace plus one untracked Add shape",
            "negative controls cover three refusal modes and are not exhaustive",
            "closure does not prove recovery from arbitrary process, filesystem, Git, power-loss, corruption, or concurrent-writer failures",
            "closure does not authorize automatic recovery",
            "closure does not establish canonical UU-AAP conformance",
            "closure does not promote recovery interfaces to Stable Core or the interface registry"
        };
        var nonEffects = new[]
        {
            "no source file mutation",
            "no source restore or rollback",
            "no file or directory deletion",
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
            "closure artifact write is limited to Workbench/artifacts/recovery-closures"
        };

        var receipt = new RecoveryEvidenceClosureReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            closed,
            closed ? "CLOSED_BOUNDED_RECOVERY_EVIDENCE_ENVELOPE" : "OPEN_RECOVERY_EVIDENCE_BINDING_INCOMPLETE",
            repositoryRoot,
            currentHead,
            currentTags,
            true,
            evidence,
            envelopeDigest,
            positiveDrillVerified,
            admissionVerified,
            negativeMatrixVerified,
            admissionToDrillBindingVerified,
            crossBindingsVerified,
            positiveDrillVerified,
            unknownDirtyVerified,
            byteDriftVerified,
            pathDriftVerified,
            matrix.AllRecoveryAttemptsRefusedBeforeAuthority,
            mainEvidenceUnchanged,
            admission.BoundedRecoveryCapabilityAdmitted && closed,
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
            closedScope,
            limitations,
            nonEffects,
            "v0.22 closes retained v0.19 positive drill evidence, v0.20 bounded capability admission, and v0.21 negative-control refusals into one byte-bound Workbench recovery evidence envelope. Closure preserves the admitted narrow capability and its limitations; it is not recovery execution authority, production-main recovery proof, a general recovery claim, automatic recovery authority, canonical UU-AAP conformance, or Stable Core promotion.");

        var artifactDir = Path.Combine(repositoryRoot, "artifacts", "recovery-closures");
        Directory.CreateDirectory(artifactDir);
        var artifactPath = Path.Combine(artifactDir, $"recovery-evidence-closure-v0.22-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(receipt, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return (receipt, artifactPath);
    }

    private static string FindLatestAdmissionArtifact(string repositoryRoot)
        => FindLatestMatchingArtifact<RecoveryCapabilityAdmissionReceipt>(
            repositoryRoot,
            Path.Combine("artifacts", "recovery-admissions"),
            "recovery-admission-v0.20-*.json",
            x => x.Admitted && string.Equals(x.Schema, "matawaka.workbench-recovery-capability-admission/v0.20", StringComparison.Ordinal),
            "No admitted retained v0.20 recovery capability admission artifact is available for closure.");

    private static string FindLatestNegativeMatrixArtifact(string repositoryRoot)
        => FindLatestMatchingArtifact<RecoveryNegativeControlMatrixReceipt>(
            repositoryRoot,
            Path.Combine("artifacts", "recovery-negative-controls"),
            "recovery-negative-control-matrix-v0.21-*.json",
            x => x.Passed && string.Equals(x.Schema, "matawaka.workbench-recovery-negative-control-matrix/v0.21", StringComparison.Ordinal),
            "No passing retained v0.21 recovery negative-control matrix artifact is available for closure.");

    private static string FindLatestMatchingArtifact<T>(
        string repositoryRoot,
        string relativeRoot,
        string pattern,
        Func<T, bool> predicate,
        string noneMessage)
    {
        var root = Path.GetFullPath(Path.Combine(repositoryRoot, relativeRoot));
        if (!Directory.Exists(root)) throw new InvalidDataException(noneMessage);
        var rootPrefix = root + Path.DirectorySeparatorChar;
        foreach (var file in Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var full = Path.GetFullPath(file);
                if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var parsed = JsonSerializer.Deserialize<T>(File.ReadAllText(full, Encoding.UTF8), JsonOptions);
                if (parsed is not null && predicate(parsed)) return full;
            }
            catch
            {
                // Unreadable evidence cannot support closure; continue to older retained evidence.
            }
        }
        throw new InvalidDataException(noneMessage);
    }

    private static string ValidateEvidencePath(string repositoryRoot, string candidate, string relativeRoot, string label)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
            throw new InvalidDataException($"Retained {label} file is missing.");
        var root = Path.GetFullPath(Path.Combine(repositoryRoot, relativeRoot));
        var full = Path.GetFullPath(candidate);
        var rootPrefix = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Retained {label} escapes its allowed artifact root.");
        return full;
    }

    private static T DeserializeFile<T>(string path, string label)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
           ?? throw new InvalidDataException($"Retained {label} artifact could not be parsed.");

    private static string HashEnvelope(IEnumerable<RecoveryEvidenceClosureItem> evidence)
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
            if (raw.Length < 4) throw new InvalidDataException($"Unexpected git status porcelain line in recovery evidence closure: {raw}");
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
            throw new InvalidDataException($"Non-allowlisted read-only Git operation in recovery evidence closure: {args[0]}");

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
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only Git recovery-closure process.");
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
            throw new TimeoutException($"Read-only Git recovery-closure operation timed out after {GitTimeout.TotalSeconds:0} seconds.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Read-only Git recovery-closure operation failed: {string.Join(' ', args)} :: {stderr.Trim()}");
        return stdout;
    }

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
