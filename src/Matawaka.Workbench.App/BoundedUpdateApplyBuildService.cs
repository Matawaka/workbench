using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record WorkbenchUpdateApplyBuildAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string TargetRepository,
    string TargetVersion,
    string TargetTag,
    string PredecessorTag,
    string PredecessorCommit,
    string StagingRoot,
    string ApplyPlanSha256,
    string FixedDotnetPath,
    string FixedDotnetSha256,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool FreshPlanRevalidationRequired,
    bool ExactSourceMutationAllowed,
    bool FixedOfflineBuildAllowed,
    bool FixedOfflinePublishAllowed,
    bool CandidateLaunchAllowed,
    bool CheckpointAllowed,
    bool NetworkAccessAllowed,
    bool NetworkIsolationEnforced,
    bool CatalogMutationAllowed,
    bool AgentExecuteAllowed,
    IReadOnlyList<string> AllowedEffects,
    IReadOnlyList<string> NonEffects);

public sealed record WorkbenchUpdateApplyBuildReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string TargetVersion,
    string TargetTag,
    string PredecessorTag,
    string PredecessorCommit,
    string StagingRoot,
    string ApplyPlanArtifactPath,
    string ApplyPlanSha256,
    IReadOnlyList<WorkbenchStagedSourceChange> SourceChanges,
    WorkbenchUpdateApplyBuildAuthorityReceipt Authority,
    bool FreshApplyPlanVerified,
    bool ExactSourceBytesApplied,
    bool WorkingTreeMatchesPlannedDelta,
    bool OfflineBuildCompleted,
    bool OfflineAppPublishCompleted,
    bool OfflineSemanticHostPublishCompleted,
    string BuildSourceManifestPath,
    string BuildSourceManifestSha256,
    string CandidateExecutablePath,
    string CandidateExecutableSha256,
    string SemanticHostExecutablePath,
    string SemanticHostExecutableSha256,
    string Status,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record WorkbenchCandidateLaunchAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string CandidateExecutablePath,
    string CandidateExecutableSha256,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool ExactReceiptBoundExecutableOnly,
    bool NetworkAuthorityCreated,
    bool CatalogMutationAuthorityCreated,
    bool AgentExecuteAuthorityCreated,
    bool CheckpointAuthorityCreated,
    IReadOnlyList<string> NonEffects);

public sealed record WorkbenchCandidateLaunchReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string CandidateExecutablePath,
    string CandidateExecutableSha256,
    int ProcessId,
    WorkbenchCandidateLaunchAuthorityReceipt Authority,
    string Status,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// v0.14 maintenance transaction. It consumes a freshly revalidated staging-only
/// materialization and READY staged apply plan after a separate explicit UI
/// confirmation. It may apply only the exact planned payload bytes to Workbench
/// source, run only the fixed local dotnet executable with --no-restore for
/// build/publish, and emit byte-bound receipts. It cannot checkpoint, publish to
/// Git remotes, mutate catalog repositories, or grant Agent Execute authority.
/// Candidate launch is a separate explicit receipt-bound UI gate.
/// </summary>
public sealed class BoundedUpdateApplyBuildService
{
    public const string ReceiptSchema = "matawaka.workbench-update-apply-build-receipt/v0.14";
    public const string AuthoritySchema = "matawaka.workbench-update-apply-build-authority-receipt/v0.14";
    public const string LaunchReceiptSchema = "matawaka.workbench-candidate-launch-receipt/v0.14";
    public const string LaunchAuthoritySchema = "matawaka.workbench-candidate-launch-authority-receipt/v0.14";
    public const string Version = "0.14.0";

    private static readonly TimeSpan GitProcessTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DotnetProcessTimeout = TimeSpan.FromMinutes(3);

