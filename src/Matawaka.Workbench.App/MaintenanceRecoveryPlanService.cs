using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record MaintenanceRecoveryPlanStep(
    int Sequence,
    string Operation,
    string Target,
    string Basis,
    bool Mutating,
    bool Destructive,
    bool RequiresSeparateAuthority);

public sealed record MaintenanceRecoveryPlanReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RepositoryRoot,
    string AssessmentArtifactPath,
    string AssessmentArtifactSha256,
    string AssessmentSchema,
    DateTimeOffset AssessmentObservedAt,
    string AssessmentClassification,
    string AssessmentHead,
    IReadOnlyList<string> AssessmentTags,
    IReadOnlyList<string> AssessmentDirtyPaths,
    string ReverifiedHead,
    IReadOnlyList<string> ReverifiedTags,
    IReadOnlyList<string> ReverifiedDirtyPaths,
    bool AssessmentArtifactVerified,
    bool RepositoryStateReverified,
    bool AssessmentStillCurrent,
    bool RecoveryRequired,
    bool SeparateRecoveryAuthorityEligible,
    string Status,
    IReadOnlyList<MaintenanceRecoveryPlanStep> ProposedSteps,
    IReadOnlyList<string> EvidenceRootsRetained,
    bool RecoveryExecutionAuthorized,
    bool RollbackAuthorized,
    bool DeletionAuthorized,
    bool SourceMutationAuthorized,
    bool BuildAuthorized,
    bool CheckpointAuthorized,
    bool NetworkAccessAuthorized,
    bool CatalogMutationAuthorized,
    bool AgentExecuteAuthorized,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Read-only recovery planning over a fresh recovery assessment. A plan can
/// describe a later bounded recovery transition, but it cannot execute it.
/// Observation, planning, and recovery authority remain separate gates.
/// </summary>
public sealed class MaintenanceRecoveryPlanService
{
    public const string Version = "0.17.0";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<MaintenanceRecoveryPlanReceipt> PlanAsync(
        string workspaceRoot,
        string assessmentArtifactPath,
        MaintenanceRecoveryAssessmentReceipt assessment,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var validatedAssessmentPath = ValidateAssessmentArtifact(repositoryRoot, assessmentArtifactPath, assessment);
        var assessmentSha256 = HashFile(validatedAssessmentPath);

        var currentHead = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Trim();
        var currentTags = SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var statusOutput = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        var statusEntries = ParseStatusEntries(statusOutput);
        var dirtyPaths = statusEntries.Select(x => x.Path).OrderBy(x => x, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray();

        var assessmentTags = assessment.CurrentTags.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var assessmentDirty = assessment.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var stateMatches =
            string.Equals(currentHead, assessment.CurrentHead, StringComparison.OrdinalIgnoreCase) &&
            currentTags.SequenceEqual(assessmentTags, StringComparer.Ordinal) &&
            dirtyPaths.SequenceEqual(assessmentDirty, StringComparer.Ordinal);

        if (!stateMatches)
            throw new InvalidDataException("Recovery assessment is stale relative to the current Workbench Git state. Run Recovery check again before planning recovery.");

        var steps = new List<MaintenanceRecoveryPlanStep>();
        string status;
        bool separateAuthorityEligible;

        switch (assessment.Classification)
        {
            case "CLEAN_ACCEPTED":
                status = "NO_RECOVERY_REQUIRED";
                separateAuthorityEligible = false;
                steps.Add(new MaintenanceRecoveryPlanStep(
                    1,
                    "retain-current-accepted-state",
                    repositoryRoot,
                    "Working tree is clean and no local maintenance residue was observed by the bound assessment.",
                    false,
                    false,
                    false));
                break;

            case "CLEAN_ACCEPTED_WITH_STALE_MAINTENANCE_EVIDENCE":
                status = "NO_RECOVERY_REQUIRED_RETAIN_STALE_EVIDENCE";
                separateAuthorityEligible = false;
                steps.Add(new MaintenanceRecoveryPlanStep(
                    1,
                    "retain-current-accepted-state",
                    repositoryRoot,
                    "Current Git state is clean and accepted; stale maintenance evidence does not imply a failed source state.",
                    false,
                    false,
                    false));
                steps.Add(new MaintenanceRecoveryPlanStep(
                    2,
                    "retain-maintenance-evidence",
                    "WorkBench-local backup/candidate/evidence roots",
                    "Retention is the default because evidence existence is not deletion authority and stale evidence may still explain prior maintenance transitions.",
                    false,
                    false,
                    true));
                break;

            case "BOUNDED_DIRTY_UPDATE_CANDIDATE":
                status = "READY_FOR_SEPARATE_RECOVERY_AUTHORITY";
                separateAuthorityEligible = true;
                var sequence = 1;
                foreach (var entry in statusEntries.OrderBy(x => x.Path, StringComparer.Ordinal))
                {
                    var untracked = string.Equals(entry.XY, "??", StringComparison.Ordinal);
                    steps.Add(new MaintenanceRecoveryPlanStep(
                        sequence++,
                        untracked ? "remove-exact-untracked-candidate-path-after-byte-reverification" : "restore-tracked-path-from-current-accepted-head",
                        entry.Path,
                        untracked
                            ? "Path is untracked in the fresh bounded dirty candidate assessment. Any later removal must first prove exact candidate-byte identity."
                            : $"Git porcelain state {entry.XY} is bounded by the fresh assessment; a later recovery gate may restore this tracked path from the accepted HEAD.",
                        true,
                        untracked,
                        true));
                }
                steps.Add(new MaintenanceRecoveryPlanStep(
                    sequence,
                    "post-recovery-read-only-assessment",
                    repositoryRoot,
                    "Any later recovery execution must end with a new read-only assessment proving the resulting state; that assessment is not created by this plan.",
                    false,
                    false,
                    true));
                break;

            case "UNKNOWN_DIRTY_WORKTREE":
                status = "REFUSED_UNBOUNDED_RECOVERY_PLAN";
                separateAuthorityEligible = false;
                break;

            default:
                throw new InvalidDataException($"Unsupported recovery assessment classification: {assessment.Classification}");
        }

        var retainedRoots = assessment.SourceBackupRoots
            .Concat(assessment.CandidateRoots)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nonEffects = new[]
        {
            "no source file mutation",
            "no source restore or rollback",
            "no file or directory deletion",
            "no dotnet build or publish",
            "no git add/commit/tag",
            "no git fetch or push",
            "no remote creation/update",
            "no network access",
            "no Matawaka catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "plan artifact write is limited to Workbench/artifacts/recovery-plans"
        };

        return new MaintenanceRecoveryPlanReceipt(
            "matawaka.workbench-maintenance-recovery-plan/v0.17",
            Version,
            DateTimeOffset.Now,
            repositoryRoot,
            validatedAssessmentPath,
            assessmentSha256,
            assessment.Schema,
            assessment.ObservedAt,
            assessment.Classification,
            assessment.CurrentHead,
            assessmentTags,
            assessmentDirty,
            currentHead,
            currentTags,
            dirtyPaths,
            true,
            true,
            true,
            assessment.RecoveryRequired,
            separateAuthorityEligible,
            status,
            steps,
            retainedRoots,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            nonEffects,
            "A fresh recovery assessment can support a bounded recovery plan, but a plan is not recovery authority. This receipt cannot restore, delete, mutate source, build, checkpoint, publish, access the network, mutate the catalog, or grant Agent Execute. Stale maintenance evidence is retained by default rather than treated as garbage.");
    }

    private static string ValidateAssessmentArtifact(
        string repositoryRoot,
        string artifactPath,
        MaintenanceRecoveryAssessmentReceipt expected)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
            throw new InvalidDataException("Recovery assessment artifact is missing.");

        var full = Path.GetFullPath(artifactPath);
        var allowedRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "recovery-assessments")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recovery assessment artifact must be under Workbench/artifacts/recovery-assessments.");

