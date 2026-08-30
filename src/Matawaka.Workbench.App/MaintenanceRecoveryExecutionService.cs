using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record MaintenanceRecoveryExecutionPathReceipt(
    string Path,
    string GitStatus,
    string PlannedAction,
    string RecoveryOperation,
    string PreRecoverySha256,
    string ExpectedCandidateSha256,
    bool PostRecoveryExists,
    string? PostRecoverySha256);

public sealed record MaintenanceRecoveryExecutionAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string RepositoryRoot,
    string AcceptedHead,
    IReadOnlyList<string> AcceptedTags,
    string AssessmentArtifactPath,
    string AssessmentArtifactSha256,
    string PlanArtifactPath,
    string PlanArtifactSha256,
    string CandidateApplyPlanArtifactPath,
    string CandidateApplyPlanArtifactSha256,
    IReadOnlyList<string> DirtyPaths,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool FreshAssessmentAndPlanRevalidationRequired,
    bool ExactCandidateByteReverificationRequired,
    bool RestoreTrackedPathsAllowed,
    bool RemoveExactUntrackedCandidatePathsAllowed,
    bool BuildAllowed,
    bool CheckpointAllowed,
    bool NetworkAccessAllowed,
    bool CatalogMutationAllowed,
    bool AgentExecuteAllowed,
    IReadOnlyList<string> AllowedEffects,
    IReadOnlyList<string> NonEffects);

public sealed record MaintenanceRecoveryExecutionReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RepositoryRoot,
    string AssessmentArtifactPath,
    string AssessmentArtifactSha256,
    string PlanArtifactPath,
    string PlanArtifactSha256,
    string CandidateApplyPlanArtifactPath,
    string CandidateApplyPlanArtifactSha256,
    string PreRecoveryHead,
    IReadOnlyList<string> PreRecoveryTags,
    IReadOnlyList<string> PreRecoveryDirtyPaths,
    MaintenanceRecoveryExecutionAuthorityReceipt Authority,
    IReadOnlyList<MaintenanceRecoveryExecutionPathReceipt> PathReceipts,
    string PostRecoveryHead,
    IReadOnlyList<string> PostRecoveryTags,
    IReadOnlyList<string> PostRecoveryDirtyPaths,
    bool HeadUnchanged,
    bool AcceptedTagsUnchanged,
    bool WorkingTreeCleanAfterRecovery,
    bool FreshPostRecoveryAssessmentRequired,
    string Status,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Explicit bounded recovery execution for an interrupted Workbench update.
/// The service consumes a fresh BOUNDED_DIRTY_UPDATE_CANDIDATE assessment and a
/// READY recovery plan, then separately re-binds the dirty bytes to one exact
/// staged apply-plan receipt before any mutation. It may only restore tracked
/// Workbench paths from the current accepted HEAD blob bytes and delete exact
/// untracked candidate bytes. It cannot build, checkpoint, use the network,
/// mutate catalog repositories, or grant Agent Execute authority.
/// </summary>
public sealed class MaintenanceRecoveryExecutionService
{
    public const string Version = "0.18.0";
    public const string ReceiptSchema = "matawaka.workbench-maintenance-recovery-execution/v0.18";
    public const string AuthoritySchema = "matawaka.workbench-maintenance-recovery-execution-authority/v0.18";