    private readonly StagedUpdateApplyPlanService _planner;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public BoundedUpdateApplyBuildService(StagedUpdateApplyPlanService planner)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    }

    public async Task<(WorkbenchUpdateApplyBuildReceipt Receipt, string ArtifactPath, string AuthorityPath)> ApplyAndBuildAsync(
        WorkbenchUpdateMaterializationReceipt materialization,
        WorkbenchStagedApplyPlanReceipt confirmedPlan,
        string confirmedPlanArtifactPath,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (materialization is null) throw new InvalidDataException("Materialization receipt is required.");
        if (confirmedPlan is null) throw new InvalidDataException("A READY staged apply plan is required.");
        if (!string.Equals(confirmedPlan.Status, "READY_FOR_SEPARATE_SOURCE_APPLY_AUTHORITY", StringComparison.Ordinal) ||
            confirmedPlan.SourceMutationAuthorized || confirmedPlan.BuildAuthorized || confirmedPlan.CheckpointAuthorized)
            throw new InvalidDataException("The staged apply plan is not eligible for a separate source-apply/build authority decision.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var planArtifact = ValidatePlanArtifact(repositoryRoot, confirmedPlanArtifactPath, confirmedPlan);
        var planSha = HashFile(planArtifact);

        // Freshly re-plan from the exact materialized staging before any mutation.
        var replanned = await _planner.PlanAsync(materialization, workspaceRoot, cancellationToken);
        VerifyEquivalentPlan(confirmedPlan, replanned.Receipt);

        var currentHead = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Trim();
        if (!string.Equals(currentHead, confirmedPlan.PredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed after staged planning.");
        var tags = SplitLines(await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "tag", "--points-at", "HEAD"));
        if (!tags.Contains(confirmedPlan.PredecessorTag, StringComparer.Ordinal))
            throw new InvalidDataException("Accepted predecessor tag no longer points at Workbench HEAD.");
        var statusBefore = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        if (!string.IsNullOrWhiteSpace(statusBefore))
            throw new InvalidDataException("Workbench working tree must be clean immediately before source apply.");

        var dotnetPath = ResolveFixedDotnet(workspaceRoot);
        var dotnetSha = HashFile(dotnetPath);
        var changes = confirmedPlan.SourceChanges
            .Where(item => item.Action is "Add" or "Replace")
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ToArray();
        if (changes.Length == 0)
            throw new InvalidDataException("The staged plan has no source mutation to apply.");

        var nonEffects = new[]
        {
            "no git add/commit/tag",
            "no git fetch or push",
            "no remote creation/update",
            "no package download or restore",
            "no network operation requested; OS network isolation is not enforced",
            "no Matawaka catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no checkpoint authority",
            "no arbitrary executable path or command accepted from JSON",
            "candidate launch remains a separate explicit UI authority gate",
            "git/dotnet maintenance subprocesses are timeout-bounded and killed on timeout"
        };

        var authority = new WorkbenchUpdateApplyBuildAuthorityReceipt(
            AuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.update.apply-source-and-build",
            repositoryRoot,
            confirmedPlan.TargetVersion,
            confirmedPlan.TargetTag,
            confirmedPlan.PredecessorTag,
            confirmedPlan.PredecessorCommit,
            confirmedPlan.StagingRoot,
            planSha,
            dotnetPath,
            dotnetSha,
            "explicit Apply + Build button + confirmation dialog after a fresh READY staged apply plan",
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
            false,
            new[]
            {
                "replace/add only the exact READY staged payload paths",
                "run fixed local dotnet build --no-restore",
                "run fixed local dotnet publish --no-restore for App",
                "run fixed local dotnet publish --no-restore for SemanticHost",
                "write Workbench-local authority/build/source-manifest receipts under artifacts"
            },
            nonEffects);

        var authorityDir = Path.Combine(repositoryRoot, "artifacts", "update-applies");
        Directory.CreateDirectory(authorityDir);
        var authorityPath = Path.Combine(authorityDir, $"apply-build-authority-v0.14-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(authorityPath, JsonSerializer.Serialize(authority, JsonOptions), new UTF8Encoding(false), cancellationToken);

        var backupRoot = Path.Combine(repositoryRoot, ".workbench", "update-source-backups", $"v0.14-{DateTime.Now:yyyyMMdd-HHmmssfff}");
        var candidateDir = Path.Combine(repositoryRoot, "artifacts", $"app-v{confirmedPlan.TargetVersion}-gui-update");
        var semanticDir = Path.Combine(candidateDir, "semantic-host");
        var applied = false;

        try
        {
            BackupCurrentFiles(repositoryRoot, backupRoot, changes);
            applied = true;
            ApplyExactStagedBytes(repositoryRoot, confirmedPlan.StagingRoot, changes);
            VerifyAppliedBytes(repositoryRoot, changes);
            await VerifyWorkingTreeDeltaAsync(repositoryRoot, changes, cancellationToken).ConfigureAwait(false);

            if (Directory.Exists(candidateDir)) Directory.Delete(candidateDir, recursive: true);
            Directory.CreateDirectory(candidateDir);
            Directory.CreateDirectory(semanticDir);

            var solutionPath = Path.Combine(repositoryRoot, "Matawaka.Workbench.sln");
            var appProject = Path.Combine(repositoryRoot, "src", "Matawaka.Workbench.App", "Matawaka.Workbench.App.csproj");
            var semanticProject = Path.Combine(repositoryRoot, "src", "Matawaka.Workbench.SemanticHost", "Matawaka.Workbench.SemanticHost.csproj");

            await RunDotnetAsync(dotnetPath, workspaceRoot, repositoryRoot, cancellationToken,
                "build", solutionPath, "-c", "Release", "--no-restore");
            await RunDotnetAsync(dotnetPath, workspaceRoot, repositoryRoot, cancellationToken,
                "publish", appProject, "-c", "Release", "--no-restore", "-o", candidateDir);
            await RunDotnetAsync(dotnetPath, workspaceRoot, repositoryRoot, cancellationToken,
                "publish", semanticProject, "-c", "Release", "--no-restore", "-o", semanticDir);

            VerifyAppliedBytes(repositoryRoot, changes);
            await VerifyWorkingTreeDeltaAsync(repositoryRoot, changes, cancellationToken).ConfigureAwait(false);

            var candidateExe = Path.Combine(candidateDir, "Matawaka.Workbench.App.exe");
            var semanticExe = Path.Combine(semanticDir, "Matawaka.Workbench.SemanticHost.exe");
            if (!File.Exists(candidateExe) || !File.Exists(semanticExe))
                throw new InvalidDataException("Fixed publish completed without the expected App/SemanticHost executable.");

            // The semantic runtime verifies a byte-bound integrity manifest before any semantic input.
            // GUI self-hosted publish must materialize that manifest just like the earlier bootstrap path did;
            // a published executable by itself is deliberately not sufficient authority to run the host.
            var semanticHostDigest = HashFile(semanticExe);
            WriteSemanticHostIntegrityManifest(semanticExe, semanticHostDigest);

            var manifest = await WriteDynamicBuildSourceManifestAsync(repositoryRoot, confirmedPlan, changes, cancellationToken);
            var receipt = new WorkbenchUpdateApplyBuildReceipt(
                ReceiptSchema,
                Version,
                DateTimeOffset.Now,
                confirmedPlan.TargetVersion,
                confirmedPlan.TargetTag,
                confirmedPlan.PredecessorTag,
                confirmedPlan.PredecessorCommit,
                confirmedPlan.StagingRoot,
                planArtifact,
                planSha,
                changes,
                authority,
                true,
                true,
                true,
                true,
                true,
                true,
                manifest.Path,
                manifest.Sha256,
                candidateExe,
                HashFile(candidateExe),
                semanticExe,
                HashFile(semanticExe),
                "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED",
                nonEffects,
                "v0.14 applies only a freshly revalidated exact staged source delta and runs only fixed local dotnet build/publish with --no-restore after explicit human confirmation. Git/dotnet maintenance subprocesses are timeout-bounded so UI causal liveness does not depend on an unbounded child process. The resulting candidate is byte-bound but is not accepted, checkpointed, published, or launched by this receipt.");

            var receiptPath = Path.Combine(authorityDir, $"apply-build-v0.14-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
            return (receipt, receiptPath, authorityPath);
        }
        catch (Exception original)
        {
            Exception? rollbackFailure = null;
            if (applied)
            {
                try { RestoreSource(repositoryRoot, backupRoot, changes); }
                catch (Exception ex) { rollbackFailure = ex; }
            }
            if (Directory.Exists(candidateDir))
            {
                try { Directory.Delete(candidateDir, recursive: true); } catch { }
            }
            string clean = string.Empty;
            try { clean = await RunGitReadOnlyAsync(repositoryRoot, CancellationToken.None, "status", "--porcelain=v1", "--untracked-files=all"); }
            catch (Exception ex) { rollbackFailure ??= ex; }
            if (rollbackFailure is not null || !string.IsNullOrWhiteSpace(clean))
                throw new InvalidDataException($"Apply/build failed and automatic rollback could not prove a clean predecessor source frontier. Original={original.Message}; Rollback={rollbackFailure?.Message ?? clean.Trim()}");
            throw;
        }
    }

    public async Task<(WorkbenchCandidateLaunchReceipt Receipt, string ArtifactPath)> LaunchCandidateAsync(
        WorkbenchUpdateApplyBuildReceipt buildReceipt,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (buildReceipt is null || !string.Equals(buildReceipt.Status, "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED", StringComparison.Ordinal))
            throw new InvalidDataException("A successful v0.14 apply/build receipt is required before candidate launch.");
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var expectedPrefix = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts")) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(buildReceipt.CandidateExecutablePath);
        if (!candidate.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
            throw new InvalidDataException("Candidate executable is missing or escapes Workbench artifacts.");
        var actualSha = HashFile(candidate);
        if (!string.Equals(actualSha, buildReceipt.CandidateExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Candidate executable changed after build receipt creation.");

        var head = (await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD")).Trim();
        if (!string.Equals(head, buildReceipt.PredecessorCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workbench HEAD changed before candidate launch.");
        VerifyAppliedBytes(repositoryRoot, buildReceipt.SourceChanges);
        await VerifyWorkingTreeDeltaAsync(repositoryRoot, buildReceipt.SourceChanges, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(buildReceipt.SemanticHostExecutablePath) ||
            !string.Equals(HashFile(buildReceipt.SemanticHostExecutablePath), buildReceipt.SemanticHostExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Receipt-bound SemanticHost changed or disappeared before candidate launch.");

        var nonEffects = new[]
        {
            "no git checkpoint",
            "no git fetch or push",
            "no remote publication",
            "no catalog repository mutation authority",
            "no Agent Execute authority",
            "no network authority inferred",
            "launch is limited to the exact receipt-bound candidate executable"
        };
        var authority = new WorkbenchCandidateLaunchAuthorityReceipt(
            LaunchAuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.update.launch-built-candidate",
            candidate,
            actualSha,
            "explicit Launch candidate button + confirmation after successful byte-bound apply/build receipt",
            true,
            true,
            false,
            false,
            false,
            false,
            nonEffects);

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = candidate,
            UseShellExecute = false,
            WorkingDirectory = repositoryRoot
        }) ?? throw new InvalidDataException("Failed to start the exact built candidate executable.");

        var receipt = new WorkbenchCandidateLaunchReceipt(
            LaunchReceiptSchema,
            Version,
            DateTimeOffset.Now,
            candidate,
            actualSha,
            process.Id,
            authority,
            "CANDIDATE_LAUNCHED_NOT_ACCEPTED",
            nonEffects,
            "Candidate launch is an explicitly confirmed local maintenance action. It does not accept the candidate; the launched Workbench must still pass Self-test and receive a separate explicit local checkpoint confirmation.");

        var dir = Path.Combine(repositoryRoot, "artifacts", "update-applies");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"candidate-launch-v0.14-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private static string ValidatePlanArtifact(string repositoryRoot, string path, WorkbenchStagedApplyPlanReceipt plan)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidDataException("Staged apply plan artifact is missing.");
        var full = Path.GetFullPath(path);
        var prefix = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "update-apply-plans")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Staged apply plan artifact escapes the fixed Workbench artifact directory.");
        var parsed = JsonSerializer.Deserialize<WorkbenchStagedApplyPlanReceipt>(File.ReadAllText(full, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("Staged apply plan artifact could not be parsed.");
        VerifyEquivalentPlan(plan, parsed);
        return full;
    }

    private static void VerifyEquivalentPlan(WorkbenchStagedApplyPlanReceipt expected, WorkbenchStagedApplyPlanReceipt observed)
    {
        if (!string.Equals(expected.Status, observed.Status, StringComparison.Ordinal) ||
            !string.Equals(expected.TargetVersion, observed.TargetVersion, StringComparison.Ordinal) ||
            !string.Equals(expected.TargetTag, observed.TargetTag, StringComparison.Ordinal) ||
            !string.Equals(expected.PredecessorTag, observed.PredecessorTag, StringComparison.Ordinal) ||
            !string.Equals(expected.PredecessorCommit, observed.PredecessorCommit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.StagingRoot, observed.StagingRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Fresh staged apply plan differs from the confirmed plan.");

        var a = expected.SourceChanges.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var b = observed.SourceChanges.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        if (a.Length != b.Length) throw new InvalidDataException("Fresh staged apply plan file count changed.");
        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i].Path, b[i].Path, StringComparison.Ordinal) ||
                !string.Equals(a[i].Action, b[i].Action, StringComparison.Ordinal) ||
                !string.Equals(a[i].CurrentSha256, b[i].CurrentSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(a[i].StagedSha256, b[i].StagedSha256, StringComparison.OrdinalIgnoreCase) ||
                a[i].StagedBytes != b[i].StagedBytes)
                throw new InvalidDataException($"Fresh staged apply plan changed for {a[i].Path}.");
        }
    }

    private static void BackupCurrentFiles(string repositoryRoot, string backupRoot, IReadOnlyList<WorkbenchStagedSourceChange> changes)
    {
        foreach (var change in changes.Where(x => x.Action == "Replace"))
        {
            var source = ResolveRepositoryDestination(repositoryRoot, change.Path);
            if (!File.Exists(source)) throw new InvalidDataException($"Planned replacement source disappeared: {change.Path}");
            var actual = HashFile(source);
            if (!string.Equals(actual, change.CurrentSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Planned replacement source changed: {change.Path}");
            var backup = ResolveBoundedPath(backupRoot, change.Path, "backup");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(source, backup, overwrite: false);
        }
    }

    private static void ApplyExactStagedBytes(string repositoryRoot, string stagingRoot, IReadOnlyList<WorkbenchStagedSourceChange> changes)
    {
        var payloadRoot = Path.Combine(stagingRoot, "payload");
        foreach (var change in changes)
        {
            var staged = ResolveBoundedPath(payloadRoot, change.Path, "staging");
            if (!File.Exists(staged) || !string.Equals(HashFile(staged), change.StagedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Staged source changed before apply: {change.Path}");
            var destination = ResolveRepositoryDestination(repositoryRoot, change.Path);
            if (change.Action == "Add" && File.Exists(destination))
                throw new InvalidDataException($"Planned Add destination now exists: {change.Path}");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temp = destination + ".matawaka-update-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(staged, temp, overwrite: false);
                if (!string.Equals(HashFile(temp), change.StagedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Temporary applied bytes mismatch: {change.Path}");
                File.Move(temp, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }

    private static void VerifyAppliedBytes(string repositoryRoot, IReadOnlyList<WorkbenchStagedSourceChange> changes)
    {
        foreach (var change in changes)
        {
            var destination = ResolveRepositoryDestination(repositoryRoot, change.Path);
            if (!File.Exists(destination) || !string.Equals(HashFile(destination), change.StagedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Applied source byte verification failed: {change.Path}");
        }
    }

    private static async Task VerifyWorkingTreeDeltaAsync(string repositoryRoot, IReadOnlyList<WorkbenchStagedSourceChange> changes, CancellationToken cancellationToken)
    {
        var result = await RunGitReadOnlyAsync(repositoryRoot, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all").ConfigureAwait(false);
        var actual = ParseStatusPaths(result);
        var expected = changes.Select(x => x.Path).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidDataException("Workbench working tree delta differs from the exact planned Add/Replace set.");
    }

    private static void RestoreSource(string repositoryRoot, string backupRoot, IReadOnlyList<WorkbenchStagedSourceChange> changes)
    {
        foreach (var change in changes.Reverse())
        {
            var destination = ResolveRepositoryDestination(repositoryRoot, change.Path);
            if (change.Action == "Add")
            {
                if (File.Exists(destination)) File.Delete(destination);
                continue;
            }
            var backup = ResolveBoundedPath(backupRoot, change.Path, "backup");
            if (!File.Exists(backup)) throw new InvalidDataException($"Rollback backup missing: {change.Path}");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(backup, destination, overwrite: true);
        }
    }

    private static void WriteSemanticHostIntegrityManifest(string semanticExecutablePath, string semanticHostSha256)
    {
        var semanticDirectory = Path.GetDirectoryName(semanticExecutablePath)
            ?? throw new InvalidDataException("Published SemanticHost directory cannot be resolved.");
        var manifestPath = Path.Combine(semanticDirectory, "semantic-host.integrity.json");
        var integrityManifest = new
        {
            Schema = "matawaka.semantic-host-integrity-manifest/v0.7",
            Executable = Path.GetFileName(semanticExecutablePath),
            Sha256 = semanticHostSha256,
            UuAapFrontier = "f5673a39ddeef05f82c828f6cff554518f5f8ef6"
        };
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(integrityManifest, JsonOptions),
            new UTF8Encoding(false));

        // Fail closed if the manifest was not durably materialized beside the exact host.
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("SemanticHost integrity manifest was not materialized after publish.");
    }

    private static async Task<(string Path, string Sha256)> WriteDynamicBuildSourceManifestAsync(
        string repositoryRoot,
        WorkbenchStagedApplyPlanReceipt plan,
        IReadOnlyList<WorkbenchStagedSourceChange> changes,
        CancellationToken cancellationToken)
    {
        var versionSuffix = ToSchemaVersion(plan.TargetVersion);
        var files = changes.OrderBy(x => x.Path, StringComparer.Ordinal)
            .Select(x => new BuildSourceManifestFile(x.Path, x.StagedSha256))
            .ToArray();
        var manifest = new BuildSourceManifest(
            $"matawaka.workbench-build-source-manifest/{versionSuffix}",
            plan.TargetVersion,
            plan.PredecessorCommit,
            DateTimeOffset.Now,
            files);
        var dir = Path.Combine(repositoryRoot, "artifacts", "checkpoints");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v{plan.TargetVersion}-source-manifest-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return (path, HashFile(path));
    }

    private static string ToSchemaVersion(string targetVersion)
    {
        var parts = targetVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out _) || !int.TryParse(parts[1], out _))
            throw new InvalidDataException($"Unsupported target version for build manifest schema: {targetVersion}");
        return $"v{parts[0]}.{parts[1]}";
    }

    private static async Task RunDotnetAsync(
        string dotnetPath,
        string workspaceRoot,
        string repositoryRoot,
        CancellationToken cancellationToken,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = dotnetPath,
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        psi.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(dotnetPath)!;
        psi.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        psi.Environment["NUGET_PACKAGES"] = Path.Combine(workspaceRoot, ".nuget", "packages");
        psi.Environment["DOTNET_CLI_HOME"] = Path.Combine(workspaceRoot, ".dotnet-home");
        psi.Environment["TEMP"] = Path.Combine(workspaceRoot, ".tmp");
        psi.Environment["TMP"] = Path.Combine(workspaceRoot, ".tmp");
        Directory.CreateDirectory(psi.Environment["DOTNET_CLI_HOME"]!);
        Directory.CreateDirectory(psi.Environment["TEMP"]!);

        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed local dotnet process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await WaitForProcessExitBoundedAsync(
            process,
            cancellationToken,
            DotnetProcessTimeout,
            $"fixed offline dotnet operation ({string.Join(" ", args)})").ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed offline dotnet operation failed ({string.Join(" ", args)}): {stderr.Trim()}\n{stdout.Trim()}");
    }

    private static string ResolveFixedDotnet(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var path = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), ".dotnet-sdk", "dotnet.exe"));
        if (!File.Exists(path)) throw new InvalidDataException($"Fixed local dotnet executable is missing: {path}");
        return path;
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static string ResolveRepositoryDestination(string repositoryRoot, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(".workbench/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Update payload target is not allowed in repository source: {relativePath}");
        return ResolveBoundedPath(repositoryRoot, normalized, "repository");
    }

    private static string ResolveBoundedPath(string rootPath, string relativePath, string label)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(rootPath, NormalizeRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Update path escapes {label} root: {relativePath}");
        return destination;
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Empty update path.");
        var normalized = path.Replace('\\', '/').Trim('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (normalized.Length == 0 || normalized.Contains(':') || normalized.Contains('\0') || parts.Any(x => x is "." or ".."))
            throw new InvalidDataException($"Unsafe update path: {path}");
        return string.Join('/', parts);
    }

    private static async Task<string> RunGitReadOnlyAsync(string repositoryRoot, CancellationToken cancellationToken, params string[] args)
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
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidDataException("Failed to start fixed read-only git process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await WaitForProcessExitBoundedAsync(process, cancellationToken, GitProcessTimeout, "fixed read-only git operation").ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidDataException($"Fixed read-only git operation failed: {stderr.Trim()}");
        return stdout;
    }

    private static async Task WaitForProcessExitBoundedAsync(
        Process process,
        CancellationToken cancellationToken,
        TimeSpan timeout,
        string operation)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort kill. The caller still receives a bounded failure.
            }

            try
            {
                if (!process.HasExited)
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the primary cancellation/timeout result.
            }

            if (cancellationToken.IsCancellationRequested)
                throw;

            throw new InvalidDataException($"{operation} timed out after {timeout.TotalSeconds:0} seconds; process tree termination was requested.");
        }
    }

    private static IReadOnlyList<string> ParseStatusPaths(string stdout)
        => SplitLines(stdout)
            .Select(line => line.Length >= 4 ? line[3..].Trim() : line.Trim())
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string[] SplitLines(string value)
        => value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