        var parsed = JsonSerializer.Deserialize<MaintenanceRecoveryAssessmentReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("Recovery assessment artifact could not be parsed.");

        if (!string.Equals(parsed.Schema, expected.Schema, StringComparison.Ordinal) ||
            parsed.ObservedAt != expected.ObservedAt ||
            !string.Equals(parsed.CurrentHead, expected.CurrentHead, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.Classification, expected.Classification, StringComparison.Ordinal) ||
            parsed.WorkingTreeClean != expected.WorkingTreeClean ||
            !parsed.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(expected.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Recovery assessment artifact does not match the in-memory assessment receipt.");

        return full;
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository not found: {root}");
        return root;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record GitStatusEntry(string XY, string Path);

    private static IReadOnlyList<GitStatusEntry> ParseStatusEntries(string output)
    {
        var entries = new List<GitStatusEntry>();
        foreach (var raw in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4) throw new InvalidDataException($"Unexpected git status porcelain line: {raw}");
            var xy = raw[..2];
            var path = raw[3..];
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) path = path[(arrow + 4)..];
            path = NormalizeRelativePath(path.Trim('"'));
            entries.Add(new GitStatusEntry(xy, path));
        }
        return entries;
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static IReadOnlyList<string> SplitLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static async Task<string> RunGitReadOnlyAsync(string repositoryRoot, CancellationToken cancellationToken, params string[] args)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "rev-parse", "tag", "status" };
        if (args.Length == 0 || !allowed.Contains(args[0])) throw new InvalidDataException("Recovery plan attempted a non-allowlisted Git operation.");

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
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only Git recovery-plan process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("Read-only Git recovery planning timed out after 10 seconds.");
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidDataException($"Read-only Git recovery planning failed: {stderr.Trim()}");
        return stdout;
    }
}