    private static readonly TimeSpan GitProcessTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<(MaintenanceRecoveryExecutionReceipt Receipt, string ArtifactPath, string AuthorityPath)> ExecuteAsync(
        string workspaceRoot,
        string assessmentArtifactPath,
        MaintenanceRecoveryAssessmentReceipt assessment,
        string planArtifactPath,
        MaintenanceRecoveryPlanReceipt plan,
        CancellationToken cancellationToken)
    {
        if (assessment is null) throw new InvalidDataException("Recovery assessment is required.");
        if (plan is null) throw new InvalidDataException("Recovery plan is required.");
        if (!string.Equals(assessment.Classification, "BOUNDED_DIRTY_UPDATE_CANDIDATE", StringComparison.Ordinal) || !assessment.RecoveryRequired)
            throw new InvalidDataException("Recovery execution is only eligible for a fresh BOUNDED_DIRTY_UPDATE_CANDIDATE assessment.");
        if (!string.Equals(plan.Status, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) ||
            !plan.SeparateRecoveryAuthorityEligible ||
            !plan.AssessmentStillCurrent ||
            plan.RecoveryExecutionAuthorized || plan.RollbackAuthorized || plan.DeletionAuthorized ||
            plan.SourceMutationAuthorized || plan.BuildAuthorized || plan.CheckpointAuthorized ||
            plan.NetworkAccessAuthorized || plan.CatalogMutationAuthorized || plan.AgentExecuteAuthorized)
            throw new InvalidDataException("Recovery plan is not eligible for a separate recovery execution authority decision.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var validatedAssessmentPath = ValidateAssessmentArtifact(repositoryRoot, assessmentArtifactPath, assessment);
        var assessmentSha = HashFile(validatedAssessmentPath);
        var validatedPlanPath = ValidatePlanArtifact(repositoryRoot, planArtifactPath, plan, validatedAssessmentPath, assessmentSha);
        var planSha = HashFile(validatedPlanPath);

        var preHead = (await RunGitTextAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var preTags = SplitLines(await RunGitTextAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD").ConfigureAwait(false))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var statusOutput = await RunGitTextAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all").ConfigureAwait(false);
        var statusEntries = ParseStatusEntries(statusOutput);
        var dirtyPaths = statusEntries.Select(x => x.Path).OrderBy(x => x, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray();

        var expectedTags = assessment.CurrentTags.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var expectedDirty = assessment.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!string.Equals(preHead, assessment.CurrentHead, StringComparison.OrdinalIgnoreCase) ||
            !preTags.SequenceEqual(expectedTags, StringComparer.Ordinal) ||
            !dirtyPaths.SequenceEqual(expectedDirty, StringComparer.Ordinal))
            throw new InvalidDataException("Workbench Git state changed after the recovery assessment/plan. Run Recovery check and Recovery plan again.");

        if (!string.Equals(plan.ReverifiedHead, preHead, StringComparison.OrdinalIgnoreCase) ||
            !plan.ReverifiedTags.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(preTags, StringComparer.Ordinal) ||
            !plan.ReverifiedDirtyPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(dirtyPaths, StringComparer.Ordinal))
            throw new InvalidDataException("Recovery plan no longer matches the freshly reverified Git state.");

        foreach (var entry in statusEntries)
        {
            if (!string.Equals(entry.XY, " M", StringComparison.Ordinal) && !string.Equals(entry.XY, "??", StringComparison.Ordinal))
                throw new InvalidDataException($"Recovery execution refuses index changes, renames, deletes, or unrecognized Git status {entry.XY} for {entry.Path}.");
        }

        var candidateBinding = BindExactCandidatePlan(repositoryRoot, preHead, statusEntries);
        var nonEffects = new[]
        {
            "no git add/commit/tag",
            "no git fetch or push",
            "no remote creation/update",
            "no dotnet restore/build/test/publish",
            "no package download or installer execution",
            "no network access",
            "no Matawaka catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no checkpoint authority",
            "no deletion except an exact byte-reverified untracked candidate path",
            "no tracked restore from arbitrary backup or external source; tracked bytes come only from current accepted HEAD",
            "post-recovery assessment remains a separate read-only step"
        };

        var authority = new MaintenanceRecoveryExecutionAuthorityReceipt(
            AuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.maintenance.recover-bounded-dirty-update-candidate",
            repositoryRoot,
            preHead,
            preTags,
            validatedAssessmentPath,
            assessmentSha,
            validatedPlanPath,
            planSha,
            candidateBinding.Path,
            candidateBinding.Sha256,
            dirtyPaths,
            "explicit Recovery execute button + confirmation dialog after a fresh BOUNDED_DIRTY assessment and READY recovery plan",
            true,
            true,
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
                "restore only exact tracked dirty candidate paths from current accepted HEAD blob bytes",
                "delete only exact untracked candidate paths after SHA-256 re-verification against the bound staged apply plan",
                "write Workbench-local recovery authority/execution receipts under artifacts/recovery-executions"
            },
            nonEffects);

        var artifactDir = Path.Combine(repositoryRoot, "artifacts", "recovery-executions");
        Directory.CreateDirectory(artifactDir);
        var authorityPath = Path.Combine(artifactDir, $"recovery-execution-authority-v0.18-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(authorityPath, JsonSerializer.Serialize(authority, JsonOptions), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

        var pathReceipts = new List<MaintenanceRecoveryExecutionPathReceipt>();
        foreach (var entry in statusEntries.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var change = candidateBinding.Plan.SourceChanges.Single(x => string.Equals(NormalizeRelativePath(x.Path), entry.Path, StringComparison.Ordinal));
            var destination = ResolveBoundedRepositoryPath(repositoryRoot, entry.Path);
            if (!File.Exists(destination))
                throw new InvalidDataException($"Dirty candidate path disappeared before recovery execution: {entry.Path}");

            var preSha = HashFile(destination);
            if (!string.Equals(preSha, change.StagedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Candidate bytes changed after recovery authority was created: {entry.Path}");

            if (string.Equals(entry.XY, "??", StringComparison.Ordinal))
            {
                if (!string.Equals(change.Action, "Add", StringComparison.Ordinal))
                    throw new InvalidDataException($"Untracked recovery path is not an Add in the bound staged apply plan: {entry.Path}");
                File.Delete(destination);
                if (File.Exists(destination))
                    throw new InvalidDataException($"Exact untracked candidate path was not removed: {entry.Path}");
                pathReceipts.Add(new MaintenanceRecoveryExecutionPathReceipt(
                    entry.Path,
                    entry.XY,
                    change.Action,
                    "remove-exact-untracked-candidate-path",
                    preSha,
                    change.StagedSha256,
                    false,
                    null));
            }
            else
            {
                if (!string.Equals(change.Action, "Replace", StringComparison.Ordinal))
                    throw new InvalidDataException($"Tracked dirty recovery path is not a Replace in the bound staged apply plan: {entry.Path}");
                var acceptedBytes = await ReadAcceptedHeadBlobAsync(repositoryRoot, entry.Path, cancellationToken).ConfigureAwait(false);
                await WriteExactFileAsync(destination, acceptedBytes, cancellationToken).ConfigureAwait(false);
                var postSha = HashFile(destination);
                pathReceipts.Add(new MaintenanceRecoveryExecutionPathReceipt(
                    entry.Path,
                    entry.XY,
                    change.Action,
                    "restore-tracked-path-from-current-accepted-head-blob",
                    preSha,
                    change.StagedSha256,
                    true,
                    postSha));
            }
        }

        var postHead = (await RunGitTextAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var postTags = SplitLines(await RunGitTextAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD").ConfigureAwait(false))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var postStatus = await RunGitTextAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all").ConfigureAwait(false);
        var postDirty = ParseStatusEntries(postStatus).Select(x => x.Path).OrderBy(x => x, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray();

        var headUnchanged = string.Equals(preHead, postHead, StringComparison.OrdinalIgnoreCase);
        var tagsUnchanged = preTags.SequenceEqual(postTags, StringComparer.Ordinal);
        var clean = postDirty.Length == 0;
        if (!headUnchanged || !tagsUnchanged || !clean)
            throw new InvalidDataException("Recovery execution did not converge to the same clean accepted HEAD/tag state. No checkpoint or build will be attempted.");

        var receipt = new MaintenanceRecoveryExecutionReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            repositoryRoot,
            validatedAssessmentPath,
            assessmentSha,
            validatedPlanPath,
            planSha,
            candidateBinding.Path,
            candidateBinding.Sha256,
            preHead,
            preTags,
            dirtyPaths,
            authority,
            pathReceipts,
            postHead,
            postTags,
            postDirty,
            true,
            true,
            true,
            true,
            "RECOVERED_TO_CURRENT_ACCEPTED_HEAD_FRESH_ASSESSMENT_REQUIRED",
            nonEffects,
            "Recovery execution is a separately confirmed bounded maintenance action. It restores only the Workbench source paths proven to be exact bytes of one interrupted update candidate and returns to the same accepted HEAD/tag. It does not accept a candidate, build, checkpoint, publish, delete retained maintenance evidence, access the network, mutate the catalog, or grant Agent Execute. Run a fresh Recovery check after execution.");

        var receiptPath = Path.Combine(artifactDir, $"recovery-execution-v0.18-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return (receipt, receiptPath, authorityPath);
    }

    private static (WorkbenchStagedApplyPlanReceipt Plan, string Path, string Sha256) BindExactCandidatePlan(
        string repositoryRoot,
        string acceptedHead,
        IReadOnlyList<GitStatusEntry> statusEntries)
    {
        var planDir = Path.Combine(repositoryRoot, "artifacts", "update-apply-plans");
        if (!Directory.Exists(planDir))
            throw new InvalidDataException("No local staged apply-plan evidence exists for bounded recovery execution.");

        var dirtyPaths = statusEntries.Select(x => x.Path).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        foreach (var file in Directory.GetFiles(planDir, "*.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).Take(100))
        {
            WorkbenchStagedApplyPlanReceipt? candidate;
            try
            {
                candidate = JsonSerializer.Deserialize<WorkbenchStagedApplyPlanReceipt>(File.ReadAllText(file, Encoding.UTF8), JsonOptions);
            }
            catch
            {
                continue;
            }
            if (candidate is null ||
                !string.Equals(candidate.Status, "READY_FOR_SEPARATE_SOURCE_APPLY_AUTHORITY", StringComparison.Ordinal) ||
                !string.Equals(candidate.PredecessorCommit, acceptedHead, StringComparison.OrdinalIgnoreCase) ||
                candidate.SourceMutationAuthorized || candidate.BuildAuthorized || candidate.CheckpointAuthorized)
                continue;

            var mutatingChanges = candidate.SourceChanges
                .Where(x => string.Equals(x.Action, "Add", StringComparison.Ordinal) || string.Equals(x.Action, "Replace", StringComparison.Ordinal))
                .Select(x => new { Change = x, Path = NormalizeRelativePath(x.Path) })
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .ToArray();
            if (!mutatingChanges.Select(x => x.Path).SequenceEqual(dirtyPaths, StringComparer.Ordinal))
                continue;

            var exact = true;
            foreach (var entry in statusEntries)
            {
                var match = mutatingChanges.SingleOrDefault(x => string.Equals(x.Path, entry.Path, StringComparison.Ordinal));
                if (match is null)
                {
                    exact = false;
                    break;
                }
                if (string.Equals(entry.XY, "??", StringComparison.Ordinal) && !string.Equals(match.Change.Action, "Add", StringComparison.Ordinal))
                {
                    exact = false;
                    break;
                }
                if (string.Equals(entry.XY, " M", StringComparison.Ordinal) && !string.Equals(match.Change.Action, "Replace", StringComparison.Ordinal))
                {
                    exact = false;
                    break;
                }
                var full = ResolveBoundedRepositoryPath(repositoryRoot, entry.Path);
                if (!File.Exists(full) || !string.Equals(HashFile(full), match.Change.StagedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    exact = false;
                    break;
                }
            }
            if (exact)
                return (candidate, file, HashFile(file));
        }

        throw new InvalidDataException("Dirty Workbench paths could not be byte-bound to one exact staged apply-plan receipt for the current accepted HEAD. Recovery execution is refused.");
    }

    private static string ValidateAssessmentArtifact(string repositoryRoot, string artifactPath, MaintenanceRecoveryAssessmentReceipt expected)
    {
        var full = ValidateArtifactPath(repositoryRoot, artifactPath, "recovery-assessments");
        var parsed = JsonSerializer.Deserialize<MaintenanceRecoveryAssessmentReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("Recovery assessment artifact could not be parsed.");
        if (!string.Equals(parsed.Schema, expected.Schema, StringComparison.Ordinal) ||
            !string.Equals(parsed.CurrentHead, expected.CurrentHead, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.Classification, expected.Classification, StringComparison.Ordinal) ||
            !parsed.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(expected.DirtyPaths.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Recovery assessment artifact does not match the in-memory assessment receipt.");
        return full;
    }

    private static string ValidatePlanArtifact(
        string repositoryRoot,
        string artifactPath,
        MaintenanceRecoveryPlanReceipt expected,
        string assessmentPath,
        string assessmentSha)
    {
        var full = ValidateArtifactPath(repositoryRoot, artifactPath, "recovery-plans");
        var parsed = JsonSerializer.Deserialize<MaintenanceRecoveryPlanReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("Recovery plan artifact could not be parsed.");
        if (!string.Equals(parsed.Schema, expected.Schema, StringComparison.Ordinal) ||
            !string.Equals(parsed.Status, expected.Status, StringComparison.Ordinal) ||
            !string.Equals(parsed.ReverifiedHead, expected.ReverifiedHead, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFullPath(parsed.AssessmentArtifactPath), Path.GetFullPath(assessmentPath), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.AssessmentArtifactSha256, assessmentSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recovery plan artifact does not match the in-memory plan or bound assessment artifact.");
        return full;
    }

    private static string ValidateArtifactPath(string repositoryRoot, string artifactPath, string childDirectory)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
            throw new InvalidDataException($"Required recovery {childDirectory} artifact is missing.");
        var full = Path.GetFullPath(artifactPath);
        var allowedRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", childDirectory)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Recovery artifact must be under Workbench/artifacts/{childDirectory}.");
        return full;
    }

    private static async Task<byte[]> ReadAcceptedHeadBlobAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken)
    {
        var normalized = NormalizeRelativePath(relativePath);
        ValidateRelativePath(normalized);
        var psi = CreateGitStartInfo(repositoryRoot);
        psi.ArgumentList.Add("show");
        psi.ArgumentList.Add($"HEAD:{normalized}");

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only Git blob process.");
        using var output = new MemoryStream();
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await copyTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("Read-only Git blob recovery lookup timed out.");
        }
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Accepted HEAD blob lookup failed for {normalized}: {stderr.Trim()}");
        return output.ToArray();
    }

    private static async Task WriteExactFileAsync(string destination, byte[] bytes, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Recovery destination directory cannot be resolved.");
        Directory.CreateDirectory(directory);
        var temp = destination + $".matawaka-recovery-{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temp, destination, true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    private static async Task<string> RunGitTextAsync(string repositoryRoot, CancellationToken cancellationToken, params string[] args)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "rev-parse", "tag", "status" };
        if (args.Length == 0 || !allowed.Contains(args[0]))
            throw new InvalidDataException("Recovery execution attempted a non-allowlisted Git query.");
        var psi = CreateGitStartInfo(repositoryRoot);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only Git recovery process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidDataException("Read-only Git recovery query timed out.");
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidDataException($"Read-only Git recovery query failed: {stderr.Trim()}");
        return stdout;
    }

    private static ProcessStartInfo CreateGitStartInfo(string repositoryRoot)
    {
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
        return psi;
    }

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
            ValidateRelativePath(path);
            entries.Add(new GitStatusEntry(xy, path));
        }
        return entries.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository not found: {root}");
        return root;
    }

    private static string ResolveBoundedRepositoryPath(string repositoryRoot, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        ValidateRelativePath(normalized);
        var rootPrefix = Path.GetFullPath(repositoryRoot) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(repositoryRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Recovery path escapes Workbench repository: {relativePath}");
        return full;
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("../", StringComparison.Ordinal) || path.Contains("..\\", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsafe recovery relative path: {path}");
        if (path.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase) || path.StartsWith(".workbench/", StringComparison.OrdinalIgnoreCase) || path.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Recovery execution cannot mutate maintenance/evidence/Git metadata paths: {path}");
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static IReadOnlyList<string> SplitLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record GitStatusEntry(string XY, string Path);
}
