using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record IsolatedRecoveryDrillAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string MainRepositoryRoot,
    string FixtureRoot,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool MainRepositoryMutationAllowed,
    bool FixtureGitInitializationAllowed,
    bool FixtureCandidateMutationAllowed,
    bool FixtureRecoveryExecutionAllowed,
    bool BuildAllowed,
    bool CheckpointAllowed,
    bool NetworkAccessAllowed,
    bool CatalogMutationAllowed,
    bool AgentExecuteAllowed,
    IReadOnlyList<string> AllowedEffects,
    IReadOnlyList<string> NonEffects);

public sealed record IsolatedRecoveryDrillReceipt(
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
    string DrillRoot,
    string FixtureRepositoryRoot,
    string FixtureAcceptedHead,
    IReadOnlyList<string> FixtureAcceptedTags,
    IReadOnlyList<string> CandidateDirtyPaths,
    string PreRecoveryClassification,
    string RecoveryPlanStatus,
    string RecoveryExecutionStatus,
    string PostRecoveryClassification,
    bool PostRecoveryWorkingTreeClean,
    bool TrackedFileRestored,
    bool UntrackedAdditionRemoved,
    bool FixtureHeadUnchanged,
    bool FixtureTagsUnchanged,
    IsolatedRecoveryDrillAuthorityReceipt Authority,
    string AssessmentArtifactPath,
    string PlanArtifactPath,
    string ExecutionArtifactPath,
    string ExecutionAuthorityArtifactPath,
    string PostRecoveryAssessmentArtifactPath,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Human-confirmed isolated drill for the already-accepted bounded recovery
/// services. The drill never dirties the main Workbench repository. It creates
/// a nested local Git fixture below .workbench/recovery-drills, commits a tiny
/// accepted fixture state, creates one exact interrupted candidate (tracked
/// Replace + untracked Add) plus byte-bound local maintenance evidence, then
/// runs the real recovery assessment, plan and execution services against the
/// fixture. The fixture and receipts are retained as evidence.
/// </summary>
public sealed class IsolatedRecoveryDrillService
{
    public const string Version = "0.19.0";
    public const string ReceiptSchema = "matawaka.workbench-isolated-recovery-drill/v0.19";
    public const string AuthoritySchema = "matawaka.workbench-isolated-recovery-drill-authority/v0.19";

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

    public async Task<(IsolatedRecoveryDrillReceipt Receipt, string ArtifactPath)> RunAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var mainRepositoryRoot = ResolveMainRepositoryRoot(workspaceRoot);
        var mainBefore = await ObserveGitStateAsync(mainRepositoryRoot, cancellationToken).ConfigureAwait(false);
        if (mainBefore.DirtyPaths.Count != 0)
            throw new InvalidDataException("Isolated recovery drill requires a clean accepted main Workbench repository. It will not run against a dirty main source tree.");
        if (!mainBefore.Tags.Contains("workbench-v0.19-accepted", StringComparer.Ordinal))
            throw new InvalidDataException("Isolated recovery drill is enabled only after workbench-v0.19-accepted points at the current main HEAD.");

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var drillRoot = Path.Combine(mainRepositoryRoot, ".workbench", "recovery-drills", $"v0.19-{stamp}");
        var fixtureWorkspaceRoot = drillRoot;
        var fixtureRepositoryRoot = Path.Combine(fixtureWorkspaceRoot, "Workbench");
        Directory.CreateDirectory(fixtureRepositoryRoot);

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
            "no cleanup or deletion of retained drill evidence",
            "fixture Git mutation is bounded to the nested .workbench/recovery-drills repository"
        };

        var authority = new IsolatedRecoveryDrillAuthorityReceipt(
            AuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.maintenance.isolated-recovery-drill",
            mainRepositoryRoot,
            drillRoot,
            "explicit Recovery drill button + confirmation dialog after v0.19 accepted",
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
            new[]
            {
                "create and retain a nested local Git fixture under .workbench/recovery-drills",
                "commit/tag only the tiny fixture accepted state using fixed Git arguments",
                "create exact candidate bytes only in the fixture repository",
                "run the existing recovery assessment/plan/execution services only against the fixture",
                "write drill evidence under the fixture and main Workbench artifacts/recovery-drills"
            },
            nonEffects);

        await InitializeFixtureAsync(fixtureRepositoryRoot, cancellationToken).ConfigureAwait(false);
        var fixtureAcceptedHead = (await RunGitAsync(fixtureRepositoryRoot, cancellationToken, false, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var fixtureAcceptedTags = SplitLines(await RunGitAsync(fixtureRepositoryRoot, cancellationToken, false, "tag", "--points-at", "HEAD").ConfigureAwait(false));

        var trackedRelative = "fixture/tracked.txt";
        var addedRelative = "fixture/new.txt";
        var trackedPath = Path.Combine(fixtureRepositoryRoot, "fixture", "tracked.txt");
        var addedPath = Path.Combine(fixtureRepositoryRoot, "fixture", "new.txt");
        var trackedCandidateBytes = Utf8NoBom.GetBytes("candidate-replacement-v0.19\n");
        var addedCandidateBytes = Utf8NoBom.GetBytes("candidate-addition-v0.19\n");
        await File.WriteAllBytesAsync(trackedPath, trackedCandidateBytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(addedPath, addedCandidateBytes, cancellationToken).ConfigureAwait(false);

        var stagingRoot = Path.Combine(drillRoot, "staging");
        var stagingPayload = Path.Combine(stagingRoot, "payload", "fixture");
        Directory.CreateDirectory(stagingPayload);
        await File.WriteAllBytesAsync(Path.Combine(stagingPayload, "tracked.txt"), trackedCandidateBytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(stagingPayload, "new.txt"), addedCandidateBytes, cancellationToken).ConfigureAwait(false);

        var trackedCandidateSha = HashBytes(trackedCandidateBytes);
        var addedCandidateSha = HashBytes(addedCandidateBytes);
        var trackedAcceptedSha = HashBytes(Utf8NoBom.GetBytes("accepted-v0.19\n"));

        var sourceChanges = new[]
        {
            new WorkbenchStagedSourceChange(trackedRelative, "Replace", trackedAcceptedSha, trackedCandidateSha, trackedCandidateBytes.LongLength),
            new WorkbenchStagedSourceChange(addedRelative, "Add", null, addedCandidateSha, addedCandidateBytes.LongLength)
        };
        var stagedPlan = new WorkbenchStagedApplyPlanReceipt(
            Schema: "matawaka.workbench-staged-apply-plan-receipt/v0.14",
            Version: "0.14.0",
            ObservedAt: DateTimeOffset.Now,
            TargetVersion: "0.19-recovery-drill-candidate",
            TargetTag: "workbench-v0.19-drill-candidate",
            PredecessorTag: "workbench-v0.19-drill-accepted",
            PredecessorCommit: fixtureAcceptedHead,
            CurrentHead: fixtureAcceptedHead,
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
            NonEffects: new[] { "fixture evidence only", "no main repository authority" },
            Note: "Synthetic isolated fixture plan used only to exercise bounded recovery execution.");

        var applyPlanDir = Path.Combine(fixtureRepositoryRoot, "artifacts", "update-apply-plans");
        Directory.CreateDirectory(applyPlanDir);
        var stagedPlanPath = Path.Combine(applyPlanDir, $"staged-apply-plan-v0.19-drill-{stamp}.json");
        await File.WriteAllTextAsync(stagedPlanPath, JsonSerializer.Serialize(stagedPlan, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);

        var applyBuildDir = Path.Combine(fixtureRepositoryRoot, "artifacts", "update-applies");
        Directory.CreateDirectory(applyBuildDir);
        var applyBuildPath = Path.Combine(applyBuildDir, $"apply-build-v0.19-drill-{stamp}.json");
        await File.WriteAllTextAsync(
            applyBuildPath,
            JsonSerializer.Serialize(new
            {
                Schema = "matawaka.workbench-isolated-drill-interrupted-candidate/v0.19",
                Status = "INTERRUPTED_UPDATE_CANDIDATE",
                TargetVersion = "0.19-recovery-drill-candidate",
                SourceChanges = sourceChanges
            }, JsonOptions),
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);

        var candidateState = await ObserveGitStateAsync(fixtureRepositoryRoot, cancellationToken).ConfigureAwait(false);
        var candidateDirtyPaths = candidateState.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var expectedCandidateDirty = new[] { addedRelative, trackedRelative }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!candidateDirtyPaths.SequenceEqual(expectedCandidateDirty, StringComparer.Ordinal))
            throw new InvalidDataException($"Isolated drill candidate dirty set is not exact. expected={string.Join(';', expectedCandidateDirty)} actual={string.Join(';', candidateDirtyPaths)}");

        var assessment = await _assessmentService.AssessAsync(fixtureWorkspaceRoot, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(assessment.Classification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) || !assessment.RecoveryRequired)
            throw new InvalidDataException($"Isolated drill did not reach BOUNDED_DIRTY_UPDATE_CANDIDATE: {assessment.Classification}");
        var assessmentDir = Path.Combine(fixtureRepositoryRoot, "artifacts", "recovery-assessments");
        Directory.CreateDirectory(assessmentDir);
        var assessmentPath = Path.Combine(assessmentDir, $"recovery-assessment-v0.19-drill-{stamp}.json");
        await File.WriteAllTextAsync(assessmentPath, JsonSerializer.Serialize(assessment, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);

        var plan = await _planService.PlanAsync(fixtureWorkspaceRoot, assessmentPath, assessment, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(plan.Status, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) || !plan.SeparateRecoveryAuthorityEligible)
            throw new InvalidDataException($"Isolated drill recovery plan is not eligible: {plan.Status}");
        var planDir = Path.Combine(fixtureRepositoryRoot, "artifacts", "recovery-plans");
        Directory.CreateDirectory(planDir);
        var planPath = Path.Combine(planDir, $"recovery-plan-v0.19-drill-{stamp}.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);

        var execution = await _executionService.ExecuteAsync(
            fixtureWorkspaceRoot,
            assessmentPath,
            assessment,
            planPath,
            plan,
            cancellationToken).ConfigureAwait(false);

        var postAssessment = await _assessmentService.AssessAsync(fixtureWorkspaceRoot, cancellationToken).ConfigureAwait(false);
        var postAssessmentPath = Path.Combine(assessmentDir, $"recovery-assessment-post-v0.19-drill-{stamp}.json");
        await File.WriteAllTextAsync(postAssessmentPath, JsonSerializer.Serialize(postAssessment, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);

        var fixtureAfter = await ObserveGitStateAsync(fixtureRepositoryRoot, cancellationToken).ConfigureAwait(false);
        var trackedRestored = File.Exists(trackedPath) && string.Equals(HashFile(trackedPath), trackedAcceptedSha, StringComparison.OrdinalIgnoreCase);
        var untrackedRemoved = !File.Exists(addedPath);
        var fixtureHeadUnchanged = string.Equals(fixtureAcceptedHead, fixtureAfter.Head, StringComparison.OrdinalIgnoreCase);
        var fixtureTagsUnchanged = fixtureAcceptedTags.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(fixtureAfter.Tags.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);

        var mainAfter = await ObserveGitStateAsync(mainRepositoryRoot, cancellationToken).ConfigureAwait(false);
        var mainUnchanged = string.Equals(mainBefore.Head, mainAfter.Head, StringComparison.OrdinalIgnoreCase) &&
            mainBefore.Tags.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(mainAfter.Tags.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal) &&
            mainBefore.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(mainAfter.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);

        var passed =
            string.Equals(execution.Receipt.Status, "RECOVERED_TO_CURRENT_ACCEPTED_HEAD_FRESH_ASSESSMENT_REQUIRED", StringComparison.Ordinal) &&
            execution.Receipt.WorkingTreeCleanAfterRecovery &&
            string.Equals(postAssessment.Classification, "CLEAN_ACCEPTED", StringComparison.Ordinal) &&
            postAssessment.WorkingTreeClean &&
            trackedRestored &&
            untrackedRemoved &&
            fixtureHeadUnchanged &&
            fixtureTagsUnchanged &&
            mainUnchanged;

        var receipt = new IsolatedRecoveryDrillReceipt(
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
            drillRoot,
            fixtureRepositoryRoot,
            fixtureAcceptedHead,
            fixtureAcceptedTags,
            candidateDirtyPaths,
            assessment.Classification,
            plan.Status,
            execution.Receipt.Status,
            postAssessment.Classification,
            postAssessment.WorkingTreeClean,
            trackedRestored,
            untrackedRemoved,
            fixtureHeadUnchanged,
            fixtureTagsUnchanged,
            authority,
            assessmentPath,
            planPath,
            execution.ArtifactPath,
            execution.AuthorityPath,
            postAssessmentPath,
            nonEffects,
            "The isolated drill proves bounded recovery behavior against a nested fixture only. It does not prove recovery from every failure mode and it does not create recovery, deletion, build, checkpoint, network, catalog, or Agent Execute authority over the main Workbench repository.");

        var mainArtifactDir = Path.Combine(mainRepositoryRoot, "artifacts", "recovery-drills");
        Directory.CreateDirectory(mainArtifactDir);
        var artifactPath = Path.Combine(mainArtifactDir, $"isolated-recovery-drill-v0.19-{stamp}.json");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(receipt, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return (receipt, artifactPath);
    }

    private static async Task InitializeFixtureAsync(string fixtureRepositoryRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(fixtureRepositoryRoot, "fixture"));
        await RunGitAsync(fixtureRepositoryRoot, cancellationToken, true, "init").ConfigureAwait(false);
        await RunGitAsync(fixtureRepositoryRoot, cancellationToken, true, "config", "user.name", "Matawaka Recovery Drill").ConfigureAwait(false);
        await RunGitAsync(fixtureRepositoryRoot, cancellationToken, true, "config", "user.email", "recovery-drill@local.invalid").ConfigureAwait(false);
        await RunGitAsync(fixtureRepositoryRoot, cancellationToken, true, "config", "core.autocrlf", "false").ConfigureAwait(false);
        await RunGitAsync(fixtureRepositoryRoot, cancellationToken, true, "config", "commit.gpgsign", "false").ConfigureAwait(false);
        await RunGitAsync(fixtureRepositoryRoot, cancellationToken, true, "config", "tag.gpgsign", "false").ConfigureAwait(false);

        await File.WriteAllTextAsync(Path.Combine(fixtureRepositoryRoot, ".gitignore"), "artifacts/\n.workbench/\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(fixtureRepositoryRoot, "fixture", "tracked.txt"), "accepted-v0.19\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);
        await RunGitAsync(fixtureRepositoryRoot, cancellationToken, true, "add", "--", ".gitignore", "fixture/tracked.txt").ConfigureAwait(false);
        await RunGitAsync(fixtureRepositoryRoot, cancellationToken, true, "commit", "-m", "Accepted isolated recovery drill fixture").ConfigureAwait(false);
        await RunGitAsync(fixtureRepositoryRoot, cancellationToken, true, "tag", "-a", "workbench-v0.19-drill-accepted", "-m", "Accepted isolated recovery drill fixture").ConfigureAwait(false);
    }

    private static string ResolveMainRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository not found: {root}");
        return root;
    }

    private sealed record GitState(string Head, IReadOnlyList<string> Tags, IReadOnlyList<string> DirtyPaths);

    private static async Task<GitState> ObserveGitStateAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var head = (await RunGitAsync(repositoryRoot, cancellationToken, false, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var tags = SplitLines(await RunGitAsync(repositoryRoot, cancellationToken, false, "tag", "--points-at", "HEAD").ConfigureAwait(false));
        var status = await RunGitAsync(repositoryRoot, cancellationToken, false, "status", "--porcelain=v1", "--untracked-files=all").ConfigureAwait(false);
        return new GitState(head, tags, ParseStatusPaths(status));
    }

    private static IReadOnlyList<string> ParseStatusPaths(string output)
    {
        var paths = new List<string>();
        foreach (var raw in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4) throw new InvalidDataException($"Unexpected git status porcelain line in recovery drill: {raw}");
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
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed Git recovery-drill process.");
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
            throw new InvalidDataException("Fixed Git recovery-drill process timed out after 20 seconds.");
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed Git recovery-drill operation failed ({args[0]}), exit={process.ExitCode}: {stderr.Trim()}");
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
