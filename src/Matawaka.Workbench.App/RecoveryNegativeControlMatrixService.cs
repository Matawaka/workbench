using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RecoveryNegativeControlMatrixAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string MainRepositoryRoot,
    string MatrixRoot,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool MainRepositoryMutationAllowed,
    bool FixtureGitInitializationAllowed,
    bool FixtureCandidateMutationAllowed,
    bool FixtureRecoveryExecutionAttemptAllowed,
    bool ExpectedRecoveryMutationAllowed,
    bool BuildAllowed,
    bool CheckpointAllowed,
    bool NetworkAccessAllowed,
    bool CatalogMutationAllowed,
    bool AgentExecuteAllowed,
    IReadOnlyList<string> AllowedEffects,
    IReadOnlyList<string> NonEffects);

public sealed record RecoveryNegativeControlScenarioReceipt(
    string Id,
    bool Passed,
    string FixtureRepositoryRoot,
    string FixtureAcceptedHead,
    IReadOnlyList<string> FixtureAcceptedTags,
    string AssessmentClassification,
    string RecoveryPlanStatus,
    bool SeparateRecoveryAuthorityEligible,
    string NegativeMutation,
    IReadOnlyList<string> DirtyPathsAtExecutionAttempt,
    bool ExecutionAttempted,
    bool ExecutionRejected,
    string RejectionMessage,
    bool RecoveryAuthorityArtifactCreated,
    bool RecoveryExecutionArtifactCreated,
    bool CandidateStatePreservedAfterRefusal,
    bool FixtureHeadUnchanged,
    bool FixtureTagsUnchanged,
    string PostControlClassification,
    bool PostControlRecoveryRequired,
    string AssessmentArtifactPath,
    string PlanArtifactPath);

public sealed record RecoveryNegativeControlMatrixReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    bool Passed,
    string MainRepositoryRoot,
    string MainHeadBefore,
    IReadOnlyList<string> MainTagsBefore,
    IReadOnlyList<string> MainDirtyPathsBefore,
    string MainHeadAfter,
    IReadOnlyList<string> MainTagsAfter,
    IReadOnlyList<string> MainDirtyPathsAfter,
    bool MainRepositoryUnchanged,
    string MatrixRoot,
    IReadOnlyList<RecoveryNegativeControlScenarioReceipt> Scenarios,
    bool UnknownDirtyRefused,
    bool ByteDriftAfterPlanRefused,
    bool PathSetDriftAfterPlanRefused,
    bool AllRecoveryAttemptsRefusedBeforeAuthority,
    RecoveryNegativeControlMatrixAuthorityReceipt Authority,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Isolated negative-control matrix for the accepted bounded recovery gates.
/// It never dirties the main Workbench repository. Each control runs in its
/// own nested local Git fixture under .workbench/recovery-negative-controls.
/// The controls prove refusal for an unknown dirty path, for candidate-byte
/// drift after a READY recovery plan, and for dirty-path-set drift after a
/// READY recovery plan. Recovery execution is called only inside the fixtures
/// and is expected to fail before creating recovery authority or mutating the
/// candidate state.
/// </summary>
public sealed class RecoveryNegativeControlMatrixService
{
    public const string Version = "0.21.0";
    public const string ReceiptSchema = "matawaka.workbench-recovery-negative-control-matrix/v0.21";
    public const string AuthoritySchema = "matawaka.workbench-recovery-negative-control-matrix-authority/v0.21";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly MaintenanceRecoveryAssessmentService _assessmentService = new();
    private readonly MaintenanceRecoveryPlanService _planService = new();
    private readonly MaintenanceRecoveryExecutionService _executionService = new();

    public async Task<(RecoveryNegativeControlMatrixReceipt Receipt, string ArtifactPath)> RunAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var mainRepositoryRoot = ResolveMainRepositoryRoot(workspaceRoot);
        var mainBefore = await ObserveGitStateAsync(mainRepositoryRoot, cancellationToken).ConfigureAwait(false);
        if (mainBefore.DirtyPaths.Count != 0)
            throw new InvalidDataException("Recovery negative-control matrix requires a clean accepted main Workbench repository.");
        if (!mainBefore.Tags.Contains("workbench-v0.21-accepted", StringComparer.Ordinal))
            throw new InvalidDataException("Recovery negative-control matrix is enabled only after workbench-v0.21-accepted points at the current main HEAD.");

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var matrixRoot = Path.Combine(mainRepositoryRoot, ".workbench", "recovery-negative-controls", $"v0.21-{stamp}");
        Directory.CreateDirectory(matrixRoot);

