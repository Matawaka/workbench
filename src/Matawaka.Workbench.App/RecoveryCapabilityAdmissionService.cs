using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RecoveryCapabilityAdmissionReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    bool Admitted,
    string Status,
    string MainRepositoryRoot,
    string CurrentHead,
    IReadOnlyList<string> CurrentTags,
    bool WorkingTreeClean,
    string EvidenceArtifactPath,
    string EvidenceArtifactSha256,
    string EvidenceSchema,
    string EvidenceVersion,
    DateTimeOffset EvidenceObservedAt,
    string EvidenceMainHead,
    IReadOnlyList<string> EvidenceMainTags,
    string PreRecoveryClassification,
    string RecoveryPlanStatus,
    string RecoveryExecutionStatus,
    string PostRecoveryClassification,
    bool PostRecoveryWorkingTreeClean,
    bool TrackedFileRestored,
    bool UntrackedAdditionRemoved,
    bool FixtureHeadUnchanged,
    bool FixtureTagsUnchanged,
    bool MainRepositoryUnchangedInDrill,
    bool ExplicitUiConfirmationObservedInDrill,
    bool DrillMainRepositoryMutationAllowed,
    bool DrillBuildAllowed,
    bool DrillCheckpointAllowed,
    bool DrillNetworkAccessAllowed,
    bool DrillCatalogMutationAllowed,
    bool DrillAgentExecuteAllowed,
    bool BoundedRecoveryCapabilityAdmitted,
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
    IReadOnlyList<string> AdmittedScope,
    IReadOnlyList<string> EvidenceLimitations,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Read-only admission surface over retained isolated recovery evidence.
/// It does not execute recovery and does not broaden the authority proven by
/// the drill. A positive result is deliberately narrower than a general
/// recovery claim or Stable Core admission.
/// </summary>
public sealed class RecoveryCapabilityAdmissionService
{
    public const string Version = "0.20.0";
    public const string ReceiptSchema = "matawaka.workbench-recovery-capability-admission/v0.20";
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<(RecoveryCapabilityAdmissionReceipt Receipt, string ArtifactPath)> AssessAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var currentHead = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var currentTags = SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD").ConfigureAwait(false));
        var status = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all").ConfigureAwait(false);
        var dirtyPaths = ParseStatusPaths(status);

        if (dirtyPaths.Count != 0)
            throw new InvalidDataException("Recovery capability admission requires a clean accepted main Workbench repository.");
        if (!currentTags.Contains("workbench-v0.20-accepted", StringComparer.Ordinal))
            throw new InvalidDataException("Recovery capability admission is enabled only after workbench-v0.20-accepted points at the current HEAD.");

        var evidencePath = FindLatestPassingDrillArtifact(repositoryRoot);
        var evidenceSha = HashFile(evidencePath);
        var drill = JsonSerializer.Deserialize<IsolatedRecoveryDrillReceipt>(File.ReadAllText(evidencePath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("Retained isolated recovery drill artifact could not be parsed.");

        var evidenceMainTags = drill.MainTagsAfter.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var evidenceLimitations = new[]
        {
            "evidence covers one isolated tracked Replace plus one isolated untracked Add recovery shape",
            "the drill ran against a nested fixture, not the production main Workbench repository",
            "the drill does not prove recovery from every interruption, corruption, process, filesystem, or Git failure mode",
            "the drill does not prove automatic recovery safety",
            "admission does not establish canonical UU-AAP conformance",
            "admission does not promote recovery interfaces to Stable Core or the interface registry"
        };
        var admittedScope = new[]
        {
            "detect one exact byte-bound interrupted update candidate",
            "plan recovery only when dirty paths remain bounded to retained update evidence",
            "restore an exact tracked candidate path from the current accepted HEAD after re-verification",
            "remove an exact byte-reverified untracked candidate addition",
            "require the recovered fixture to converge to the same accepted HEAD/tag and a clean fresh assessment"
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
            "no Stable Core or interface-registry promotion",
            "admission artifact write is limited to Workbench/artifacts/recovery-admissions"
        };

        var passed =
            string.Equals(drill.Schema, "matawaka.workbench-isolated-recovery-drill/v0.19", StringComparison.Ordinal) &&
            string.Equals(drill.Version, "0.19.0", StringComparison.Ordinal) &&
            drill.Passed &&
            string.Equals(Path.GetFullPath(drill.MainRepositoryRoot), repositoryRoot, StringComparison.OrdinalIgnoreCase) &&
            drill.MainRepositoryUnchanged &&
            string.Equals(drill.MainHeadBefore, drill.MainHeadAfter, StringComparison.OrdinalIgnoreCase) &&
            drill.MainDirtyPathsBefore.Count == 0 &&
            drill.MainDirtyPathsAfter.Count == 0 &&
            drill.MainTagsBefore.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(evidenceMainTags, StringComparer.Ordinal) &&
            drill.MainTagsAfter.Contains("workbench-v0.19-accepted", StringComparer.Ordinal) &&
            drill.CandidateDirtyPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "fixture/new.txt", "fixture/tracked.txt" }, StringComparer.Ordinal) &&
            string.Equals(drill.PreRecoveryClassification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) &&
            string.Equals(drill.RecoveryPlanStatus, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) &&
            string.Equals(drill.RecoveryExecutionStatus, "RECOVERED_TO_CURRENT_ACCEPTED_HEAD_FRESH_ASSESSMENT_REQUIRED", StringComparison.Ordinal) &&
            string.Equals(drill.PostRecoveryClassification, "CLEAN_ACCEPTED", StringComparison.Ordinal) &&
            drill.PostRecoveryWorkingTreeClean &&
            drill.TrackedFileRestored &&
            drill.UntrackedAdditionRemoved &&
            drill.FixtureHeadUnchanged &&
            drill.FixtureTagsUnchanged &&
            drill.Authority.ExplicitUiConfirmationRequired &&
            !drill.Authority.MainRepositoryMutationAllowed &&
            drill.Authority.FixtureGitInitializationAllowed &&
            drill.Authority.FixtureCandidateMutationAllowed &&
            drill.Authority.FixtureRecoveryExecutionAllowed &&
            !drill.Authority.BuildAllowed &&
            !drill.Authority.CheckpointAllowed &&
            !drill.Authority.NetworkAccessAllowed &&
            !drill.Authority.CatalogMutationAllowed &&
            !drill.Authority.AgentExecuteAllowed;

        var receipt = new RecoveryCapabilityAdmissionReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            passed,
            passed ? "ADMITTED_ISOLATED_BOUNDED_RECOVERY_CAPABILITY" : "NO_ADMISSION_EVIDENCE_INCOMPLETE",
            repositoryRoot,
            currentHead,
            currentTags,
            true,
            evidencePath,
            evidenceSha,
            drill.Schema,
            drill.Version,
            drill.ObservedAt,
            drill.MainHeadAfter,
            evidenceMainTags,
            drill.PreRecoveryClassification,
            drill.RecoveryPlanStatus,
            drill.RecoveryExecutionStatus,
            drill.PostRecoveryClassification,
            drill.PostRecoveryWorkingTreeClean,
            drill.TrackedFileRestored,
            drill.UntrackedAdditionRemoved,
            drill.FixtureHeadUnchanged,
            drill.FixtureTagsUnchanged,
            drill.MainRepositoryUnchanged,
            drill.Authority.ExplicitUiConfirmationRequired,
            drill.Authority.MainRepositoryMutationAllowed,
            drill.Authority.BuildAllowed,
            drill.Authority.CheckpointAllowed,
            drill.Authority.NetworkAccessAllowed,
            drill.Authority.CatalogMutationAllowed,
            drill.Authority.AgentExecuteAllowed,
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
            admittedScope,
            evidenceLimitations,
            nonEffects,
            "A positive v0.20 admission records that retained v0.19 isolated evidence supports one bounded Workbench recovery capability. Evidence admission is not recovery execution authority, is not proof of production-main-repository recovery or every failure mode, and does not authorize automatic recovery, deletion, build, checkpoint, publication, network access, catalog mutation, Agent Execute, or Stable Core promotion.");

        var artifactDir = Path.Combine(repositoryRoot, "artifacts", "recovery-admissions");
        Directory.CreateDirectory(artifactDir);
        var artifactPath = Path.Combine(artifactDir, $"recovery-admission-v0.20-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(receipt, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return (receipt, artifactPath);
    }

    private static string FindLatestPassingDrillArtifact(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "recovery-drills");
        if (!Directory.Exists(root))
            throw new InvalidDataException("No retained recovery-drill evidence directory exists.");

        foreach (var file in Directory.GetFiles(root, "isolated-recovery-drill-v0.19-*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var full = Path.GetFullPath(file);
                var allowedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)) continue;
                var drill = JsonSerializer.Deserialize<IsolatedRecoveryDrillReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions);
                if (drill is not null && drill.Passed && string.Equals(drill.Schema, "matawaka.workbench-isolated-recovery-drill/v0.19", StringComparison.Ordinal))
                    return full;
            }
            catch
            {
                // Unreadable evidence cannot support admission; continue to older retained evidence.
            }
        }

        throw new InvalidDataException("No passing retained v0.19 isolated recovery drill artifact is available for admission.");
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
            if (raw.Length < 4) throw new InvalidDataException($"Unexpected git status porcelain line in recovery admission: {raw}");
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

    private static async Task<string> RunGitReadOnlyAsync(string repositoryRoot, CancellationToken cancellationToken, params string[] args)
    {
        if (args.Length == 0) throw new InvalidDataException("Git command is required.");
        if (!new[] { "rev-parse", "tag", "status" }.Contains(args[0], StringComparer.Ordinal))
            throw new InvalidDataException($"Non-allowlisted read-only Git operation in recovery admission: {args[0]}");

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
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only Git recovery-admission process.");
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
            throw new TimeoutException($"Read-only Git recovery-admission operation timed out after {GitTimeout.TotalSeconds:0} seconds.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Read-only Git recovery-admission operation failed: {string.Join(' ', args)} :: {stderr.Trim()}");
        return stdout;
    }

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
