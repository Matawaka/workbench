using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record MaintenanceUpdateOrchestratorPreview(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string PackagePath,
    string PackageFileName,
    string PackageSha256,
    string TargetVersion,
    string TargetTag,
    string PredecessorTag,
    string PredecessorCommit,
    WorkbenchUpdatePlanReceipt PreviewPlan,
    string PreviewPlanArtifactPath,
    bool EffectAuthorized,
    string Status,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record MaintenanceUpdateOrchestratorReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string PackagePath,
    string PackageSha256,
    string TargetVersion,
    string TargetTag,
    string PredecessorTag,
    string PredecessorCommit,
    WorkbenchUpdatePlanReceipt PreviewPlan,
    string PreviewPlanArtifactPath,
    WorkbenchUpdatePlanReceipt FreshPlan,
    string FreshPlanArtifactPath,
    WorkbenchUpdateMaterializationReceipt Materialization,
    string MaterializationArtifactPath,
    WorkbenchStagedApplyPlanReceipt StagedApplyPlan,
    string StagedApplyPlanArtifactPath,
    WorkbenchUpdateApplyBuildReceipt ApplyBuild,
    string ApplyBuildArtifactPath,
    string ApplyBuildAuthorityPath,
    bool SingleUiMaintenanceIntentObserved,
    bool TypedSubReceiptsPreserved,
    bool LaunchPerformed,
    bool CheckpointAuthorized,
    bool PublicationAuthorized,
    string Status,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// v0.33 sequences the existing typed update services behind one explicit
/// operator maintenance intent. It does not replace their validation logic,
/// does not mint downstream authority from an earlier receipt, and stops before
/// candidate launch. Apply/build rollback remains owned by the accepted
/// BoundedUpdateApplyBuildService.
/// </summary>
public sealed class MaintenanceUpdateOrchestratorService
{
    public const string PreviewSchema = "matawaka.workbench-maintenance-update-orchestrator-preview/v0.33";
    public const string ReceiptSchema = "matawaka.workbench-maintenance-update-orchestrator-receipt/v0.33";
    public const string Version = "0.33.0";

    private readonly LocalUpdateIntakeService _intake;
    private readonly LocalUpdateMaterializationService _materializer;
    private readonly StagedUpdateApplyPlanService _stagedPlanner;
    private readonly BoundedUpdateApplyBuildService _applyBuild;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public MaintenanceUpdateOrchestratorService(
        LocalUpdateIntakeService intake,
        LocalUpdateMaterializationService materializer,
        StagedUpdateApplyPlanService stagedPlanner,
        BoundedUpdateApplyBuildService applyBuild)
    {
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));
        _stagedPlanner = stagedPlanner ?? throw new ArgumentNullException(nameof(stagedPlanner));
        _applyBuild = applyBuild ?? throw new ArgumentNullException(nameof(applyBuild));
    }

    public async Task<MaintenanceUpdateOrchestratorPreview> PrepareAsync(
        string packagePath,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            throw new InvalidDataException("Local Workbench update package is missing.");

        var planned = await _intake.PlanAsync(packagePath, workspaceRoot, cancellationToken);
        var plan = planned.Receipt;
        if (!string.Equals(plan.Status, "READY_FOR_SEPARATE_MATERIALIZATION_AUTHORITY", StringComparison.Ordinal) ||
            plan.MaterializationAuthorized || plan.BuildAuthorized || plan.CheckpointAuthorized)
            throw new InvalidDataException($"Package preview is not READY for a later explicit maintenance session: {plan.Status}");

        var packageSha = HashFile(packagePath);
        if (!string.Equals(packageSha, plan.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package bytes changed while preparing the orchestrator preview.");

        var nonEffects = new[]
        {
            "preview writes only the existing read-only update-plan receipt",
            "no staging materialization",
            "no tracked source mutation",
            "no build or publish",
            "no candidate launch",
            "no checkpoint or tag creation",
            "no remote publication or network authority",
            "no catalog mutation",
            "no Agent Execute or ActionPermit"
        };

        return new MaintenanceUpdateOrchestratorPreview(
            PreviewSchema,
            Version,
            DateTimeOffset.Now,
            Path.GetFullPath(packagePath),
            Path.GetFileName(packagePath),
            packageSha,
            plan.TargetVersion,
            plan.TargetTag,
            plan.PredecessorTag,
            plan.PredecessorCommit,
            plan,
            planned.ArtifactPath,
            false,
            "READY_FOR_EXPLICIT_UPDATE_CANDIDATE_MAINTENANCE_INTENT",
            nonEffects,
            "Read-only preparation only. The preview itself carries no materialization/source/build/launch/checkpoint/publication authority.");
    }

    public async Task<MaintenanceUpdateOrchestratorReceipt> ExecuteConfirmedAsync(
        MaintenanceUpdateOrchestratorPreview preview,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ValidatePreview(preview);
        if (!File.Exists(preview.PackagePath))
            throw new InvalidDataException("The previewed package is no longer available.");
        var currentPackageSha = HashFile(preview.PackagePath);
        if (!string.Equals(currentPackageSha, preview.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package changed after Update candidate preview. Start a new maintenance session.");

        // Fresh read-only plan after the explicit operator decision. This plan is
        // compared to the preview but is still non-authorizing by itself.
        var freshlyPlanned = await _intake.PlanAsync(preview.PackagePath, workspaceRoot, cancellationToken);
        VerifyEquivalentPlan(preview.PreviewPlan, freshlyPlanned.Receipt);

        // Existing materializer re-verifies package/predecessor again before its
        // staging-only effect, preserving its own typed receipt and authority boundary.
        var materialized = await _materializer.MaterializeAsync(
            preview.PackagePath,
            freshlyPlanned.Receipt,
            workspaceRoot,
            cancellationToken);
        VerifyMaterialization(preview, materialized.Receipt);

        // Existing staged planner re-verifies accepted HEAD/tag, staging file-set and
        // all bytes. READY remains a plan state; it does not itself mutate source.
        var staged = await _stagedPlanner.PlanAsync(materialized.Receipt, workspaceRoot, cancellationToken);
        VerifyStagedPlan(preview, staged.Receipt);

        // Existing apply/build service performs its own fresh re-plan and owns the
        // exact source transaction + rollback behavior. Orchestrator does not duplicate it.
        var built = await _applyBuild.ApplyAndBuildAsync(
            materialized.Receipt,
            staged.Receipt,
            staged.ArtifactPath,
            workspaceRoot,
            cancellationToken);
        VerifyApplyBuild(preview, built.Receipt);

        var nonEffects = new[]
        {
            "no candidate launch; Launch candidate remains a separate UI decision",
            "no Self-test or acceptance",
            "no git checkpoint or accepted tag",
            "no remote publication",
            "no general network authority",
            "no catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no Stable Core or interface-registry promotion",
            "aggregate orchestration receipt does not replace or erase typed sub-receipts"
        };

        return new MaintenanceUpdateOrchestratorReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            preview.PackagePath,
            preview.PackageSha256,
            preview.TargetVersion,
            preview.TargetTag,
            preview.PredecessorTag,
            preview.PredecessorCommit,
            preview.PreviewPlan,
            preview.PreviewPlanArtifactPath,
            freshlyPlanned.Receipt,
            freshlyPlanned.ArtifactPath,
            materialized.Receipt,
            materialized.ArtifactPath,
            staged.Receipt,
            staged.ArtifactPath,
            built.Receipt,
            built.ArtifactPath,
            built.AuthorityPath,
            true,
            true,
            false,
            false,
            false,
            "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED",
            nonEffects,
            "One explicit Update candidate maintenance intent sequenced the existing typed plan/materialize/apply-plan/apply-build gates. Every service revalidated its own current evidence. Candidate launch remains a separate exact-executable confirmation and no downstream acceptance/publication authority was created.");
    }

    public static async Task<string> WriteReceiptAsync(
        string workspaceRoot,
        MaintenanceUpdateOrchestratorReceipt receipt,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var directory = Path.Combine(repositoryRoot, "artifacts", "update-orchestrator");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"update-orchestrator-v0.33-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, JsonOptions), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        var preview = new MaintenanceUpdateOrchestratorPreview(
            PreviewSchema,
            Version,
            DateTimeOffset.UnixEpoch,
            @"C:\fixture\candidate.zip",
            "candidate.zip",
            new string('a', 64),
            "0.33.0",
            "workbench-v0.33-accepted",
            "workbench-v0.32-accepted",
            new string('b', 40),
            null!,
            @"C:\fixture\preview.json",
            false,
            "READY_FOR_EXPLICIT_UPDATE_CANDIDATE_MAINTENANCE_INTENT",
            Array.Empty<string>(),
            "fixture");

        return new[]
        {
            ("orchestrator-preview-non-authorizing", preview.EffectAuthorized == false, preview.EffectAuthorized.ToString(), "false"),
            ("orchestrator-stops-before-launch", true, "ExecuteConfirmedAsync returns build receipt only; LaunchCandidateAsync is not called by orchestrator", "separate launch"),
            ("orchestrator-reuses-intake", true, nameof(LocalUpdateIntakeService), "existing typed intake"),
            ("orchestrator-reuses-materializer", true, nameof(LocalUpdateMaterializationService), "existing typed materializer"),
            ("orchestrator-reuses-staged-planner", true, nameof(StagedUpdateApplyPlanService), "existing typed staged planner"),
            ("orchestrator-reuses-apply-build", true, nameof(BoundedUpdateApplyBuildService), "existing typed apply/build + rollback"),
            ("orchestrator-no-action-permit", true, "no ActionPermit type/effect in orchestration contract", "no ActionPermit"),
            ("orchestrator-no-publication", true, "no FixedGitHubPublicationService invocation", "no publication")
        };
    }

    private static void ValidatePreview(MaintenanceUpdateOrchestratorPreview preview)
    {
        if (preview is null) throw new InvalidDataException("Update candidate preview is required.");
        if (!string.Equals(preview.Schema, PreviewSchema, StringComparison.Ordinal) ||
            !string.Equals(preview.Version, Version, StringComparison.Ordinal) ||
            !string.Equals(preview.Status, "READY_FOR_EXPLICIT_UPDATE_CANDIDATE_MAINTENANCE_INTENT", StringComparison.Ordinal) ||
            preview.EffectAuthorized)
            throw new InvalidDataException("Unexpected or authorizing orchestrator preview.");
        if (preview.PreviewPlan is null)
            throw new InvalidDataException("Typed preview plan is missing.");
    }

    private static void VerifyEquivalentPlan(WorkbenchUpdatePlanReceipt expected, WorkbenchUpdatePlanReceipt actual)
    {
        if (!string.Equals(actual.Status, "READY_FOR_SEPARATE_MATERIALIZATION_AUTHORITY", StringComparison.Ordinal) ||
            !string.Equals(expected.PackageSha256, actual.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.TargetVersion, actual.TargetVersion, StringComparison.Ordinal) ||
            !string.Equals(expected.TargetTag, actual.TargetTag, StringComparison.Ordinal) ||
            !string.Equals(expected.PredecessorTag, actual.PredecessorTag, StringComparison.Ordinal) ||
            !string.Equals(expected.PredecessorCommit, actual.PredecessorCommit, StringComparison.OrdinalIgnoreCase) ||
            expected.PayloadFileCount != actual.PayloadFileCount ||
            expected.PayloadBytes != actual.PayloadBytes ||
            expected.MaterializationAuthorized || actual.MaterializationAuthorized ||
            expected.BuildAuthorized || actual.BuildAuthorized ||
            expected.CheckpointAuthorized || actual.CheckpointAuthorized)
            throw new InvalidDataException("Fresh update plan differs from preview identity/bytes/authority boundary.");

        var expectedFiles = expected.PayloadFiles.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var actualFiles = actual.PayloadFiles.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        if (expectedFiles.Length != actualFiles.Length ||
            expectedFiles.Where((item, i) => !string.Equals(item.Path, actualFiles[i].Path, StringComparison.Ordinal) ||
                                             !string.Equals(item.Sha256, actualFiles[i].Sha256, StringComparison.OrdinalIgnoreCase)).Any())
            throw new InvalidDataException("Fresh update plan payload set differs from preview.");
    }

    private static void VerifyMaterialization(MaintenanceUpdateOrchestratorPreview preview, WorkbenchUpdateMaterializationReceipt receipt)
    {
        if (!string.Equals(receipt.Status, "MATERIALIZED_STAGING_ONLY", StringComparison.Ordinal) ||
            !string.Equals(receipt.PackageSha256, preview.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.TargetVersion, preview.TargetVersion, StringComparison.Ordinal) ||
            !string.Equals(receipt.TargetTag, preview.TargetTag, StringComparison.Ordinal) ||
            !string.Equals(receipt.PredecessorCommit, preview.PredecessorCommit, StringComparison.OrdinalIgnoreCase) ||
            receipt.Authority.RepositorySourceMutationAllowed || receipt.Authority.BuildAllowed || receipt.Authority.CheckpointAllowed)
            throw new InvalidDataException("Materialization receipt violated the expected staging-only v0.33 orchestration boundary.");
    }

    private static void VerifyStagedPlan(MaintenanceUpdateOrchestratorPreview preview, WorkbenchStagedApplyPlanReceipt receipt)
    {
        if (!string.Equals(receipt.Status, "READY_FOR_SEPARATE_SOURCE_APPLY_AUTHORITY", StringComparison.Ordinal) ||
            !string.Equals(receipt.TargetVersion, preview.TargetVersion, StringComparison.Ordinal) ||
            !string.Equals(receipt.TargetTag, preview.TargetTag, StringComparison.Ordinal) ||
            !string.Equals(receipt.PredecessorCommit, preview.PredecessorCommit, StringComparison.OrdinalIgnoreCase) ||
            receipt.SourceMutationAuthorized || receipt.BuildAuthorized || receipt.CheckpointAuthorized)
            throw new InvalidDataException("Staged apply plan violated the expected non-authorizing READY boundary.");
    }

    private static void VerifyApplyBuild(MaintenanceUpdateOrchestratorPreview preview, WorkbenchUpdateApplyBuildReceipt receipt)
    {
        if (!string.Equals(receipt.Status, "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED", StringComparison.Ordinal) ||
            !string.Equals(receipt.TargetVersion, preview.TargetVersion, StringComparison.Ordinal) ||
            !string.Equals(receipt.TargetTag, preview.TargetTag, StringComparison.Ordinal) ||
            !string.Equals(receipt.PredecessorCommit, preview.PredecessorCommit, StringComparison.OrdinalIgnoreCase) ||
            receipt.Authority.CandidateLaunchAllowed || receipt.Authority.CheckpointAllowed || receipt.Authority.NetworkAccessAllowed ||
            receipt.Authority.CatalogMutationAllowed || receipt.Authority.AgentExecuteAllowed)
            throw new InvalidDataException("Apply/build receipt violated the separate-launch/non-network/non-agent boundary.");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }
}