        var nonEffects = new[]
        {
            "no main Workbench source mutation",
            "no main Workbench git add/commit/tag",
            "no main Workbench rollback or deletion",
            "no dotnet restore/build/test/publish",
            "no git fetch or push",
            "no remote creation/update",
            "no network access",
            "no Matawaka catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no cleanup or deletion of retained negative-control evidence",
            "fixture Git/candidate mutation is bounded to nested .workbench/recovery-negative-controls repositories"
        };

        var authority = new RecoveryNegativeControlMatrixAuthorityReceipt(
            AuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.maintenance.isolated-recovery-negative-control-matrix",
            mainRepositoryRoot,
            matrixRoot,
            "explicit Recovery negatives button + confirmation dialog after v0.21 accepted",
            true,
            false,
            true,
            true,
            true,
            false,
            false,
            false,
            false,
            false,
            false,
            new[]
            {
                "create and retain three nested local Git fixtures under .workbench/recovery-negative-controls",
                "commit/tag only tiny fixture accepted states using fixed Git arguments",
                "create exact negative-control dirty states only in fixture repositories",
                "run existing recovery assessment/plan/execution gates only against fixtures",
                "write retained negative-control evidence under fixture artifacts and main Workbench artifacts/recovery-negative-controls"
            },
            nonEffects);

        var unknown = await RunUnknownDirtyControlAsync(Path.Combine(matrixRoot, "unknown-dirty"), cancellationToken).ConfigureAwait(false);
        var byteDrift = await RunByteDriftControlAsync(Path.Combine(matrixRoot, "byte-drift-after-plan"), cancellationToken).ConfigureAwait(false);
        var pathDrift = await RunPathSetDriftControlAsync(Path.Combine(matrixRoot, "path-set-drift-after-plan"), cancellationToken).ConfigureAwait(false);

        var mainAfter = await ObserveGitStateAsync(mainRepositoryRoot, cancellationToken).ConfigureAwait(false);
        var mainUnchanged = StatesEqual(mainBefore, mainAfter);
        var scenarios = new[] { unknown, byteDrift, pathDrift };
        var allRefusedBeforeAuthority = scenarios.All(x =>
            x.ExecutionAttempted && x.ExecutionRejected &&
            !x.RecoveryAuthorityArtifactCreated && !x.RecoveryExecutionArtifactCreated);
        var passed = scenarios.All(x => x.Passed) && allRefusedBeforeAuthority && mainUnchanged;

        var receipt = new RecoveryNegativeControlMatrixReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            passed,
            mainRepositoryRoot,
            mainBefore.Head,
            mainBefore.Tags,
            mainBefore.DirtyPaths,
            mainAfter.Head,
            mainAfter.Tags,
            mainAfter.DirtyPaths,
            mainUnchanged,
            matrixRoot,
            scenarios,
            unknown.Passed,
            byteDrift.Passed,
            pathDrift.Passed,
            allRefusedBeforeAuthority,
            authority,
            nonEffects,
            "The v0.21 matrix is a negative-control proof against nested fixtures only. It demonstrates that unknown dirty state, candidate-byte drift after planning, and dirty-path-set drift after planning do not receive bounded recovery execution authority. It does not prove every refusal mode, does not mutate or recover the main Workbench repository, and does not create automatic recovery or Stable Core authority.");

        var artifactDir = Path.Combine(mainRepositoryRoot, "artifacts", "recovery-negative-controls");
        Directory.CreateDirectory(artifactDir);
        var artifactPath = Path.Combine(artifactDir, $"recovery-negative-control-matrix-v0.21-{stamp}.json");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(receipt, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return (receipt, artifactPath);
    }

    private async Task<RecoveryNegativeControlScenarioReceipt> RunUnknownDirtyControlAsync(
        string scenarioRoot,
        CancellationToken cancellationToken)
    {
        var fixture = await InitializeFixtureAsync(scenarioRoot, "unknown-dirty", cancellationToken).ConfigureAwait(false);
        var rogueRelative = "fixture/rogue.txt";
        var roguePath = Path.Combine(fixture.RepositoryRoot, "fixture", "rogue.txt");
        var rogueBytes = Utf8NoBom.GetBytes("unknown-dirty-v0.21\n");
        await File.WriteAllBytesAsync(roguePath, rogueBytes, cancellationToken).ConfigureAwait(false);

        var assessment = await _assessmentService.AssessAsync(scenarioRoot, cancellationToken).ConfigureAwait(false);
        var assessmentPath = await WriteAssessmentAsync(fixture.RepositoryRoot, "unknown-dirty", assessment, cancellationToken).ConfigureAwait(false);
        var plan = await _planService.PlanAsync(scenarioRoot, assessmentPath, assessment, cancellationToken).ConfigureAwait(false);
        var planPath = await WritePlanAsync(fixture.RepositoryRoot, "unknown-dirty", plan, cancellationToken).ConfigureAwait(false);

        var atAttempt = await ObserveGitStateAsync(fixture.RepositoryRoot, cancellationToken).ConfigureAwait(false);
        var beforeArtifacts = CountRecoveryExecutionArtifacts(fixture.RepositoryRoot);
        var (rejected, rejectionMessage) = await ExpectExecutionRefusalAsync(scenarioRoot, assessmentPath, assessment, planPath, plan, cancellationToken).ConfigureAwait(false);
        var afterArtifacts = CountRecoveryExecutionArtifacts(fixture.RepositoryRoot);
        var post = await _assessmentService.AssessAsync(scenarioRoot, cancellationToken).ConfigureAwait(false);
        var finalState = await ObserveGitStateAsync(fixture.RepositoryRoot, cancellationToken).ConfigureAwait(false);

        var authorityCreated = afterArtifacts.AuthorityCount > beforeArtifacts.AuthorityCount;
        var executionCreated = afterArtifacts.ExecutionCount > beforeArtifacts.ExecutionCount;
        var candidatePreserved = File.Exists(roguePath) && string.Equals(HashFile(roguePath), HashBytes(rogueBytes), StringComparison.OrdinalIgnoreCase);
        var headUnchanged = string.Equals(fixture.AcceptedHead, finalState.Head, StringComparison.OrdinalIgnoreCase);
        var tagsUnchanged = fixture.AcceptedTags.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(finalState.Tags.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);
        var passed =
            string.Equals(assessment.Classification, "UNKNOWN_DIRTY_WORKTREE", StringComparison.Ordinal) && assessment.RecoveryRequired &&
            string.Equals(plan.Status, "REFUSED_UNBOUNDED_RECOVERY_PLAN", StringComparison.Ordinal) && !plan.SeparateRecoveryAuthorityEligible &&
            atAttempt.DirtyPaths.SequenceEqual(new[] { rogueRelative }, StringComparer.Ordinal) &&
            rejected && !authorityCreated && !executionCreated && candidatePreserved && headUnchanged && tagsUnchanged &&
            string.Equals(post.Classification, "UNKNOWN_DIRTY_WORKTREE", StringComparison.Ordinal) && post.RecoveryRequired;

        return new RecoveryNegativeControlScenarioReceipt(
            "unknown-dirty-refused",
            passed,
            fixture.RepositoryRoot,
            fixture.AcceptedHead,
            fixture.AcceptedTags,
            assessment.Classification,
            plan.Status,
            plan.SeparateRecoveryAuthorityEligible,
            "create one dirty path with no retained candidate evidence",
            atAttempt.DirtyPaths,
            true,
            rejected,
            rejectionMessage,
            authorityCreated,
            executionCreated,
            candidatePreserved,
            headUnchanged,
            tagsUnchanged,
            post.Classification,
            post.RecoveryRequired,
            assessmentPath,
            planPath);
    }

    private async Task<RecoveryNegativeControlScenarioReceipt> RunByteDriftControlAsync(
        string scenarioRoot,
        CancellationToken cancellationToken)
    {
        var prepared = await PrepareBoundedCandidateAsync(scenarioRoot, "byte-drift", cancellationToken).ConfigureAwait(false);
        var assessment = await _assessmentService.AssessAsync(scenarioRoot, cancellationToken).ConfigureAwait(false);
        var assessmentPath = await WriteAssessmentAsync(prepared.Fixture.RepositoryRoot, "byte-drift", assessment, cancellationToken).ConfigureAwait(false);
        var plan = await _planService.PlanAsync(scenarioRoot, assessmentPath, assessment, cancellationToken).ConfigureAwait(false);
        var planPath = await WritePlanAsync(prepared.Fixture.RepositoryRoot, "byte-drift", plan, cancellationToken).ConfigureAwait(false);

        var driftBytes = Utf8NoBom.GetBytes("candidate-replacement-v0.21-drifted-after-plan\n");
        await File.WriteAllBytesAsync(prepared.TrackedPath, driftBytes, cancellationToken).ConfigureAwait(false);
        var atAttempt = await ObserveGitStateAsync(prepared.Fixture.RepositoryRoot, cancellationToken).ConfigureAwait(false);
        var beforeArtifacts = CountRecoveryExecutionArtifacts(prepared.Fixture.RepositoryRoot);
        var (rejected, rejectionMessage) = await ExpectExecutionRefusalAsync(scenarioRoot, assessmentPath, assessment, planPath, plan, cancellationToken).ConfigureAwait(false);
        var afterArtifacts = CountRecoveryExecutionArtifacts(prepared.Fixture.RepositoryRoot);
        var post = await _assessmentService.AssessAsync(scenarioRoot, cancellationToken).ConfigureAwait(false);
        var finalState = await ObserveGitStateAsync(prepared.Fixture.RepositoryRoot, cancellationToken).ConfigureAwait(false);

        var authorityCreated = afterArtifacts.AuthorityCount > beforeArtifacts.AuthorityCount;
        var executionCreated = afterArtifacts.ExecutionCount > beforeArtifacts.ExecutionCount;
        var candidatePreserved =
            File.Exists(prepared.TrackedPath) && string.Equals(HashFile(prepared.TrackedPath), HashBytes(driftBytes), StringComparison.OrdinalIgnoreCase) &&
            File.Exists(prepared.AddedPath) && string.Equals(HashFile(prepared.AddedPath), prepared.AddedCandidateSha, StringComparison.OrdinalIgnoreCase);
        var headUnchanged = string.Equals(prepared.Fixture.AcceptedHead, finalState.Head, StringComparison.OrdinalIgnoreCase);
        var tagsUnchanged = prepared.Fixture.AcceptedTags.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(finalState.Tags.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);
        var expectedDirty = new[] { prepared.AddedRelative, prepared.TrackedRelative }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var passed =
            string.Equals(assessment.Classification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) && assessment.RecoveryRequired &&
            string.Equals(plan.Status, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) && plan.SeparateRecoveryAuthorityEligible && plan.AssessmentStillCurrent &&
            atAttempt.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(expectedDirty, StringComparer.Ordinal) &&
            rejected && rejectionMessage.Contains("byte-bound", StringComparison.OrdinalIgnoreCase) &&
            !authorityCreated && !executionCreated && candidatePreserved && headUnchanged && tagsUnchanged &&
            string.Equals(post.Classification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) && post.RecoveryRequired;

        return new RecoveryNegativeControlScenarioReceipt(
            "candidate-byte-drift-after-plan-refused",
            passed,
            prepared.Fixture.RepositoryRoot,
            prepared.Fixture.AcceptedHead,
            prepared.Fixture.AcceptedTags,
            assessment.Classification,
            plan.Status,
            plan.SeparateRecoveryAuthorityEligible,
            "replace one exact candidate file with different bytes after READY recovery plan while preserving dirty path set",
            atAttempt.DirtyPaths,
            true,
            rejected,
            rejectionMessage,
            authorityCreated,
            executionCreated,
            candidatePreserved,
            headUnchanged,
            tagsUnchanged,
            post.Classification,
            post.RecoveryRequired,
            assessmentPath,
            planPath);
    }

    private async Task<RecoveryNegativeControlScenarioReceipt> RunPathSetDriftControlAsync(
        string scenarioRoot,
        CancellationToken cancellationToken)
    {
        var prepared = await PrepareBoundedCandidateAsync(scenarioRoot, "path-drift", cancellationToken).ConfigureAwait(false);
        var assessment = await _assessmentService.AssessAsync(scenarioRoot, cancellationToken).ConfigureAwait(false);
        var assessmentPath = await WriteAssessmentAsync(prepared.Fixture.RepositoryRoot, "path-drift", assessment, cancellationToken).ConfigureAwait(false);
        var plan = await _planService.PlanAsync(scenarioRoot, assessmentPath, assessment, cancellationToken).ConfigureAwait(false);
        var planPath = await WritePlanAsync(prepared.Fixture.RepositoryRoot, "path-drift", plan, cancellationToken).ConfigureAwait(false);

        var rogueRelative = "fixture/rogue-after-plan.txt";
        var roguePath = Path.Combine(prepared.Fixture.RepositoryRoot, "fixture", "rogue-after-plan.txt");
        var rogueBytes = Utf8NoBom.GetBytes("rogue-after-plan-v0.21\n");
        await File.WriteAllBytesAsync(roguePath, rogueBytes, cancellationToken).ConfigureAwait(false);
        var atAttempt = await ObserveGitStateAsync(prepared.Fixture.RepositoryRoot, cancellationToken).ConfigureAwait(false);
        var beforeArtifacts = CountRecoveryExecutionArtifacts(prepared.Fixture.RepositoryRoot);
        var (rejected, rejectionMessage) = await ExpectExecutionRefusalAsync(scenarioRoot, assessmentPath, assessment, planPath, plan, cancellationToken).ConfigureAwait(false);
        var afterArtifacts = CountRecoveryExecutionArtifacts(prepared.Fixture.RepositoryRoot);
        var post = await _assessmentService.AssessAsync(scenarioRoot, cancellationToken).ConfigureAwait(false);
        var finalState = await ObserveGitStateAsync(prepared.Fixture.RepositoryRoot, cancellationToken).ConfigureAwait(false);

        var authorityCreated = afterArtifacts.AuthorityCount > beforeArtifacts.AuthorityCount;
        var executionCreated = afterArtifacts.ExecutionCount > beforeArtifacts.ExecutionCount;
        var candidatePreserved =
            File.Exists(prepared.TrackedPath) && string.Equals(HashFile(prepared.TrackedPath), prepared.TrackedCandidateSha, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(prepared.AddedPath) && string.Equals(HashFile(prepared.AddedPath), prepared.AddedCandidateSha, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(roguePath) && string.Equals(HashFile(roguePath), HashBytes(rogueBytes), StringComparison.OrdinalIgnoreCase);
        var headUnchanged = string.Equals(prepared.Fixture.AcceptedHead, finalState.Head, StringComparison.OrdinalIgnoreCase);
        var tagsUnchanged = prepared.Fixture.AcceptedTags.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(finalState.Tags.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);
        var expectedAttemptDirty = new[] { prepared.AddedRelative, rogueRelative, prepared.TrackedRelative }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var passed =
            string.Equals(assessment.Classification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) && assessment.RecoveryRequired &&
            string.Equals(plan.Status, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) && plan.SeparateRecoveryAuthorityEligible && plan.AssessmentStillCurrent &&
            atAttempt.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(expectedAttemptDirty, StringComparer.Ordinal) &&
            rejected && rejectionMessage.Contains("state changed", StringComparison.OrdinalIgnoreCase) &&
            !authorityCreated && !executionCreated && candidatePreserved && headUnchanged && tagsUnchanged &&
            string.Equals(post.Classification, "UNKNOWN_DIRTY_WORKTREE", StringComparison.Ordinal) && post.RecoveryRequired;

        return new RecoveryNegativeControlScenarioReceipt(
            "dirty-path-set-drift-after-plan-refused",
            passed,
            prepared.Fixture.RepositoryRoot,
            prepared.Fixture.AcceptedHead,
            prepared.Fixture.AcceptedTags,
            assessment.Classification,
            plan.Status,
            plan.SeparateRecoveryAuthorityEligible,
            "add one previously unassessed rogue dirty path after READY recovery plan",
            atAttempt.DirtyPaths,
            true,
            rejected,
            rejectionMessage,
            authorityCreated,
            executionCreated,
            candidatePreserved,
            headUnchanged,
            tagsUnchanged,
            post.Classification,
            post.RecoveryRequired,
            assessmentPath,
            planPath);
    }

    private async Task<(bool Rejected, string Message)> ExpectExecutionRefusalAsync(
        string scenarioRoot,
        string assessmentPath,
        MaintenanceRecoveryAssessmentReceipt assessment,
        string planPath,
        MaintenanceRecoveryPlanReceipt plan,
        CancellationToken cancellationToken)
    {
        try
        {
            await _executionService.ExecuteAsync(
                scenarioRoot,
                assessmentPath,
                assessment,
                planPath,
                plan,
                cancellationToken).ConfigureAwait(false);
            return (false, "Recovery execution unexpectedly completed.");
        }
        catch (InvalidDataException ex)
        {
            return (true, ex.Message);
        }
    }

    private async Task<PreparedBoundedCandidate> PrepareBoundedCandidateAsync(
        string scenarioRoot,
        string scenarioId,
        CancellationToken cancellationToken)
    {
        var fixture = await InitializeFixtureAsync(scenarioRoot, scenarioId, cancellationToken).ConfigureAwait(false);
        var trackedRelative = "fixture/tracked.txt";
        var addedRelative = "fixture/new.txt";
        var trackedPath = Path.Combine(fixture.RepositoryRoot, "fixture", "tracked.txt");
        var addedPath = Path.Combine(fixture.RepositoryRoot, "fixture", "new.txt");
        var trackedCandidateBytes = Utf8NoBom.GetBytes($"candidate-replacement-v0.21-{scenarioId}\n");
        var addedCandidateBytes = Utf8NoBom.GetBytes($"candidate-addition-v0.21-{scenarioId}\n");
        await File.WriteAllBytesAsync(trackedPath, trackedCandidateBytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(addedPath, addedCandidateBytes, cancellationToken).ConfigureAwait(false);

        var stagingRoot = Path.Combine(scenarioRoot, "staging");
        var stagingPayload = Path.Combine(stagingRoot, "payload", "fixture");
        Directory.CreateDirectory(stagingPayload);
        await File.WriteAllBytesAsync(Path.Combine(stagingPayload, "tracked.txt"), trackedCandidateBytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(stagingPayload, "new.txt"), addedCandidateBytes, cancellationToken).ConfigureAwait(false);

        var trackedCandidateSha = HashBytes(trackedCandidateBytes);
        var addedCandidateSha = HashBytes(addedCandidateBytes);
        var trackedAcceptedSha = HashBytes(Utf8NoBom.GetBytes("accepted-v0.21\n"));
        var sourceChanges = new[]
        {
            new WorkbenchStagedSourceChange(trackedRelative, "Replace", trackedAcceptedSha, trackedCandidateSha, trackedCandidateBytes.LongLength),
            new WorkbenchStagedSourceChange(addedRelative, "Add", null, addedCandidateSha, addedCandidateBytes.LongLength)
        };
        var stagedPlan = new WorkbenchStagedApplyPlanReceipt(
            Schema: "matawaka.workbench-staged-apply-plan-receipt/v0.14",
            Version: "0.14.0",
            ObservedAt: DateTimeOffset.Now,
            TargetVersion: $"0.21-negative-{scenarioId}",
            TargetTag: $"workbench-v0.21-negative-{scenarioId}-candidate",
            PredecessorTag: fixture.AcceptedTag,
            PredecessorCommit: fixture.AcceptedHead,
            CurrentHead: fixture.AcceptedHead,
            StagingRoot: stagingRoot,
            MaterializationReceiptEligible: true,
            PredecessorReverified: true,
            WorkingTreeClean: true,
            StagingRootBounded: true,
            ExactStagedFileSetVerified: true,
            StagedPayloadDigestsVerified: true,
            SourceChanges: sourceChanges,
            AddCount: 1,
            ReplaceCount: 1,
            NoOpCount: 0,
            SourceMutationAuthorized: false,
            BuildAuthorized: false,
            CheckpointAuthorized: false,
            Status: "READY_FOR_SEPARATE_SOURCE_APPLY_AUTHORITY",
            NonEffects: new[] { "negative-control fixture evidence only", "no main repository authority" },
            Note: "Synthetic isolated negative-control staged plan used only to test recovery refusal boundaries.");

        var applyPlanDir = Path.Combine(fixture.RepositoryRoot, "artifacts", "update-apply-plans");
        Directory.CreateDirectory(applyPlanDir);
        var stagedPlanPath = Path.Combine(applyPlanDir, $"staged-apply-plan-v0.21-negative-{scenarioId}.json");
        await File.WriteAllTextAsync(stagedPlanPath, JsonSerializer.Serialize(stagedPlan, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);

        var applyBuildDir = Path.Combine(fixture.RepositoryRoot, "artifacts", "update-applies");
        Directory.CreateDirectory(applyBuildDir);
        var applyBuildPath = Path.Combine(applyBuildDir, $"apply-build-v0.21-negative-{scenarioId}.json");
        await File.WriteAllTextAsync(
            applyBuildPath,
            JsonSerializer.Serialize(new
            {
                Schema = "matawaka.workbench-negative-control-interrupted-candidate/v0.21",
                Status = "INTERRUPTED_UPDATE_CANDIDATE",
                TargetVersion = $"0.21-negative-{scenarioId}",
                SourceChanges = sourceChanges
            }, JsonOptions),
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);

        return new PreparedBoundedCandidate(
            fixture,
            trackedRelative,
            addedRelative,
            trackedPath,
            addedPath,
            trackedCandidateSha,
            addedCandidateSha,
            stagedPlanPath,
            applyBuildPath);
    }

    private static async Task<FixtureContext> InitializeFixtureAsync(
        string scenarioRoot,
        string scenarioId,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = Path.Combine(scenarioRoot, "Workbench");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "fixture"));
        await RunGitAsync(repositoryRoot, cancellationToken, true, "init").ConfigureAwait(false);
        await RunGitAsync(repositoryRoot, cancellationToken, true, "config", "user.name", "Matawaka Recovery Negative Control").ConfigureAwait(false);
        await RunGitAsync(repositoryRoot, cancellationToken, true, "config", "user.email", "recovery-negative-control@local.invalid").ConfigureAwait(false);
        await RunGitAsync(repositoryRoot, cancellationToken, true, "config", "core.autocrlf", "false").ConfigureAwait(false);
        await RunGitAsync(repositoryRoot, cancellationToken, true, "config", "commit.gpgsign", "false").ConfigureAwait(false);
        await RunGitAsync(repositoryRoot, cancellationToken, true, "config", "tag.gpgsign", "false").ConfigureAwait(false);

        await File.WriteAllTextAsync(Path.Combine(repositoryRoot, ".gitignore"), "artifacts/\n.workbench/\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "fixture", "tracked.txt"), "accepted-v0.21\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);
        await RunGitAsync(repositoryRoot, cancellationToken, true, "add", "--", ".gitignore", "fixture/tracked.txt").ConfigureAwait(false);
        await RunGitAsync(repositoryRoot, cancellationToken, true, "commit", "-m", $"Accepted recovery negative-control fixture {scenarioId}").ConfigureAwait(false);
        var acceptedTag = $"workbench-v0.21-negative-{scenarioId}-accepted";
        await RunGitAsync(repositoryRoot, cancellationToken, true, "tag", "-a", acceptedTag, "-m", $"Accepted recovery negative-control fixture {scenarioId}").ConfigureAwait(false);

        var acceptedHead = (await RunGitAsync(repositoryRoot, cancellationToken, false, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var acceptedTags = SplitLines(await RunGitAsync(repositoryRoot, cancellationToken, false, "tag", "--points-at", "HEAD").ConfigureAwait(false));
        return new FixtureContext(repositoryRoot, acceptedHead, acceptedTag, acceptedTags);
    }

    private static async Task<string> WriteAssessmentAsync(
        string repositoryRoot,
        string scenarioId,
        MaintenanceRecoveryAssessmentReceipt assessment,
        CancellationToken cancellationToken)
    {
        var dir = Path.Combine(repositoryRoot, "artifacts", "recovery-assessments");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"recovery-assessment-v0.21-negative-{scenarioId}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(assessment, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static async Task<string> WritePlanAsync(
        string repositoryRoot,
        string scenarioId,
        MaintenanceRecoveryPlanReceipt plan,
        CancellationToken cancellationToken)
    {
        var dir = Path.Combine(repositoryRoot, "artifacts", "recovery-plans");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"recovery-plan-v0.21-negative-{scenarioId}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static (int AuthorityCount, int ExecutionCount) CountRecoveryExecutionArtifacts(string repositoryRoot)
    {
        var dir = Path.Combine(repositoryRoot, "artifacts", "recovery-executions");
        if (!Directory.Exists(dir)) return (0, 0);
        var authority = Directory.GetFiles(dir, "recovery-execution-authority-*.json", SearchOption.TopDirectoryOnly).Length;
        var execution = Directory.GetFiles(dir, "recovery-execution-v*.json", SearchOption.TopDirectoryOnly).Length;
        return (authority, execution);
    }

    private static string ResolveMainRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository not found: {root}");
        return root;
    }

    private sealed record GitState(string Head, IReadOnlyList<string> Tags, IReadOnlyList<string> DirtyPaths);
    private sealed record FixtureContext(string RepositoryRoot, string AcceptedHead, string AcceptedTag, IReadOnlyList<string> AcceptedTags);
    private sealed record PreparedBoundedCandidate(
        FixtureContext Fixture,
        string TrackedRelative,
        string AddedRelative,
        string TrackedPath,
        string AddedPath,
        string TrackedCandidateSha,
        string AddedCandidateSha,
        string StagedPlanPath,
        string ApplyBuildPath);

    private static async Task<GitState> ObserveGitStateAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var head = (await RunGitAsync(repositoryRoot, cancellationToken, false, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var tags = SplitLines(await RunGitAsync(repositoryRoot, cancellationToken, false, "tag", "--points-at", "HEAD").ConfigureAwait(false));
        var status = await RunGitAsync(repositoryRoot, cancellationToken, false, "status", "--porcelain=v1", "--untracked-files=all").ConfigureAwait(false);
        return new GitState(head, tags, ParseStatusPaths(status));
    }

    private static bool StatesEqual(GitState left, GitState right)
        => string.Equals(left.Head, right.Head, StringComparison.OrdinalIgnoreCase) &&
           left.Tags.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(right.Tags.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal) &&
           left.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(right.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);

    private static IReadOnlyList<string> ParseStatusPaths(string output)
    {
        var paths = new List<string>();
        foreach (var raw in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4) throw new InvalidDataException($"Unexpected git status porcelain line in recovery negative-control matrix: {raw}");
            var path = raw[3..];
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) path = path[(arrow + 4)..];
            paths.Add(path.Trim('"').Replace('\\', '/').TrimStart('/'));
        }
        return paths.OrderBy(x => x, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> SplitLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static async Task<string> RunGitAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        bool fixtureMutation,
        params string[] args)
    {
        if (args.Length == 0) throw new InvalidDataException("Git command is required.");
        var readOnly = new HashSet<string>(StringComparer.Ordinal) { "rev-parse", "tag", "status" };
        var fixtureSetup = new HashSet<string>(StringComparer.Ordinal) { "init", "config", "add", "commit", "tag" };
        if (fixtureMutation)
        {
            if (!fixtureSetup.Contains(args[0])) throw new InvalidDataException($"Non-allowlisted fixture Git mutation: {args[0]}");
        }
        else if (!readOnly.Contains(args[0]))
        {
            throw new InvalidDataException($"Non-allowlisted read-only Git operation: {args[0]}");
        }

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
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed Git recovery-negative-control process.");
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
            throw new InvalidDataException("Fixed Git recovery-negative-control process timed out after 20 seconds.");
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed Git recovery-negative-control operation failed ({args[0]}), exit={process.ExitCode}: {stderr.Trim()}");
        return stdout;
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
